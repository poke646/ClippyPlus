using ClippyPlus.App.Models;

namespace ClippyPlus.Tests;

public sealed class MonitorMathTests
{
    [Fact]
    public void ResolveNearest_ReturnsMonitorContainingCursor()
    {
        var monitors = new[]
        {
            new MonitorSnapshot("left", new ScreenBounds(-1920, 0, 1920, 1080)),
            new MonitorSnapshot("primary", new ScreenBounds(0, 0, 1920, 1080)),
        };

        var result = MonitorMath.ResolveNearest(monitors, -100, 300);

        Assert.Equal("left", result.DeviceName);
    }

    [Fact]
    public void ResolveNearest_ReturnsClosestMonitorWhenCursorIsOutside()
    {
        var monitors = new[]
        {
            new MonitorSnapshot("primary", new ScreenBounds(0, 0, 1920, 1080)),
            new MonitorSnapshot("right", new ScreenBounds(1920, 0, 1920, 1080)),
        };

        var result = MonitorMath.ResolveNearest(monitors, 4000, 500);

        Assert.Equal("right", result.DeviceName);
    }
}
