using ClippyPlus.App.Services;
using SharpHook.Data;

namespace ClippyPlus.Tests;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void TryParse_ParsesControlShiftKey()
    {
        var parsed = HotkeyGesture.TryParse("Ctrl+Shift+V", out var gesture);

        Assert.True(parsed);
        Assert.True(gesture.Modifiers.HasFlag(HotkeyModifiers.Control));
        Assert.True(gesture.Modifiers.HasFlag(HotkeyModifiers.Shift));
        Assert.Equal("V", gesture.Key);
    }

    [Fact]
    public void TryMapKeyCode_MapsLettersAndSpace()
    {
        Assert.True(HotkeyGesture.TryMapKeyCode(KeyCode.VcD, out var letter));
        Assert.Equal("D", letter);

        Assert.True(HotkeyGesture.TryMapKeyCode(KeyCode.VcSpace, out var space));
        Assert.Equal("Space", space);
    }

    [Fact]
    public void DefaultEditCapture_IsControlAltShiftS()
    {
        Assert.Equal("Ctrl+Alt+Shift+S", HotkeyGesture.DefaultEditCapture.ToString());
    }
}
