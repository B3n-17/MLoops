using System.Linq;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using MLoops.ViewModels;

namespace MLoops.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);

        Loaded += OnLoaded;
        Closing += OnClosing;
        AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Wire up file picker callback
        vm.OpenFileCallback = OpenFilePickerAsync;

        // Wire up export save file callback
        vm.Export.SetSaveFileCallback(SaveFilePickerAsync);

        // Wire up waveform seek
        var waveform = this.FindControl<WaveformControl>("WaveformDisplay");
        if (waveform is not null)
        {
            waveform.SeekRequested += normalized =>
            {
                vm.Waveform.SeekToNormalized(normalized);
            };
        }

        vm.Export.PropertyChanged -= OnExportPropertyChanged;
        vm.Export.PropertyChanged += OnExportPropertyChanged;
    }

    private async Task<string?> OpenFilePickerAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Audio File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio Files")
                {
                    Patterns = ["*.wav", "*.flac", "*.ogg", "*.oga"]
                },
                FilePickerFileTypes.All
            ]
        });

        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<string?> SaveFilePickerAsync(string suggestedName, string filter)
    {
        var extension = System.IO.Path.GetExtension(suggestedName).TrimStart('.');

        IStorageFolder? startFolder = null;
        if (DataContext is MainWindowViewModel vm)
        {
            var sourcePath = vm.CurrentFile?.FilePath;
            var sourceDirectory = sourcePath is null ? null : System.IO.Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(sourceDirectory) && System.IO.Directory.Exists(sourceDirectory))
            {
                var folderUri = new Uri(sourceDirectory + System.IO.Path.DirectorySeparatorChar);
                startFolder = await StorageProvider.TryGetFolderFromPathAsync(folderUri);
            }
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Audio File",
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = startFolder,
            FileTypeChoices =
            [
                new FilePickerFileType(extension.ToUpperInvariant() + " Files")
                {
                    Patterns = [$"*.{extension}"]
                }
            ]
        });

        return file?.Path.LocalPath;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        vm.IsDragOver = false;

        var first = e.DataTransfer.Items
            .Select(item => item.TryGetFile())
            .FirstOrDefault(f => f is not null);

        if (first is not null)
        {
            var filePath = first.Path.LocalPath;
            if (!string.IsNullOrEmpty(filePath))
            {
                await vm.HandleFileDrop(filePath);
            }
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsDragOver = true;

        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDragLeave(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsDragOver = false;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Export.PropertyChanged -= OnExportPropertyChanged;

        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }

    private void OnExportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExportViewModel.ConsoleOutput))
            return;

        var console = this.FindControl<TextBox>("ExportConsoleTextBox");
        if (console is null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            var length = console.Text?.Length ?? 0;
            console.CaretIndex = length;
            console.SelectionStart = length;
            console.SelectionEnd = length;
        }, DispatcherPriority.Background);
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Source is TextBox)
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        if (!vm.IsFileLoaded || !vm.Waveform.HasWaveform)
            return;

        vm.Waveform.TogglePlayStop();
        e.Handled = true;
    }

    private static void OnGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (e.Text == " ")
        {
            e.Handled = true;
            return;
        }

        base.OnTextInput(e);
    }
}
