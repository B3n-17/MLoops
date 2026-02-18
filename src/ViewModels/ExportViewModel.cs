using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using MLoops.Models;
using MLoops.Services;

namespace MLoops.ViewModels;

public partial class ExportViewModel : ViewModelBase
{
    [ObservableProperty] private bool _canExport;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _statusType = "";
    [ObservableProperty] private string _consoleOutput = "";
    [ObservableProperty] private bool _hasConsoleOutput;
    [ObservableProperty] private bool _canExportToFlac;
    [ObservableProperty] private bool _canExportToOgg;
    [ObservableProperty] private bool _canExportToOpus;

    private readonly AudioExportService _exportService;
    private AudioFileInfo? _sourceFile;
    private CancellationTokenSource? _exportCts;
    private Func<string, string, Task<string?>>? _saveFileCallback;

    public ExportViewModel(FfmpegService ffmpegService)
    {
        _exportService = new AudioExportService(ffmpegService);
    }

    public void SetSourceFile(AudioFileInfo? file)
    {
        _sourceFile = file;
        CanExport = file is not null;

        var isWavOrFlac = file?.FileType is AudioFileType.Wav or AudioFileType.Flac;
        var isTranscodable = file?.FileType is AudioFileType.Wav or AudioFileType.Flac or AudioFileType.Ogg;
        CanExportToFlac = isWavOrFlac;
        CanExportToOgg = isTranscodable;
        CanExportToOpus = isTranscodable;
    }

    public void SetSaveFileCallback(Func<string, string, Task<string?>> callback)
    {
        _saveFileCallback = callback;
    }

    [RelayCommand]
    private async Task ExportToFlac()
    {
        if (_sourceFile is null || _saveFileCallback is null) return;

        var suggestedName = Path.GetFileNameWithoutExtension(_sourceFile.FileName) + ".flac";
        var outputPath = await _saveFileCallback(suggestedName, "FLAC files|*.flac");
        if (outputPath is null) return;

        await RunExportAsync(async (progress, log, ct) =>
        {
            var options = new ExportOptions
            {
                Format = ExportFormat.Flac,
                CompressionLevel = 12,
                SampleRate = "48000",
                BitDepth = "16",
                PreserveMetadata = true,
                KeepForeignMetadata = true
            };
            await _exportService.ExportToFlacAsync(_sourceFile, outputPath, options, progress, log, ct);
        }, "Converting to FLAC...", "FLAC conversion complete!", outputPath);
    }

    [RelayCommand]
    private async Task ExportToOgg()
    {
        if (_sourceFile is null || _saveFileCallback is null) return;

        var suggestedName = Path.GetFileNameWithoutExtension(_sourceFile.FileName) + ".ogg";
        var outputPath = await _saveFileCallback(suggestedName, "OGG files|*.ogg");
        if (outputPath is null) return;

        await RunExportAsync(async (progress, log, ct) =>
        {
            await _exportService.ExportToOggAsync(_sourceFile, outputPath, null, progress, log, ct);
        }, "Converting to OGG (Vorbis) at highest quality...", "OGG conversion complete! Metadata and loop points stored in Vorbis comments.", outputPath);
    }

    [RelayCommand]
    private async Task ExportToOpus()
    {
        if (_sourceFile is null || _saveFileCallback is null) return;

        var suggestedName = Path.GetFileNameWithoutExtension(_sourceFile.FileName) + ".ogg";
        var outputPath = await _saveFileCallback(suggestedName, "OGG files|*.ogg");
        if (outputPath is null) return;

        await RunExportAsync(async (progress, log, ct) =>
        {
            await _exportService.ExportToOpusInOggAsync(_sourceFile, outputPath, false, progress, log, ct);
        }, "Converting to Opus in Ogg (recommended 384k)...", "Opus (Ogg) conversion complete! Metadata and loop points stored in comments.", outputPath);
    }

    [RelayCommand]
    private async Task ExportToOpusMax()
    {
        if (_sourceFile is null || _saveFileCallback is null) return;

        var suggestedName = Path.GetFileNameWithoutExtension(_sourceFile.FileName) + ".ogg";
        var outputPath = await _saveFileCallback(suggestedName, "OGG files|*.ogg");
        if (outputPath is null) return;

        await RunExportAsync(async (progress, log, ct) =>
        {
            await _exportService.ExportToOpusInOggAsync(_sourceFile, outputPath, true, progress, log, ct);
        }, "Converting to Opus in Ogg (max quality)...", "Opus Max (Ogg) conversion complete! Metadata and loop points stored in comments.", outputPath);
    }

    [RelayCommand]
    private void CancelExport()
    {
        _exportCts?.Cancel();
    }

    public void CancelActiveExport()
    {
        _exportCts?.Cancel();
    }

    private async Task RunExportAsync(Func<IProgress<double>, Action<string>, CancellationToken, Task> exportAction, string progressMessage, string successMessage, string? outputPath = null)
    {
        IsExporting = true;
        Progress = 0;
        StatusMessage = progressMessage;
        StatusType = "info";
        ConsoleOutput = "";
        HasConsoleOutput = false;

        _exportCts = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            Progress = p;
        });
        Action<string> logger = AppendConsoleLine;

        if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
            AppendConsoleLine($"[info] Existing file will be overwritten: {outputPath}");

        try
        {
            await exportAction(progress, logger, _exportCts.Token);
            Progress = 100;
            StatusMessage = successMessage;
            StatusType = "success";

            if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
            {
                var bytes = new FileInfo(outputPath).Length;
                AppendConsoleLine($"[info] Wrote file: {outputPath} ({bytes} bytes)");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Export cancelled.";
            StatusType = "warning";
        }
        catch (Exception ex)
        {
            AppendConsoleLine($"[error] {ex.Message}");
            StatusMessage = $"Export failed: {ex.Message}";
            StatusType = "error";
        }
        finally
        {
            IsExporting = false;
            _exportCts?.Dispose();
            _exportCts = null;

            // Auto-clear status after delay
            _ = ClearStatusAfterDelay();
        }
    }

    private void AppendConsoleLine(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            const int maxChars = 60000;
            var next = string.IsNullOrEmpty(ConsoleOutput)
                ? line
                : ConsoleOutput + Environment.NewLine + line;

            if (next.Length > maxChars)
                next = next[^maxChars..];

            ConsoleOutput = next;
            HasConsoleOutput = true;
        });
    }

    private async Task ClearStatusAfterDelay()
    {
        await Task.Delay(5000);
        if (!IsExporting)
        {
            StatusMessage = "";
            StatusType = "";
            Progress = 0;
        }
    }
}
