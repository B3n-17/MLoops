using System.Diagnostics;
using Avalonia.Threading;
using NAudio.Wave;

namespace MLoops.Services;

public class AudioPlaybackService : IDisposable
{
    private readonly FfmpegService _ffmpeg;
    private readonly object _sync = new();

    private WaveOutEvent? _waveOut;
    private PlaybackSampleProvider? _sampleProvider;
    private DispatcherTimer? _positionTimer;

    private string? _currentFilePath;
    private double _totalDuration;
    private int _channelCount;
    private int _sampleRate;
    private double _crossfade;

    private bool _loopEnabled;
    private double _loopStartSeconds;
    private double _loopEndSeconds;

    private bool _disposed;

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    public double PlayheadSeconds
    {
        get
        {
            lock (_sync)
            {
                return _sampleProvider?.PositionSeconds ?? 0;
            }
        }
    }

    public double TotalDuration => _totalDuration;

    public event Action<double>? PlayheadChanged;
    public event Action? PlaybackEnded;

    public AudioPlaybackService(FfmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    public void SetFile(string filePath, double duration, int sampleRate, int channels)
    {
        Stop();

        lock (_sync)
        {
            _currentFilePath = filePath;
            _totalDuration = duration;
            _sampleRate = sampleRate > 0 ? sampleRate : 44100;
            _channelCount = Math.Max(1, channels);
            _crossfade = 0;
            _loopEnabled = false;
            _loopStartSeconds = 0;
            _loopEndSeconds = 0;
            _sampleProvider = null;
        }
    }

    public void SetChannelCount(int channelCount)
    {
        lock (_sync)
        {
            _channelCount = Math.Max(1, channelCount);
            _sampleProvider?.SetSourceChannelCount(_channelCount);
        }
    }

    public void SetCrossfade(double crossfade)
    {
        lock (_sync)
        {
            _crossfade = Math.Clamp(crossfade, 0, 1);
            _sampleProvider?.SetCrossfade(_crossfade);
        }
    }

    public void SetLoopRegion(bool enabled, double startSeconds, double endSeconds, int playCount = 0)
    {
        lock (_sync)
        {
            if (!enabled)
            {
                _loopEnabled = false;
                _loopStartSeconds = 0;
                _loopEndSeconds = 0;
                _sampleProvider?.SetLoop(false, 0, 0, 0);
                return;
            }

            var start = Math.Clamp(startSeconds, 0, _totalDuration);
            var end = Math.Clamp(endSeconds, 0, _totalDuration);
            if (end - start < 0.01)
            {
                _loopEnabled = false;
                _loopStartSeconds = 0;
                _loopEndSeconds = 0;
                _sampleProvider?.SetLoop(false, 0, 0, 0);
                return;
            }

            _loopEnabled = true;
            _loopStartSeconds = start;
            _loopEndSeconds = end;
            _sampleProvider?.SetLoop(true, start, end, 0);
        }
    }

    public void Play(double? startSeconds = null)
    {
        if (_ffmpeg.Config.FfmpegPath is null)
            return;

        try
        {
            EnsurePlaybackGraph();
        }
        catch
        {
            IsPlaying = false;
            IsPaused = false;
            return;
        }

        lock (_sync)
        {
            if (_sampleProvider is null || _waveOut is null)
                return;

            if (startSeconds.HasValue)
                _sampleProvider.PositionSeconds = Math.Clamp(startSeconds.Value, 0, _totalDuration);

            _waveOut.Play();
            IsPlaying = true;
            IsPaused = false;
            StartPositionTimer();
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_waveOut is null || !IsPlaying)
                return;

            _waveOut.Pause();
            IsPlaying = false;
            IsPaused = true;
            StopPositionTimer();
            PlayheadChanged?.Invoke(PlayheadSeconds);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_waveOut is not null)
                _waveOut.Stop();

            _sampleProvider?.Reset();
            IsPlaying = false;
            IsPaused = false;
            StopPositionTimer();
            PlayheadChanged?.Invoke(0);
        }
    }

    public void SeekTo(double seconds)
    {
        lock (_sync)
        {
            var position = Math.Clamp(seconds, 0, _totalDuration);
            _sampleProvider?.SetPosition(position);
            PlayheadChanged?.Invoke(position);
        }
    }

    private void EnsurePlaybackGraph()
    {
        lock (_sync)
        {
            if (_sampleProvider is not null && _waveOut is not null)
                return;

            if (_currentFilePath is null || _ffmpeg.Config.FfmpegPath is null)
                return;

            var samples = DecodeToFloatPcm(
                _currentFilePath,
                _sampleRate,
                _channelCount,
                _ffmpeg.Config.FfmpegPath);

            _sampleProvider = new PlaybackSampleProvider(samples, _sampleRate, _channelCount);
            _sampleProvider.SetCrossfade(_crossfade);
            _sampleProvider.SetLoop(_loopEnabled, _loopStartSeconds, _loopEndSeconds, 0);

            _waveOut = new WaveOutEvent
            {
                DesiredLatency = 60,
                NumberOfBuffers = 3
            };
            _waveOut.PlaybackStopped += OnPlaybackStopped;
            _waveOut.Init(_sampleProvider);
        }
    }

    private static float[] DecodeToFloatPcm(string filePath, int sampleRate, int channels, string ffmpegPath)
    {
        var effectiveChannels = Math.Max(1, channels);
        var effectiveSampleRate = Math.Max(1, sampleRate);

        if (effectiveChannels >= 4 &&
            TryDecodeStereoPair(filePath, ffmpegPath, effectiveSampleRate, "c0", "c1", out var pair12, out _) &&
            TryDecodeStereoPair(filePath, ffmpegPath, effectiveSampleRate, "c2", "c3", out var pair34, out _))
        {
            var frameCount = Math.Min(pair12.Length, pair34.Length) / 2;
            var interleaved = new float[frameCount * 4];

            for (var frame = 0; frame < frameCount; frame++)
            {
                var src = frame * 2;
                var dst = frame * 4;

                interleaved[dst] = pair12[src];
                interleaved[dst + 1] = pair12[src + 1];
                interleaved[dst + 2] = pair34[src];
                interleaved[dst + 3] = pair34[src + 1];
            }

            return interleaved;
        }

        var failures = new List<string>();

        if (TryDecodeToFloatPcm(filePath, ffmpegPath, effectiveSampleRate, effectiveChannels, useGuessLayoutMax: true, useMapChannels: true, out var mappedSamples, out var mappedError))
            return mappedSamples;

        failures.Add($"mapped channels: {mappedError}");

        if (TryDecodeToFloatPcm(filePath, ffmpegPath, effectiveSampleRate, effectiveChannels, useGuessLayoutMax: true, useMapChannels: false, out var lockedLayoutSamples, out var lockedLayoutError))
            return lockedLayoutSamples;

        failures.Add($"locked layout: {lockedLayoutError}");

        if (TryDecodeToFloatPcm(filePath, ffmpegPath, effectiveSampleRate, effectiveChannels, useGuessLayoutMax: false, useMapChannels: false, out var defaultLayoutSamples, out var defaultLayoutError))
            return defaultLayoutSamples;

        failures.Add($"default layout: {defaultLayoutError}");
        throw new InvalidOperationException($"FFmpeg decode failed. {string.Join(" | ", failures)}");
    }

    private static bool TryDecodeStereoPair(
        string filePath,
        string ffmpegPath,
        int sampleRate,
        string leftSource,
        string rightSource,
        out float[] samples,
        out string error)
    {
        samples = Array.Empty<float>();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-guess_layout_max");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("f32le");
        psi.ArgumentList.Add("-acodec");
        psi.ArgumentList.Add("pcm_f32le");
        psi.ArgumentList.Add("-af");
        psi.ArgumentList.Add($"pan=stereo|c0={leftSource}|c1={rightSource}");
        psi.ArgumentList.Add("-ac");
        psi.ArgumentList.Add("2");
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add(sampleRate.ToString());
        psi.ArgumentList.Add("pipe:1");

        using var process = Process.Start(psi);
        if (process is null)
        {
            error = "Failed to start FFmpeg process.";
            return false;
        }

        using var ms = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(ms);
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            error = string.IsNullOrWhiteSpace(stderr)
                ? $"Exit code {process.ExitCode}."
                : stderr.Trim();
            return false;
        }

        var bytes = ms.ToArray();
        var sampleCount = bytes.Length / sizeof(float);
        samples = new float[sampleCount];
        Buffer.BlockCopy(bytes, 0, samples, 0, sampleCount * sizeof(float));
        error = string.Empty;
        return true;
    }

    private static bool TryDecodeToFloatPcm(
        string filePath,
        string ffmpegPath,
        int sampleRate,
        int channels,
        bool useGuessLayoutMax,
        bool useMapChannels,
        out float[] samples,
        out string error)
    {
        samples = Array.Empty<float>();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        if (useGuessLayoutMax)
        {
            psi.ArgumentList.Add("-guess_layout_max");
            psi.ArgumentList.Add("0");
        }
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(filePath);
        if (useMapChannels)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                psi.ArgumentList.Add("-map_channel");
                psi.ArgumentList.Add($"0.0.{ch}");
            }
        }
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("f32le");
        psi.ArgumentList.Add("-acodec");
        psi.ArgumentList.Add("pcm_f32le");
        psi.ArgumentList.Add("-ac");
        psi.ArgumentList.Add(channels.ToString());
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add(sampleRate.ToString());
        psi.ArgumentList.Add("pipe:1");

        using var process = Process.Start(psi);
        if (process is null)
        {
            error = "Failed to start FFmpeg process.";
            return false;
        }

        using var ms = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(ms);
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            error = string.IsNullOrWhiteSpace(stderr)
                ? $"Exit code {process.ExitCode}."
                : stderr.Trim();
            return false;
        }

        var bytes = ms.ToArray();
        var sampleCount = bytes.Length / sizeof(float);
        samples = new float[sampleCount];
        Buffer.BlockCopy(bytes, 0, samples, 0, sampleCount * sizeof(float));
        error = string.Empty;
        return true;
    }

    private void StartPositionTimer()
    {
        _positionTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        if (_positionTimer is null)
            return;

        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (!IsPlaying)
            return;

        var pos = PlayheadSeconds;
        PlayheadChanged?.Invoke(pos);

        if (!_loopEnabled && pos >= _totalDuration)
        {
            Stop();
            PlaybackEnded?.Invoke();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_loopEnabled)
                return;

            if (IsPlaying)
            {
                IsPlaying = false;
                IsPaused = false;
                StopPositionTimer();
                PlaybackEnded?.Invoke();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopPositionTimer();

        lock (_sync)
        {
            if (_waveOut is not null)
            {
                _waveOut.PlaybackStopped -= OnPlaybackStopped;
                _waveOut.Dispose();
                _waveOut = null;
            }

            _sampleProvider = null;
        }

        GC.SuppressFinalize(this);
    }

    private sealed class PlaybackSampleProvider : ISampleProvider
    {
        private readonly float[] _sourceSamples;
        private readonly int _sampleRate;

        private int _sourceChannels;
        private long _framePosition;
        private double _crossfade;

        private bool _loopEnabled;
        private long _loopStartFrame;
        private long _loopEndFrame;

        public PlaybackSampleProvider(float[] sourceSamples, int sampleRate, int sourceChannels)
        {
            _sourceSamples = sourceSamples;
            _sampleRate = Math.Max(1, sampleRate);
            _sourceChannels = Math.Max(1, sourceChannels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public double PositionSeconds
        {
            get => _framePosition / (double)_sampleRate;
            set => SetPosition(value);
        }

        public void SetSourceChannelCount(int channels)
        {
            _sourceChannels = Math.Max(1, channels);
        }

        public void SetCrossfade(double crossfade)
        {
            _crossfade = Math.Clamp(crossfade, 0, 1);
        }

        public void SetLoop(bool enabled, double startSeconds, double endSeconds, int playCount)
        {
            if (!enabled)
            {
                _loopEnabled = false;
                _loopStartFrame = 0;
                _loopEndFrame = 0;
                return;
            }

            _loopStartFrame = Math.Max(0, (long)(startSeconds * _sampleRate));
            _loopEndFrame = Math.Max(_loopStartFrame + 1, (long)(endSeconds * _sampleRate));
            _loopEnabled = true;

            if (_framePosition < _loopStartFrame || _framePosition >= _loopEndFrame)
                _framePosition = _loopStartFrame;
        }

        public void SetPosition(double seconds)
        {
            var frame = (long)(Math.Max(0, seconds) * _sampleRate);
            var maxFrame = _sourceSamples.Length / _sourceChannels;
            _framePosition = Math.Min(frame, maxFrame);
        }

        public void Reset()
        {
            _framePosition = 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var framesRequested = count / 2;
            var framesWritten = 0;
            var maxFrames = _sourceSamples.Length / _sourceChannels;

            while (framesWritten < framesRequested)
            {
                if (_loopEnabled && _framePosition >= _loopEndFrame)
                    _framePosition = _loopStartFrame;

                if (_framePosition >= maxFrames)
                    break;

                var srcBase = (int)(_framePosition * _sourceChannels);
                float left;
                float right;

                if (_sourceChannels >= 4)
                {
                    var a12 = (float)(1 - _crossfade);
                    var a34 = (float)_crossfade;

                    left = a12 * _sourceSamples[srcBase] + a34 * _sourceSamples[srcBase + 2];
                    right = a12 * _sourceSamples[srcBase + 1] + a34 * _sourceSamples[srcBase + 3];
                }
                else if (_sourceChannels == 1)
                {
                    left = _sourceSamples[srcBase];
                    right = left;
                }
                else
                {
                    left = _sourceSamples[srcBase];
                    right = _sourceSamples[srcBase + 1];
                }

                var dest = offset + framesWritten * 2;
                buffer[dest] = left;
                buffer[dest + 1] = right;

                _framePosition++;
                framesWritten++;
            }

            var samplesWritten = framesWritten * 2;
            if (samplesWritten < count)
                Array.Clear(buffer, offset + samplesWritten, count - samplesWritten);

            return samplesWritten;
        }
    }
}
