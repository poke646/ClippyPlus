using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ClippyPlus.App.Services;

namespace ClippyPlus.App;

public partial class App : Application
{
    private TrayAppContext? _trayAppContext;
    private Window? _clipboardHost;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeService.Apply(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _clipboardHost = CreateClipboardHost();
            // Avalonia dialogs need a shown owner, even when it has no visible surface.
            _clipboardHost.Show();
            desktop.MainWindow = _clipboardHost;
            desktop.ShutdownRequested += (_, _) => _trayAppContext?.Dispose();

            var settingsStore = new SettingsStore();
            var settings = settingsStore.Load();
            LocalizationService.SetLanguage(settings.LanguageCode);
            var lastCaptureStore = new LastCaptureStore();
            var clipboardService = new ClipboardService(_clipboardHost.Clipboard!);
            var monitorService = new MonitorService(() => _clipboardHost);
            var screenshotService = new ScreenshotService();
            var startupService = new StartupService();
            var hotkeyService = new HotkeyService();
            var clipboardDiagnosticsService = new ClipboardDiagnosticsService();
            var clipboardSecurityService = new ClipboardSecurityService();
            var clipboardHistoryStore = new ClipboardHistoryStore(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClippyPlus",
                    "clipboard-history.db"),
                clipboardSecurityService);
            var clipboardHistoryListener = new ClipboardHistoryListener(clipboardHistoryStore, clipboardService);
            var windowPrivacyService = new WindowPrivacyService();

            _trayAppContext = new TrayAppContext(
                hotkeyService,
                monitorService,
                screenshotService,
                clipboardService,
                lastCaptureStore,
                startupService,
                clipboardDiagnosticsService,
                clipboardHistoryStore,
                clipboardHistoryListener,
                clipboardSecurityService,
                settingsStore,
                settings,
                windowPrivacyService,
                () => _clipboardHost);
            _ = _trayAppContext.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window CreateClipboardHost()
    {
        return new Window
        {
            Width = 1,
            Height = 1,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            TransparencyBackgroundFallback = Brushes.Transparent,
            Background = Brushes.Transparent,
            Opacity = 0,
            CanResize = false,
            Title = "ClippyPlus",
        };
    }
}
