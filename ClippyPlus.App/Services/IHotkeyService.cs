namespace ClippyPlus.App.Services;

public interface IHotkeyService : IDisposable
{
    event EventHandler? CaptureHotkeyPressed;

    event EventHandler? EditCaptureHotkeyPressed;

    event EventHandler? ClipboardHistoryHotkeyPressed;

    event EventHandler? EscapePressed;

    HotkeyRegistrationResult RegisterHotkeys(
        HotkeyGesture captureHotkey,
        HotkeyGesture editCaptureHotkey,
        HotkeyGesture clipboardHistoryHotkey);
}

public sealed record HotkeyRegistrationResult(
    bool CaptureRegistered,
    bool EditCaptureRegistered,
    bool ClipboardHistoryRegistered);
