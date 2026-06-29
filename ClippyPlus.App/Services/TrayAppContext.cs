using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ClippyPlus.App.Models;
using ClippyPlus.App.Views;

namespace ClippyPlus.App.Services;

public sealed class TrayAppContext : IDisposable
{
    private readonly IHotkeyService _hotkeyService;
    private readonly IMonitorService _monitorService;
    private readonly IScreenshotService _screenshotService;
    private readonly IClipboardService _clipboardService;
    private readonly LastCaptureStore _lastCaptureStore;
    private readonly IStartupService _startupService;
    private readonly IClipboardDiagnosticsService _clipboardDiagnosticsService;
    private readonly IClipboardHistoryStore _clipboardHistoryStore;
    private readonly IClipboardHistoryListener _clipboardHistoryListener;
    private readonly IClipboardSecurityService _clipboardSecurityService;
    private readonly SettingsStore _settingsStore;
    private readonly IWindowPrivacyService _windowPrivacyService;
    private readonly Func<Window?> _ownerProvider;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _captureMenuItem;
    private readonly NativeMenuItem _clipboardHistoryMenuItem;
    private readonly NativeMenuItem _editLastMenuItem;
    private readonly NativeMenuItem _clipboardDebugMenuItem;
    private readonly NativeMenuItem _settingsMenuItem;
    private readonly NativeMenuItem _startupMenuItem;
    private readonly NativeMenuItem _hideSharingMenuItem;
    private readonly NativeMenuItem _exitMenuItem;
    private AppSettings _settings;
    private ClipboardDebugWindow? _clipboardDebugWindow;
    private ClipboardHistoryWindow? _clipboardHistoryWindow;
    private CaptureOverlayWindow? _captureOverlayWindow;
    private bool _captureInProgress;

    public TrayAppContext(
        IHotkeyService hotkeyService,
        IMonitorService monitorService,
        IScreenshotService screenshotService,
        IClipboardService clipboardService,
        LastCaptureStore lastCaptureStore,
        IStartupService startupService,
        IClipboardDiagnosticsService clipboardDiagnosticsService,
        IClipboardHistoryStore clipboardHistoryStore,
        IClipboardHistoryListener clipboardHistoryListener,
        IClipboardSecurityService clipboardSecurityService,
        SettingsStore settingsStore,
        AppSettings settings,
        IWindowPrivacyService windowPrivacyService,
        Func<Window?> ownerProvider)
    {
        _hotkeyService = hotkeyService;
        _monitorService = monitorService;
        _screenshotService = screenshotService;
        _clipboardService = clipboardService;
        _lastCaptureStore = lastCaptureStore;
        _startupService = startupService;
        _clipboardDiagnosticsService = clipboardDiagnosticsService;
        _clipboardHistoryStore = clipboardHistoryStore;
        _clipboardHistoryListener = clipboardHistoryListener;
        _clipboardSecurityService = clipboardSecurityService;
        _settingsStore = settingsStore;
        _settings = settings;
        _windowPrivacyService = windowPrivacyService;
        _ownerProvider = ownerProvider;

        _captureMenuItem = CreateMenuItem(LocalizationService.T("Tray.Capture"), (_, _) => _ = BeginCaptureAsync());
        _clipboardHistoryMenuItem = CreateMenuItem(LocalizationService.T("Tray.ClipboardHistory"), (_, _) => _ = OpenClipboardHistoryWindowAsync());
        _editLastMenuItem = CreateMenuItem(LocalizationService.T("Tray.EditLast"), (_, _) => _ = OpenEditorAsync());
        _editLastMenuItem.IsEnabled = _lastCaptureStore.HasImage;
        _clipboardDebugMenuItem = CreateMenuItem(LocalizationService.T("Tray.ClipboardDebug"), (_, _) => OpenClipboardDebugWindow());
        _clipboardDebugMenuItem.IsVisible = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _settingsMenuItem = CreateMenuItem(LocalizationService.T("Tray.Settings"), (_, _) => OpenSettingsWindow());
        _hideSharingMenuItem = CreateMenuItem(LocalizationService.T("Tray.HideSharing"), (_, _) => ToggleHideSharing());
        _hideSharingMenuItem.ToggleType = MenuItemToggleType.CheckBox;
        _hideSharingMenuItem.IsChecked = _settings.HideSharingEnabled;
        _startupMenuItem = CreateMenuItem(LocalizationService.T("Tray.Startup"), (_, _) => ToggleStartup());
        _startupMenuItem.ToggleType = MenuItemToggleType.CheckBox;
        _startupMenuItem.IsChecked = _startupService.IsEnabled;
        _exitMenuItem = CreateMenuItem(LocalizationService.T("Tray.Exit"), (_, _) => Shutdown());

        var menu = new NativeMenu
        {
            _captureMenuItem,
            _clipboardHistoryMenuItem,
            _editLastMenuItem,
            _clipboardDebugMenuItem,
            new NativeMenuItemSeparator(),
            _hideSharingMenuItem,
            _settingsMenuItem,
            _startupMenuItem,
            new NativeMenuItemSeparator(),
            _exitMenuItem,
        };

        _trayIcon = new TrayIcon
        {
            ToolTipText = "ClippyPlus",
            Icon = AppIcon.CreateWindowIcon(),
            Menu = menu,
            IsVisible = false,
        };
        MacOSProperties.SetIsTemplateIcon(_trayIcon, true);
        _trayIcon.Clicked += (_, _) => _ = BeginCaptureAsync();

        _lastCaptureStore.Changed += (_, _) => _editLastMenuItem.IsEnabled = _lastCaptureStore.HasImage;
        _hotkeyService.CaptureHotkeyPressed += (_, _) => _ = BeginCaptureAsync(openEditorAfterCapture: false);
        _hotkeyService.EditCaptureHotkeyPressed += (_, _) => _ = BeginCaptureAsync(openEditorAfterCapture: true);
        _hotkeyService.ClipboardHistoryHotkeyPressed += (_, _) => _ = OpenClipboardHistoryWindowAsync();
        _hotkeyService.EscapePressed += (_, _) => CancelActiveCapture();
    }

