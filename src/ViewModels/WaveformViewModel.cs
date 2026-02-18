using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MLoops.Models;
using MLoops.Services;

namespace MLoops.ViewModels;

public partial class WaveformViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty] private float[] _minPeaks = Array.Empty<float>();
    [ObservableProperty] private float[] _maxPeaks = Array.Empty<float>();
    [ObservableProperty] private bool[] _clipBuckets = Array.Empty<bool>();
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private double _playheadPosition; // 0.0 to 1.0 normalized
    [ObservableProperty] private double _currentTimeSeconds;
    [ObservableProperty] private string _currentTimeDisplay = "00:00";
    [ObservableProperty] private string _totalTimeDisplay = "00:00";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isLoopEnabled;
    [ObservableProperty] private ObservableCollection<LoopMarkerInfo> _loopMarkers = new();
    [ObservableProperty] private bool _hasFourChannels;
    [ObservableProperty] private double _crossfadeValue;
    [ObservableProperty] private bool _hasWaveform;
    [ObservableProperty] private bool _hasLoopMarkers;

    private readonly AudioPlaybackService _playbackService;
    private string? _currentFilePath;
    private List<LoopPoint> _loopPoints = new();

    public WaveformViewModel(AudioPlaybackService playbackService)
    {
        _playbackService = playbackService;
        _playbackService.PlayheadChanged += OnPlayheadChanged;
        _playbackService.PlaybackEnded += OnPlaybackEnded;
    }

    public void LoadWaveformData(WaveformData data, List<LoopPoint> loopPoints, string filePath, int channels)
    {
        MinPeaks = data.MinPeaks;
        MaxPeaks = data.MaxPeaks;
        ClipBuckets = data.ClipBuckets;
        DurationSeconds = data.DurationSeconds;
        TotalTimeDisplay = AudioMetadataService.FormatTime(data.DurationSeconds);
        CurrentTimeDisplay = "00:00";
        PlayheadPosition = 0;
        CurrentTimeSeconds = 0;
        HasWaveform = data.MinPeaks.Length > 0;
        HasFourChannels = channels >= 4;
        CrossfadeValue = 0;
        _currentFilePath = filePath;
        _loopPoints = loopPoints;

        _playbackService.SetFile(filePath, data.DurationSeconds, data.SampleRate, channels);
        _playbackService.SetChannelCount(channels);
        _playbackService.SetCrossfade(CrossfadeValue);

        // Build loop markers
        LoopMarkers.Clear();
        HasLoopMarkers = false;
        if (data.DurationSeconds > 0)
        {
            var sampleRate = data.SampleRate > 0 ? data.SampleRate : 44100;
            for (var i = 0; i < loopPoints.Count; i++)
            {
                var lp = loopPoints[i];
                var startTime = Math.Min(data.DurationSeconds, lp.Start / (double)sampleRate);
                var endTime = Math.Min(data.DurationSeconds, lp.End / (double)sampleRate);
                var duration = Math.Max(0, endTime - startTime);

                LoopMarkers.Add(new LoopMarkerInfo(lp)
                {
                    LoopLabel = $"Loop {i + 1}",
                    StartNormalized = startTime / data.DurationSeconds,
                    EndNormalized = endTime / data.DurationSeconds,
                    StartTimeDisplay = AudioMetadataService.FormatTime(startTime),
                    EndTimeDisplay = AudioMetadataService.FormatTime(endTime),
                    DurationTimeDisplay = AudioMetadataService.FormatTime(duration),
                    StartSample = lp.Start,
                    EndSample = lp.End,
                    TypeName = lp.TypeName
                });
            }
        }

        HasLoopMarkers = LoopMarkers.Count > 0;
        ApplyLoopConfiguration();
    }

    [RelayCommand]
    private void Play()
    {
        if (_currentFilePath is null) return;
        ApplyLoopConfiguration();
        _playbackService.Play();
        IsPlaying = true;
    }

    [RelayCommand]
    private void Pause()
    {
        _playbackService.Pause();
        IsPlaying = false;
    }

    [RelayCommand]
    private void Stop()
    {
        _playbackService.Stop();
        IsPlaying = false;
        PlayheadPosition = 0;
        CurrentTimeSeconds = 0;
        CurrentTimeDisplay = "00:00";
    }

    public void SeekToNormalized(double normalized)
    {
        if (DurationSeconds <= 0) return;
        var seconds = Math.Max(0, Math.Min(DurationSeconds, normalized * DurationSeconds));
        _playbackService.SeekTo(seconds);
        UpdateTimeDisplay(seconds);
    }

    public void TogglePlayStop()
    {
        if (IsPlaying)
            StopCommand.Execute(null);
        else
            PlayCommand.Execute(null);
    }

    private void OnPlayheadChanged(double seconds)
    {
        if (DurationSeconds <= 0) return;
        PlayheadPosition = seconds / DurationSeconds;
        UpdateTimeDisplay(seconds);
    }

    private void OnPlaybackEnded()
    {
        IsPlaying = false;
    }

    private void UpdateTimeDisplay(double seconds)
    {
        CurrentTimeSeconds = seconds;
        CurrentTimeDisplay = AudioMetadataService.FormatTime(seconds);
    }

    partial void OnCrossfadeValueChanged(double value)
    {
        if (!HasFourChannels)
            return;

        _playbackService.SetCrossfade(value);
    }

    partial void OnIsLoopEnabledChanged(bool value)
    {
        ApplyLoopConfiguration();
    }

    private void ApplyLoopConfiguration()
    {
        if (!IsLoopEnabled || LoopMarkers.Count == 0 || DurationSeconds <= 0)
        {
            _playbackService.SetLoopRegion(false, 0, 0);
            return;
        }

        var marker = LoopMarkers[0];
        var start = marker.StartNormalized * DurationSeconds;
        var end = marker.EndNormalized * DurationSeconds;
        _playbackService.SetLoopRegion(true, start, end);
    }

    public void Dispose()
    {
        _playbackService.PlayheadChanged -= OnPlayheadChanged;
        _playbackService.PlaybackEnded -= OnPlaybackEnded;
    }
}

public class LoopMarkerInfo : ObservableObject
{
    private readonly LoopPoint _sourceLoopPoint;

    public LoopMarkerInfo(LoopPoint sourceLoopPoint)
    {
        _sourceLoopPoint = sourceLoopPoint;
    }

    public double StartNormalized { get; set; }
    public double EndNormalized { get; set; }
    public string StartTimeDisplay { get; set; } = "";
    public string EndTimeDisplay { get; set; } = "";
    public string DurationTimeDisplay { get; set; } = "";
    public uint StartSample { get; set; }
    public uint EndSample { get; set; }
    public string LoopLabel { get; set; } = "Loop 1";
    public string TypeName { get; set; } = "Forward";

    public int LoopCount
    {
        get => _sourceLoopPoint.PlayCount;
        set
        {
            var normalized = value < -1 ? -1 : value;
            if (_sourceLoopPoint.PlayCount == normalized)
                return;

            _sourceLoopPoint.PlayCount = normalized;
            OnPropertyChanged(nameof(LoopCount));
            OnPropertyChanged(nameof(LoopCountText));
            OnPropertyChanged(nameof(LoopCountHint));
        }
    }

    public string LoopCountText
    {
        get => LoopCount.ToString();
        set
        {
            if (!int.TryParse(value, out var parsed))
                return;

            LoopCount = parsed;
        }
    }

    public string LoopCountHint => LoopCount switch
    {
        -1 => "(infinite)",
        0 => "(once)",
        1 => "(twice)",
        _ => $"({LoopCount + 1} passes)"
    };
}
