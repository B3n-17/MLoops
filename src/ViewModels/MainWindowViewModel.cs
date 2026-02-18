using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MLoops.Models;
using MLoops.Services;

namespace MLoops.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty] private AudioFileInfo? _currentFile;
    [ObservableProperty] private bool _isFileLoaded;
    [ObservableProperty] private bool _isFfmpegAvailable;
    [ObservableProperty] private string _ffmpegStatusText = "Checking FFmpeg...";
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private string _statusType = "";
    [ObservableProperty] private bool _isDragOver;
    [ObservableProperty] private bool _isLoading;

    public MetadataViewModel Metadata { get; }
    public WaveformViewModel Waveform { get; }
    public ExportViewModel Export { get; }

    private readonly FfmpegService _ffmpegService;
    private readonly AudioPlaybackService _playbackService;
    private readonly WaveformService _waveformService;
    private bool _disposed;

    // Callback set by the View for opening file picker
    public Func<Task<string?>>? OpenFileCallback { get; set; }

    public MainWindowViewModel()
    {
        _ffmpegService = new FfmpegService();
        _playbackService = new AudioPlaybackService(_ffmpegService);
        _waveformService = new WaveformService(_ffmpegService);
        Metadata = new MetadataViewModel();
        Waveform = new WaveformViewModel(_playbackService);
        Export = new ExportViewModel(_ffmpegService);
    }

    public async Task InitializeAsync(string[] args)
    {
        // Parse --ffmpeg-path=... from args
        string? ffmpegPath = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--ffmpeg-path=", StringComparison.OrdinalIgnoreCase))
            {
                ffmpegPath = arg["--ffmpeg-path=".Length..];
            }
        }

        await _ffmpegService.InitializeAsync(ffmpegPath);

        IsFfmpegAvailable = _ffmpegService.IsAvailable;
        if (_ffmpegService.IsAvailable)
        {
            var sourceStr = _ffmpegService.Config.Source switch
            {
                FfmpegSource.CommandLineArgument => "CLI argument",
                FfmpegSource.AppLocalBundle => "app folder",
                FfmpegSource.SystemPath => "system PATH",
                _ => "unknown"
            };
            FfmpegStatusText = $"FFmpeg ready ({sourceStr})";
            if (_ffmpegService.Config.Version is not null)
            {
                var ver = _ffmpegService.Config.Version;
                if (ver.StartsWith("ffmpeg version "))
                    ver = ver["ffmpeg version ".Length..];
                var spaceIdx = ver.IndexOf(' ');
                if (spaceIdx > 0)
                    ver = ver[..spaceIdx];
                FfmpegStatusText = $"FFmpeg {ver} ({sourceStr})";
            }
        }
        else
        {
            FfmpegStatusText = "FFmpeg not found. Put it in ./ffmpeg, add it to PATH, or pass --ffmpeg-path=<dir>";
        }
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        if (OpenFileCallback is null) return;
        var filePath = await OpenFileCallback();
        if (filePath is not null)
            await LoadFile(filePath);
    }

    public async Task HandleFileDrop(string filePath)
    {
        await LoadFile(filePath);
    }

    private async Task LoadFile(string filePath)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading file...";
            StatusType = "info";

            // Stop any current playback
            Waveform.StopCommand.Execute(null);

            // Parse metadata
            var audioInfo = await Task.Run(() => AudioMetadataService.ParseFile(filePath));
            CurrentFile = audioInfo;
            IsFileLoaded = true;

            // Populate metadata view
            Metadata.LoadFromAudioFile(audioInfo);

            // Configure export
            Export.SetSourceFile(audioInfo);

            // Generate waveform if FFmpeg is available
            if (_ffmpegService.IsAvailable)
            {
                try
                {
                    StatusMessage = "Generating waveform...";
                    var waveformData = await _waveformService.GenerateWaveformAsync(
                        filePath,
                        audioInfo.SampleRate,
                        audioInfo.Channels,
                        audioInfo.DurationSeconds);

                    if (audioInfo.DurationSeconds > 0)
                        waveformData.DurationSeconds = audioInfo.DurationSeconds;

                    if (audioInfo.SampleRate > 0)
                        waveformData.SampleRate = audioInfo.SampleRate;

                    Waveform.LoadWaveformData(waveformData, audioInfo.LoopPoints, filePath, audioInfo.Channels);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Waveform generation failed: {ex.Message}");
                }
            }

            StatusMessage = $"Loaded: {audioInfo.FileName}";
            StatusType = "success";

            _ = ClearStatusAfterDelay();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            StatusType = "error";
            IsFileLoaded = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ClearStatusAfterDelay()
    {
        await Task.Delay(3000);
        if (StatusType == "success")
        {
            StatusMessage = "Ready.";
            StatusType = "";
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Export.CancelActiveExport();
        Waveform.Dispose();
        _playbackService.Dispose();
        GC.SuppressFinalize(this);
    }
}