    public async Task StartAsync()
    {
        _trayIcon.IsVisible = true;
        _clipboardSecurityService.Unlocked += (_, _) => _clipboardHistoryListener.Start();
        RegisterHotkeys();
        if (!_clipboardSecurityService.IsPasswordConfigured)
        {
            await EnsureClipboardUnlockedAsync();
        }
    }

    public void Dispose()
    {
        _hotkeyService.Dispose();
        _clipboardHistoryListener.Dispose();
        _clipboardDebugWindow?.Close();
        _clipboardHistoryWindow?.Close();
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }

    private async Task BeginCaptureAsync(bool openEditorAfterCapture = false)
    {
        if (_captureInProgress)
        {
            return;
        }

        _captureInProgress = true;
        try
        {
            var monitor = _monitorService.GetMonitorContainingCursor();
            var monitorImage = await _screenshotService.CaptureMonitorAsync(monitor);
            var overlayBounds = new ScreenBounds(
                monitor.Bounds.Left,
                monitor.Bounds.Top,
                monitorImage.Width,
                monitorImage.Height);
            _captureOverlayWindow = new CaptureOverlayWindow(new MonitorSnapshot(monitor.DeviceName, overlayBounds), monitorImage);
            var accepted = await _captureOverlayWindow.ShowDialog<bool>(GetOwner());
            if (!accepted || _captureOverlayWindow.Selection.IsEmpty)
            {
                return;
            }

            var cropped = _screenshotService.Crop(monitorImage, _captureOverlayWindow.Selection);
            _lastCaptureStore.Set(cropped);
            await _clipboardService.CopyImageAsync(cropped);
            if (ClipboardHistoryEntryFactory.FromImageBytes(cropped.PngBytes) is { } entry)
            {
                _clipboardHistoryStore.Upsert(entry);
            }

            if (openEditorAfterCapture)
            {
                await OpenEditorAsync(cropped);
            }
        }
        catch (Exception ex)
        {
            await MessageWindow.ShowAsync("ClippyPlus", ex.Message, _ownerProvider());
        }
        finally
        {
            _captureOverlayWindow = null;
            _captureInProgress = false;
        }
    }

    private void CancelActiveCapture()
    {
        if (!_captureInProgress)
        {
            return;
        }

        _captureOverlayWindow?.CancelCapture();
    }

    private async Task OpenEditorAsync()
    {
        if (_lastCaptureStore.Image is not { } image)
        {
            _ = MessageWindow.ShowAsync("ClippyPlus", LocalizationService.T("Tray.NoLastCapture"), _ownerProvider());
            return;
        }

        await OpenEditorAsync(image);
    }

    private async Task OpenEditorAsync(CapturedImage image)
    {
        if (!await EnsureClipboardUnlockedAsync())
        {
            return;
        }

        var editor = new EditorWindow(image, _clipboardService);
        editor.Show(GetOwner());
        editor.Activate();
    }

    private void OpenClipboardDebugWindow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        if (_clipboardDebugWindow is { IsVisible: true })
        {
            _clipboardDebugWindow.Activate();
            return;
        }

