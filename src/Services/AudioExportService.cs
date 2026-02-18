using System.Text.Json;
using MLoops.Models;

namespace MLoops.Services;

public class AudioExportService
{
    private readonly FfmpegService _ffmpeg;

    public AudioExportService(FfmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    public async Task ExportToFlacAsync(
        AudioFileInfo source, string outputPath, ExportOptions options,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "-i", source.FilePath,
            "-map", "0:a:0",
            "-vn", "-sn", "-dn",
            "-c:a", "flac",
            "-compression_level", options.CompressionLevel.ToString(),
            "-lpc_type", "cholesky",
            "-lpc_passes", "3",
            "-prediction_order_method", "search"
        };

        var requestedSampleRate = options.SampleRate;
        if (string.Equals(requestedSampleRate, "original", StringComparison.OrdinalIgnoreCase))
            requestedSampleRate = source.SampleRate > 0 ? source.SampleRate.ToString() : null;

        if (!string.IsNullOrWhiteSpace(requestedSampleRate))
            args.AddRange(["-ar", requestedSampleRate]);

        if (!string.IsNullOrWhiteSpace(requestedSampleRate) && !string.Equals(requestedSampleRate, source.SampleRate.ToString(), StringComparison.Ordinal))
            args.AddRange(["-af", "aresample=resampler=soxr:precision=33:dither_method=triangular"]);

        var requestedBitDepth = options.BitDepth;
        if (string.Equals(requestedBitDepth, "original", StringComparison.OrdinalIgnoreCase))
            requestedBitDepth = source.BitsPerSample > 0 ? source.BitsPerSample.ToString() : null;

        if (!string.IsNullOrWhiteSpace(requestedBitDepth))
        {
            switch (requestedBitDepth)
            {
                case "8":
                    args.AddRange(["-sample_fmt", "u8"]);
                    break;
                case "16":
                    args.AddRange(["-sample_fmt", "s16"]);
                    break;
                case "24":
                    args.AddRange(["-sample_fmt", "s32", "-bits_per_raw_sample", "24"]);
                    break;
            }
        }

        if (options.PreserveMetadata)
        {
            args.AddRange(["-map_metadata", "0"]);
            AddMetadataArgs(args, source);
        }
        else
        {
            args.AddRange(["-map_metadata", "-1"]);
        }

        // FLAC does not support arbitrary RIFF foreign chunks as-is.
        // KeepForeignMetadata is limited to textual tags already added above.
        _ = options.KeepForeignMetadata;

        args.AddRange(["-f", "flac"]);
        args.Add(outputPath);

        var duration = source.DurationSeconds > 0 ? TimeSpan.FromSeconds(source.DurationSeconds) : (TimeSpan?)null;
        var exitCode = await _ffmpeg.RunFfmpegAsync(args, p => progress?.Report(p), duration, log, ct);

        if (exitCode != 0)
            throw new InvalidOperationException($"FFmpeg FLAC export failed with exit code {exitCode}");
    }

    public async Task ExportToOggAsync(
        AudioFileInfo source, string outputPath, ExportOptions? options = null,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var args = BuildOggArgs(source, outputPath, engineFallback: false);

        var duration = source.DurationSeconds > 0 ? TimeSpan.FromSeconds(source.DurationSeconds) : (TimeSpan?)null;
        var exitCode = await _ffmpeg.RunFfmpegAsync(args, p => progress?.Report(p), duration, log, ct);

        if (exitCode != 0)
        {
            // Retry with engine-agnostic aresample
            var retryArgs = BuildOggArgs(source, outputPath, engineFallback: true);
            exitCode = await _ffmpeg.RunFfmpegAsync(retryArgs, p => progress?.Report(p), duration, log, ct);

            if (exitCode != 0)
                throw new InvalidOperationException($"FFmpeg OGG export failed with exit code {exitCode}");
        }
    }

    public async Task ExportToOpusInOggAsync(
        AudioFileInfo source, string outputPath,
        bool maxQuality = false,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var args = BuildOpusInOggArgs(source, outputPath, maxQuality, engineFallback: false);

        var duration = source.DurationSeconds > 0 ? TimeSpan.FromSeconds(source.DurationSeconds) : (TimeSpan?)null;
        var exitCode = await _ffmpeg.RunFfmpegAsync(args, p => progress?.Report(p), duration, log, ct);

        if (exitCode != 0)
        {
            var retryArgs = BuildOpusInOggArgs(source, outputPath, maxQuality, engineFallback: true);
            exitCode = await _ffmpeg.RunFfmpegAsync(retryArgs, p => progress?.Report(p), duration, log, ct);

            if (exitCode != 0)
                throw new InvalidOperationException($"FFmpeg Opus (Ogg) export failed with exit code {exitCode}");
        }
    }

