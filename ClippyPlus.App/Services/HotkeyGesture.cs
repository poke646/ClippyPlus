using System.Runtime.InteropServices;
using SharpHook.Data;

namespace ClippyPlus.App.Services;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static HotkeyGesture DefaultCapture =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new HotkeyGesture(HotkeyModifiers.Meta | HotkeyModifiers.Shift | HotkeyModifiers.Alt, "D")
            : new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt, "D");

    public static HotkeyGesture DefaultEditCapture =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new HotkeyGesture(HotkeyModifiers.Meta | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "S")
            : new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "S");

    public static HotkeyGesture DefaultClipboardHistory =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new HotkeyGesture(HotkeyModifiers.Meta | HotkeyModifiers.Shift, "V")
            : new HotkeyGesture(HotkeyModifiers.Control, "Space");

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            parts.Add(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Cmd" : "Meta");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Option" : "Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(Key);
        return string.Join("+", parts);
    }

    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(HotkeyModifiers.None, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        var key = string.Empty;
        foreach (var rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "cmd":
                case "command":
                case "meta":
                case "win":
                    modifiers |= HotkeyModifiers.Meta;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "alt":
                case "option":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                default:
                    key = NormalizeKey(rawPart);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(key) || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static bool TryMapKeyCode(KeyCode keyCode, out string key)
    {
        key = keyCode switch
        {
            KeyCode.VcSpace => "Space",
            KeyCode.VcEscape => "Escape",
            >= KeyCode.VcA and <= KeyCode.VcZ => keyCode.ToString()[2..],
            >= KeyCode.Vc0 and <= KeyCode.Vc9 => keyCode.ToString()[2..],
            _ => string.Empty,
        };

        return key.Length > 0;
    }

    public static bool IsModifierKey(KeyCode keyCode)
    {
        return keyCode is KeyCode.VcLeftShift or KeyCode.VcRightShift
            or KeyCode.VcLeftControl or KeyCode.VcRightControl
            or KeyCode.VcLeftAlt or KeyCode.VcRightAlt
            or KeyCode.VcLeftMeta or KeyCode.VcRightMeta;
    }

    public static string NormalizeKey(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Equals(" ", StringComparison.Ordinal))
        {
            return "Space";
        }

        if (trimmed.Equals("space", StringComparison.OrdinalIgnoreCase))
        {
            return "Space";
        }

        return trimmed.Length == 1
            ? trimmed.ToUpperInvariant()
            : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }
}