        _clipboardDebugWindow = new ClipboardDebugWindow(_clipboardDiagnosticsService);
        _clipboardDebugWindow.Closed += (_, _) => _clipboardDebugWindow = null;
        _clipboardDebugWindow.Show(GetOwner());
        _clipboardDebugWindow.Activate();
    }

    private async Task OpenClipboardHistoryWindowAsync()
    {
        if (!await EnsureClipboardUnlockedAsync())
        {
            return;
        }

        if (_clipboardHistoryWindow is not null)
        {
            _clipboardHistoryWindow.SetHideSharingEnabled(_settings.HideSharingEnabled);
            _clipboardHistoryWindow.GuardSearchActivation();
            _clipboardHistoryWindow.Show();
            _clipboardHistoryWindow.CenterOnScreen();
            _clipboardHistoryWindow.Activate();
            return;
        }

        _clipboardHistoryWindow = new ClipboardHistoryWindow(
            _clipboardHistoryStore,
            _clipboardService,
            _clipboardHistoryListener,
            _settings.HideSharingEnabled,
            _windowPrivacyService);
        _clipboardHistoryWindow.Closed += (_, _) => _clipboardHistoryWindow = null;
        _clipboardHistoryWindow.GuardSearchActivation();
        _clipboardHistoryWindow.Show(GetOwner());
        _clipboardHistoryWindow.CenterOnScreen();
        _clipboardHistoryWindow.Activate();
    }

    private async Task<bool> EnsureClipboardUnlockedAsync()
    {
        if (_clipboardSecurityService.IsUnlocked)
        {
            return true;
        }

        var owner = GetOwner();
        if (!_clipboardSecurityService.IsPasswordConfigured)
        {
            var newPassword = await PasswordWindow.ShowCreateAsync(owner);
            if (newPassword is null)
            {
                return false;
            }

            try
            {
                _clipboardSecurityService.SetPassword(newPassword);
                return true;
            }
            catch (Exception ex)
            {
                await MessageWindow.ShowAsync("ClippyPlus", ex.Message, owner);
                return false;
            }
        }

        var password = await PasswordWindow.ShowUnlockAsync(owner);
        if (password is null)
        {
            return false;
        }

        if (_clipboardSecurityService.TryUnlock(password))
        {
            return true;
        }

        await MessageWindow.ShowAsync("ClippyPlus", LocalizationService.T("Security.InvalidPassword"), owner);
        return false;
    }

    private void OpenSettingsWindow()
    {
        var window = new SettingsWindow(_settings);
        window.Saved += (_, settings) =>
        {
            _settings = settings;
            LocalizationService.SetLanguage(_settings.LanguageCode);
            _settingsStore.Save(_settings);
            UpdateMenuText();
            _hideSharingMenuItem.IsChecked = _settings.HideSharingEnabled;
            _clipboardHistoryWindow?.SetHideSharingEnabled(_settings.HideSharingEnabled);
            RegisterHotkeys();
        };
        window.Show(GetOwner());
        window.Activate();
    }

    private void ToggleHideSharing()
    {
        _settings.HideSharingEnabled = !_settings.HideSharingEnabled;
        _settingsStore.Save(_settings);
        _hideSharingMenuItem.IsChecked = _settings.HideSharingEnabled;
        _clipboardHistoryWindow?.SetHideSharingEnabled(_settings.HideSharingEnabled);
    }

    private void ToggleStartup()
    {
        try
        {
            _startupService.SetEnabled(!_startupService.IsEnabled);
            _startupMenuItem.IsChecked = _startupService.IsEnabled;
        }
        catch (Exception ex)
        {
            _ = MessageWindow.ShowAsync("ClippyPlus", ex.Message, _ownerProvider());
        }
    }

    private void RegisterHotkeys()
    {
        if (!SettingsStore.TryValidate(_settings, out var captureHotkey, out var editCaptureHotkey, out var clipboardHistoryHotkey, out var error))
        {
            _settings = new AppSettings();
            _settingsStore.Save(_settings);
            captureHotkey = HotkeyGesture.DefaultCapture;
            editCaptureHotkey = HotkeyGesture.DefaultEditCapture;
            clipboardHistoryHotkey = HotkeyGesture.DefaultClipboardHistory;
            _ = MessageWindow.ShowAsync("ClippyPlus", LocalizationService.Format("Tray.HotkeysReset", error), _ownerProvider());
        }

        try
        {
            _hotkeyService.RegisterHotkeys(captureHotkey, editCaptureHotkey, clipboardHistoryHotkey);
        }
        catch (Exception ex)
        {
            _ = MessageWindow.ShowAsync(
                "ClippyPlus",
                LocalizationService.Format("Tray.GlobalHotkeysFailed", ex.Message),
                _ownerProvider());
        }
    }

    private void Shutdown()
    {
        Dispose();
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        });
    }

    private Window GetOwner()
    {
        return _ownerProvider() ?? throw new InvalidOperationException(LocalizationService.T("Tray.NoWindowHost"));
    }

    private void UpdateMenuText()
    {
        _captureMenuItem.Header = LocalizationService.T("Tray.Capture");
        _clipboardHistoryMenuItem.Header = LocalizationService.T("Tray.ClipboardHistory");
        _editLastMenuItem.Header = LocalizationService.T("Tray.EditLast");
        _clipboardDebugMenuItem.Header = LocalizationService.T("Tray.ClipboardDebug");
        _settingsMenuItem.Header = LocalizationService.T("Tray.Settings");
        _hideSharingMenuItem.Header = LocalizationService.T("Tray.HideSharing");
        _startupMenuItem.Header = LocalizationService.T("Tray.Startup");
        _exitMenuItem.Header = LocalizationService.T("Tray.Exit");
    }

    private static NativeMenuItem CreateMenuItem(string header, EventHandler handler)
    {
        var item = new NativeMenuItem(header);
        item.Click += handler;
        return item;
    }
}