    public async Task ExportToWavAsync(
        AudioFileInfo source, string outputPath, ExportOptions options,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var args = new List<string> { "-i", source.FilePath };

        if (options.SampleRate != "original")
        {
            args.AddRange(["-ar", options.SampleRate]);
            args.AddRange(["-af", "aresample=resampler=swr:precision=33:dither_method=triangular"]);
        }

        var codec = options.BitDepth switch
        {
            "24" => "pcm_s24le",
            "8" => "pcm_u8",
            _ => "pcm_s16le"
        };

        if (options.BitDepth != "original")
        {
            var fmt = options.BitDepth switch
            {
                "24" => "s24",
                "16" => "s16",
                "8" => "u8",
                _ => (string?)null
            };
            if (fmt is not null)
                args.AddRange(["-sample_fmt", fmt]);
        }

        args.AddRange(["-c:a", codec]);

        if (options.PreserveMetadata)
        {
            args.AddRange(["-map_metadata", "0"]);
            AddMetadataArgs(args, source);
        }

        args.Add(outputPath);

        var duration = source.DurationSeconds > 0 ? TimeSpan.FromSeconds(source.DurationSeconds) : (TimeSpan?)null;
        var exitCode = await _ffmpeg.RunFfmpegAsync(args, p => progress?.Report(p), duration, log, ct);

        if (exitCode != 0)
            throw new InvalidOperationException($"FFmpeg WAV export failed with exit code {exitCode}");
    }

    private static List<string> BuildOggArgs(AudioFileInfo source, string outputPath, bool engineFallback)
    {
        var args = new List<string> { "-i", source.FilePath, "-ar", "48000" };

        if (engineFallback)
            args.AddRange(["-af", "aresample=precision=33:dither_method=triangular"]);
        else
            args.AddRange(["-af", "aresample=resampler=swr:precision=33:dither_method=triangular"]);

        args.AddRange(["-c:a", "libvorbis", "-qscale:a", "10"]);

        AddMetadataArgs(args, source);
        args.Add(outputPath);
        return args;
    }

    private static List<string> BuildOpusInOggArgs(AudioFileInfo source, string outputPath, bool maxQuality, bool engineFallback)
    {
        var args = new List<string>
        {
            "-i", source.FilePath,
            "-map", "0:a:0",
            "-vn", "-sn", "-dn",
            "-ar", "48000"
        };

        if (engineFallback)
            args.AddRange(["-af", "aresample=precision=33"]);
        else
            args.AddRange(["-af", "aresample=resampler=soxr:precision=33"]);

        var targetBitrate = maxQuality
            ? source.Channels switch
            {
                <= 1 => "256k",
                2 => "510k",
                <= 6 => "768k",
                _ => "1024k"
            }
            : "384k";

        args.AddRange([
            "-c:a", "libopus",
            "-application", "audio",
            "-vbr", "on",
            "-compression_level", "10",
            "-frame_duration", "20",
            "-b:a", targetBitrate
        ]);

        if (source.Channels > 2)
        {
            // RFC 7845 family 1 is the interoperable multichannel mapping.
            // Do not force -ac here, so FFmpeg keeps the source channel layout
            // (e.g. quad) instead of defaulting to 4.0 and breaking libopus mapping.
            args.AddRange(["-mapping_family", "1"]);
        }

        AddMetadataArgs(args, source);
        args.AddRange(["-f", "ogg"]);
        args.Add(outputPath);
        return args;
    }

    private static void AddMetadataArgs(List<string> args, AudioFileInfo source)
    {
        // Add standard metadata tags
        foreach (var tag in source.Tags)
        {
            var keyUpper = tag.Key.ToUpperInvariant();
            if (keyUpper is "TITLE" or "ARTIST" or "ALBUM" or "DATE" or "COMMENT" or "GENRE")
            {
                args.AddRange(["-metadata", $"{tag.Key.ToLowerInvariant()}={tag.Value}"]);
            }
            // Also check WAV INFO tags
            var infoName = AudioMetadataService.GetInfoTagName(tag.Key).ToLowerInvariant();
            if (infoName is "title" or "artist" or "album" or "genre" or "comments")
            {
                var metaKey = infoName == "comments" ? "comment" : infoName;
                args.AddRange(["-metadata", $"{metaKey}={tag.Value}"]);
            }
        }

        // Cue points as JSON
        if (source.CuePoints.Count > 0)
        {
            var cuePoints = source.CuePoints.Select(c => new CuePointMetadataEntry
            {
                Id = c.Id,
                Position = c.Position,
                FccChunk = c.FccChunk,
                ChunkStart = c.ChunkStart,
                BlockStart = c.BlockStart,
                SampleOffset = c.SampleOffset
            }).ToArray();

            args.AddRange([
                "-metadata",
                $"CUE_POINTS={JsonSerializer.Serialize(cuePoints, MLoopsJsonSerializerContext.Default.CuePointMetadataEntryArray)}"
            ]);
        }

        // Loop points as JSON + individual tags
        if (source.LoopPoints.Count > 0)
        {
            var loopPoints = source.LoopPoints.Select(lp => new LoopPointMetadataEntry
            {
                CuePointId = lp.CuePointId,
                Type = lp.Type,
                Start = lp.Start,
                End = lp.End,
                Fraction = lp.Fraction,
                PlayCount = lp.PlayCount
            }).ToArray();

            args.AddRange([
                "-metadata",
                $"LOOP_POINTS={JsonSerializer.Serialize(loopPoints, MLoopsJsonSerializerContext.Default.LoopPointMetadataEntryArray)}"
            ]);
            var lp = source.LoopPoints[0];
            args.AddRange(["-metadata", $"LOOP_START={lp.Start}"]);
            args.AddRange(["-metadata", $"LOOP_END={lp.End}"]);
            args.AddRange(["-metadata", $"LOOP_PLAY_COUNT={lp.PlayCount}"]);
        }
    }
}
