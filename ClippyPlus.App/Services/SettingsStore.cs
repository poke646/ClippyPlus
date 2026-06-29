using System.Text.Json;

namespace ClippyPlus.App.Services;

public sealed class SettingsStore
{
    private readonly string _settingsPath;

    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClippyPlus",
            "settings.json"))
    {
    }

    public SettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public AppSettings Load()
    {
        AppSettings settings;
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }

        settings.LanguageCode = LocalizationService.NormalizeLanguageCode(settings.LanguageCode);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    public static bool TryValidate(
        AppSettings settings,
        out HotkeyGesture capture,
        out HotkeyGesture editCapture,
        out HotkeyGesture clipboardHistory,
        out string error)
    {
        capture = HotkeyGesture.DefaultCapture;
        editCapture = HotkeyGesture.DefaultEditCapture;
        clipboardHistory = HotkeyGesture.DefaultClipboardHistory;
        error = string.Empty;

        if (!HotkeyGesture.TryParse(settings.CaptureHotkey, out capture))
        {
            error = LocalizationService.T("Validation.CaptureHotkeyInvalid");
            return false;
        }

        if (!HotkeyGesture.TryParse(settings.EditCaptureHotkey, out editCapture))
        {
            error = LocalizationService.T("Validation.EditCaptureHotkeyInvalid");
            return false;
        }

        if (!HotkeyGesture.TryParse(settings.ClipboardHistoryHotkey, out clipboardHistory))
        {
            error = LocalizationService.T("Validation.ClipboardHistoryHotkeyInvalid");
            return false;
        }

        if (capture == editCapture || capture == clipboardHistory || editCapture == clipboardHistory)
        {
            error = LocalizationService.T("Validation.DuplicateHotkeys");
            return false;
        }

        return true;
    }
}
