using MLoops.Models;

namespace MLoops.Services;

public class WaveformData
{
    public float[] MinPeaks { get; set; } = Array.Empty<float>();
    public float[] MaxPeaks { get; set; } = Array.Empty<float>();
    public bool[] ClipBuckets { get; set; } = Array.Empty<bool>();
    public double DurationSeconds { get; set; }
    public int SampleRate { get; set; }
}

public class WaveformService
{
    private readonly FfmpegService _ffmpeg;

    public WaveformService(FfmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    /// <summary>
    /// Decode audio to PCM and compute waveform + true clip buckets.
    /// </summary>
    public async Task<WaveformData> GenerateWaveformAsync(
        string filePath,
        int sampleRate,
        int channels,
        double durationSeconds,
        int targetWidth = 800,
        CancellationToken ct = default)
    {
        if (_ffmpeg.Config.FfmpegPath is null)
            throw new InvalidOperationException("FFmpeg is not available.");

        var effectiveSampleRate = sampleRate > 0 ? sampleRate : 48000;
        var effectiveChannels = channels > 0 ? channels : 2;
        var effectiveDuration = durationSeconds > 0 ? durationSeconds : 0;

        // Use ffmpeg to decode to mono float32 raw PCM piped to stdout
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _ffmpeg.Config.FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Decode to source-rate/source-channel f32le so clipping detection
        // uses actual sample numbers, not resampled/downmixed values.
        foreach (var arg in new[]
        {
            "-guess_layout_max", "0",
            "-i", filePath,
            "-f", "f32le",
            "-acodec", "pcm_f32le",
            "-ac", effectiveChannels.ToString(),
            "-ar", effectiveSampleRate.ToString(),
            "pipe:1"
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start FFmpeg for waveform.");

        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        // Drain stderr concurrently to prevent deadlock
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        var width = Math.Max(1, targetWidth);
        var minPeaks = Enumerable.Repeat(1.0f, width).ToArray();
        var maxPeaks = Enumerable.Repeat(-1.0f, width).ToArray();
        var clipBuckets = new bool[width];
        var bucketTouched = new bool[width];

        var estimatedTotalFrames = effectiveDuration > 0
            ? Math.Max(1L, (long)Math.Round(effectiveDuration * effectiveSampleRate))
            : 1L;

        // Read float32 samples from stdout (preserve byte alignment across reads)
        var buffer = new byte[4096];
        var carry = new byte[4];
        var carryCount = 0;
        var frameIndex = 0L;
        var channelIndex = 0;
        var frameAccum = 0f;
        var frameClip = false;

        while (true)
        {
            var bytesRead = await process.StandardOutput.BaseStream.ReadAsync(buffer, ct);
            if (bytesRead <= 0)
                break;

            var offset = 0;

            if (carryCount > 0)
            {
                var needed = 4 - carryCount;
                var take = Math.Min(needed, bytesRead);
                Buffer.BlockCopy(buffer, 0, carry, carryCount, take);
                carryCount += take;
                offset += take;

                if (carryCount == 4)
                {
                    ConsumeSample(BitConverter.ToSingle(carry, 0));
                    carryCount = 0;
                }
            }

            var remaining = bytesRead - offset;
            var alignedBytes = remaining - (remaining % 4);
            for (var i = 0; i < alignedBytes; i += 4)
            {
                ConsumeSample(BitConverter.ToSingle(buffer, offset + i));
            }

            var tail = remaining - alignedBytes;
            if (tail > 0)
            {
                Buffer.BlockCopy(buffer, offset + alignedBytes, carry, 0, tail);
                carryCount = tail;
            }
        }

        await stderrTask;
        await process.WaitForExitAsync(ct);

        if (!bucketTouched.Any(x => x))
            return new WaveformData();

        for (var x = 0; x < width; x++)
        {
            if (!bucketTouched[x])
            {
                minPeaks[x] = 0;
                maxPeaks[x] = 0;
            }
        }

        return new WaveformData
        {
            MinPeaks = minPeaks,
            MaxPeaks = maxPeaks,
            ClipBuckets = clipBuckets,
            DurationSeconds = effectiveDuration > 0
                ? effectiveDuration
                : frameIndex / (double)effectiveSampleRate,
            SampleRate = effectiveSampleRate
        };

        void ConsumeSample(float value)
        {
            if (!float.IsFinite(value))
                value = 0;

            frameAccum += value;
            if (value > 1.0f || value < -1.0f)
                frameClip = true;

            channelIndex++;
            if (channelIndex < effectiveChannels)
                return;

            channelIndex = 0;
            var mono = frameAccum / effectiveChannels;
            frameAccum = 0;

            var bucket = (int)Math.Min(width - 1, frameIndex * width / estimatedTotalFrames);
            if (!bucketTouched[bucket])
            {
                minPeaks[bucket] = mono;
                maxPeaks[bucket] = mono;
                bucketTouched[bucket] = true;
            }
            else
            {
                if (mono < minPeaks[bucket]) minPeaks[bucket] = mono;
                if (mono > maxPeaks[bucket]) maxPeaks[bucket] = mono;
            }

            if (frameClip)
                clipBuckets[bucket] = true;

            frameClip = false;
            frameIndex++;
        }
    }
}
