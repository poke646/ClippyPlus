namespace ClippyPlus.App.Services;

public sealed class AppSettings
{
    public string LanguageCode { get; set; } = LocalizationService.DefaultLanguageCode;

    public string CaptureHotkey { get; set; } = HotkeyGesture.DefaultCapture.ToString();

    public string EditCaptureHotkey { get; set; } = HotkeyGesture.DefaultEditCapture.ToString();

    public string ClipboardHistoryHotkey { get; set; } = HotkeyGesture.DefaultClipboardHistory.ToString();

    public bool HideSharingEnabled { get; set; } = true;
}
