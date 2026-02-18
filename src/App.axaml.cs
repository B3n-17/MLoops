using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Layout;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MLoops.Services;
using MLoops.ViewModels;
using MLoops.Views;

namespace MLoops;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var args = desktop.Args ?? [];
            var ffmpegPathArg = GetFfmpegPathArg(args);
            if (string.IsNullOrWhiteSpace(ffmpegPathArg))
            {
                var ffmpegConfig = FfmpegService.Resolve(null);
                if (!ffmpegConfig.IsAvailable)
                {
                    ShowMissingFfmpegDialog(desktop, args);
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
            }

            LaunchMainWindow(desktop, args, null);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static string? GetFfmpegPathArg(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--ffmpeg-path=", StringComparison.OrdinalIgnoreCase))
                return arg["--ffmpeg-path=".Length..];
        }

        return null;
    }

    private static void ShowMissingFfmpegDialog(IClassicDesktopStyleApplicationLifetime desktop, string[] args)
    {
        var hintText = new TextBlock
        {
            Text = "FFmpeg was not found in PATH. Select your ffmpeg executable to continue.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var selectedPathBox = new TextBox
        {
            IsReadOnly = true,
            Watermark = "No file selected",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var statusText = new TextBlock
        {
            Foreground = Avalonia.Media.Brushes.Orange,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false
        };

        var browseButton = new Button
        {
            Content = "Browse FFmpeg",
            MinWidth = 130
        };

        var closeButton = new Button
        {
            Content = "Quit",
            MinWidth = 90,
            Background = Avalonia.Media.Brushes.IndianRed
        };

        var dialog = new Window
        {
            Title = "FFmpeg Required",
            Width = 640,
            Height = 260,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "FFmpeg Setup",
                        FontSize = 20,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold
                    },
                    hintText,
                    selectedPathBox,
                    statusText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children =
                        {
                            browseButton,
                            closeButton
                        }
                    }
                }
            }
        };

        browseButton.Click += async (_, _) =>
        {
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select ffmpeg executable",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("FFmpeg executable")
                    {
                        Patterns = OperatingSystem.IsWindows() ? ["ffmpeg.exe"] : ["ffmpeg"]
                    },
                    FilePickerFileTypes.All
                ]
            });

            var selected = files.FirstOrDefault()?.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(selected))
                return;

            selectedPathBox.Text = selected;

            var dir = Path.GetDirectoryName(selected);
            if (string.IsNullOrWhiteSpace(dir))
            {
                statusText.Text = "Could not resolve selected directory.";
                statusText.IsVisible = true;
                return;
            }

            var config = FfmpegService.Resolve(dir);
            if (!config.IsAvailable)
            {
                statusText.Text = "Invalid selection. Please pick the actual ffmpeg executable.";
                statusText.IsVisible = true;
                return;
            }

            statusText.IsVisible = false;
            LaunchMainWindow(desktop, args, dir);
            dialog.Close();
        };

        closeButton.Click += (_, _) => dialog.Close();

        dialog.Closed += (_, _) =>
        {
            if (desktop.MainWindow == dialog)
                desktop.Shutdown(1);
        };

        desktop.MainWindow = dialog;
        dialog.Show();
    }

    private static void LaunchMainWindow(IClassicDesktopStyleApplicationLifetime desktop, string[] args, string? ffmpegDir)
    {
        var runtimeArgs = ffmpegDir is null
            ? args
            : [.. args, $"--ffmpeg-path={ffmpegDir}"];

        var vm = new MainWindowViewModel();
        var mainWindow = new MainWindow
        {
            DataContext = vm,
        };

        desktop.MainWindow = mainWindow;
        desktop.Exit += (_, _) => vm.Dispose();
        _ = vm.InitializeAsync(runtimeArgs);
        mainWindow.Show();
    }
}
