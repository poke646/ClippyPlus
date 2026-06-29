using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;

namespace ClippyPlus.App.Services;

public sealed class HotkeyService : IHotkeyService
{
    private TaskPoolGlobalHook? _hook;
    private HotkeyGesture _captureHotkey = HotkeyGesture.DefaultCapture;
    private HotkeyGesture _editCaptureHotkey = HotkeyGesture.DefaultEditCapture;
    private HotkeyGesture _clipboardHistoryHotkey = HotkeyGesture.DefaultClipboardHistory;

    public event EventHandler? CaptureHotkeyPressed;

    public event EventHandler? EditCaptureHotkeyPressed;

    public event EventHandler? ClipboardHistoryHotkeyPressed;

    public event EventHandler? EscapePressed;

    public HotkeyRegistrationResult RegisterHotkeys(
        HotkeyGesture captureHotkey,
        HotkeyGesture editCaptureHotkey,
        HotkeyGesture clipboardHistoryHotkey)
    {
        _captureHotkey = captureHotkey;
        _editCaptureHotkey = editCaptureHotkey;
        _clipboardHistoryHotkey = clipboardHistoryHotkey;

        if (_hook is null)
        {
            _hook = new TaskPoolGlobalHook(globalHookType: GlobalHookType.Keyboard);
            _hook.KeyPressed += OnKeyPressed;
            _ = _hook.RunAsync();
        }

        return new HotkeyRegistrationResult(true, true, true);
    }

    public void Dispose()
    {
        if (_hook is not null)
        {
            _hook.KeyPressed -= OnKeyPressed;
            _hook.Dispose();
            _hook = null;
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == KeyCode.VcEscape)
        {
            Dispatcher.UIThread.Post(() => EscapePressed?.Invoke(this, EventArgs.Empty));
            return;
        }

        if (HotkeyGesture.IsModifierKey(e.Data.KeyCode) || !HotkeyGesture.TryMapKeyCode(e.Data.KeyCode, out var key))
        {
            return;
        }

        var modifiers = FromMask(e.RawEvent.Mask);
        var gesture = new HotkeyGesture(modifiers, key);
        if (gesture == _editCaptureHotkey)
        {
            Dispatcher.UIThread.Post(() => EditCaptureHotkeyPressed?.Invoke(this, EventArgs.Empty));
        }
        else if (gesture == _captureHotkey)
        {
            Dispatcher.UIThread.Post(() => CaptureHotkeyPressed?.Invoke(this, EventArgs.Empty));
        }
        else if (gesture == _clipboardHistoryHotkey)
        {
            Dispatcher.UIThread.Post(() => ClipboardHistoryHotkeyPressed?.Invoke(this, EventArgs.Empty));
        }
    }

    private static HotkeyModifiers FromMask(EventMask mask)
    {
        var modifiers = HotkeyModifiers.None;
        if (mask.HasCtrl())
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (mask.HasShift())
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (mask.HasAlt())
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (mask.HasMeta())
        {
            modifiers |= HotkeyModifiers.Meta;
        }

        return modifiers;
    }
}
