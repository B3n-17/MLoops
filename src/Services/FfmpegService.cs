using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Linq;
using MLoops.Models;

namespace MLoops.Services;

public partial class FfmpegService
{
    private FfmpegConfig _config = new();

    public FfmpegConfig Config => _config;
    public bool IsAvailable => _config.IsAvailable;

    public async Task InitializeAsync(string? cliPathArgument = null)
    {
        _config = Resolve(cliPathArgument);

        if (_config.IsAvailable && _config.FfmpegPath is not null)
        {
            _config.Version = await GetVersionAsync(_config.FfmpegPath);
        }
    }

    public static FfmpegConfig Resolve(string? cliPathArgument)
    {
        // 1. CLI argument (--ffmpeg-path=<dir>)
        if (!string.IsNullOrEmpty(cliPathArgument))
        {
            var config = TryResolveFromPath(cliPathArgument, FfmpegSource.CommandLineArgument);
            if (config is not null) return config;
        }

        // 2. App-local folder: <app-dir>/ffmpeg/
        var appDir = AppContext.BaseDirectory;
        var localCandidates = new[]
        {
            appDir,
            Path.Combine(appDir, "ffmpeg"),
            Path.Combine(appDir, "ffmpeg", "bin")
        };

        foreach (var candidate in localCandidates)
        {
            var config = TryResolveFromDirectory(candidate, FfmpegSource.AppLocalBundle);
            if (config is not null) return config;
        }

        // 3. System PATH
        var systemConfig = TryResolveFromSystemPath();
        if (systemConfig is not null) return systemConfig;

        return new FfmpegConfig { IsAvailable = false, Source = FfmpegSource.NotFound };
    }

    private static FfmpegConfig? TryResolveFromPath(string pathOrDir, FfmpegSource source)
    {
        if (string.IsNullOrWhiteSpace(pathOrDir))
            return null;

        if (File.Exists(pathOrDir))
        {
            var fileName = Path.GetFileName(pathOrDir);
            var expected = GetExeName("ffmpeg");
            if (string.Equals(fileName, expected, StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(pathOrDir);
                if (!string.IsNullOrWhiteSpace(dir))
                    return TryResolveFromDirectory(dir, source);
            }
        }

        return TryResolveFromDirectory(pathOrDir, source);
    }

    private static FfmpegConfig? TryResolveFromDirectory(string directory, FfmpegSource source)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        var ffmpegExe = GetExeName("ffmpeg");
        var ffprobeExe = GetExeName("ffprobe");
        var ffplayExe = GetExeName("ffplay");

        var ffmpegPath = Path.Combine(directory, ffmpegExe);
        if (!File.Exists(ffmpegPath)) return null;

        return new FfmpegConfig
        {
            FfmpegPath = ffmpegPath,
            FfprobePath = File.Exists(Path.Combine(directory, ffprobeExe)) ? Path.Combine(directory, ffprobeExe) : null,
            FfplayPath = File.Exists(Path.Combine(directory, ffplayExe)) ? Path.Combine(directory, ffplayExe) : null,
            IsAvailable = true,
            Source = source
        };
    }

    private static FfmpegConfig? TryResolveFromSystemPath()
    {
        var ffmpegExe = GetExeName("ffmpeg");

        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in pathDirs)
        {
            var ffmpegPath = Path.Combine(dir, ffmpegExe);
            if (File.Exists(ffmpegPath))
            {
                var ffprobeExe = GetExeName("ffprobe");
                var ffplayExe = GetExeName("ffplay");
                return new FfmpegConfig
                {
                    FfmpegPath = ffmpegPath,
                    FfprobePath = File.Exists(Path.Combine(dir, ffprobeExe)) ? Path.Combine(dir, ffprobeExe) : null,
                    FfplayPath = File.Exists(Path.Combine(dir, ffplayExe)) ? Path.Combine(dir, ffplayExe) : null,
                    IsAvailable = true,
                    Source = FfmpegSource.SystemPath
                };
            }
        }

        return null;
    }

    private static string GetExeName(string baseName)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{baseName}.exe" : baseName;
    }

    private static async Task<string?> GetVersionAsync(string ffmpegPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            // Drain stderr concurrently to prevent deadlock
            var stderrTask = process.StandardError.ReadToEndAsync();
            var output = await process.StandardOutput.ReadLineAsync();
            await stderrTask;
            await process.WaitForExitAsync();

            // First line looks like: "ffmpeg version 7.1-full_build-..."
            return output;
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> RunFfmpegAsync(
        IEnumerable<string> arguments,
        Action<double>? progressCallback = null,
        TimeSpan? totalDuration = null,
        Action<string>? logCallback = null,
        CancellationToken ct = default)
    {
        if (_config.FfmpegPath is null)
            throw new InvalidOperationException("FFmpeg is not available.");

        var psi = new ProcessStartInfo
        {
            FileName = _config.FfmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Add -y to overwrite output files without asking
        psi.ArgumentList.Add("-y");
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        if (logCallback is not null)
        {
            var argsForDisplay = string.Join(" ", psi.ArgumentList.Select(QuoteIfNeeded));
            logCallback($"$ ffmpeg {argsForDisplay}");
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start FFmpeg process.");

        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        // Parse progress from stderr
        var totalSeconds = totalDuration?.TotalSeconds ?? 0;
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync(ct) is { } line)
                {
                    logCallback?.Invoke(line);

                    if (totalSeconds > 0 && progressCallback is not null)
                    {
                        var match = TimeRegex().Match(line);
                        if (match.Success && TimeSpan.TryParse(match.Groups[1].Value, out var ts))
                        {
                            var pct = Math.Min(100.0, ts.TotalSeconds / totalSeconds * 100.0);
                            progressCallback(pct);
                        }
                    }
                }
            }
            catch { }
        }, ct);

        // Drain stdout concurrently to prevent deadlock
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);

        await stdoutTask;
        await process.WaitForExitAsync(ct);
        logCallback?.Invoke($"[ffmpeg exit code: {process.ExitCode}]");
        return process.ExitCode;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
    }

    public async Task<string> RunFfprobeAsync(IEnumerable<string> arguments, CancellationToken ct = default)
    {
        if (_config.FfprobePath is null)
            throw new InvalidOperationException("FFprobe is not available.");

        var psi = new ProcessStartInfo
        {
            FileName = _config.FfprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start FFprobe process.");

        // Drain stderr concurrently to prevent deadlock
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await stderrTask;
        await process.WaitForExitAsync(ct);
        return output;
    }

    [GeneratedRegex(@"time=(\d{2}:\d{2}:\d{2}\.\d+)")]
    private static partial Regex TimeRegex();
}
