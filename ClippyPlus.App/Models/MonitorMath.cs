namespace ClippyPlus.App.Models;

public static class MonitorMath
{
    public static MonitorSnapshot ResolveNearest(
        IReadOnlyList<MonitorSnapshot> monitors,
        int cursorX,
        int cursorY)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        foreach (var monitor in monitors)
        {
            if (Contains(monitor.Bounds, cursorX, cursorY))
            {
                return monitor;
            }
        }

        return monitors
            .OrderBy(monitor => DistanceSquared(monitor.Bounds, cursorX, cursorY))
            .First();
    }

    private static bool Contains(ScreenBounds bounds, int x, int y)
    {
        return x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
    }

    private static long DistanceSquared(ScreenBounds bounds, int x, int y)
    {
        var nearestX = Math.Min(Math.Max(x, bounds.Left), bounds.Right);
        var nearestY = Math.Min(Math.Max(y, bounds.Top), bounds.Bottom);
        var dx = x - nearestX;
        var dy = y - nearestY;
        return (long)dx * dx + (long)dy * dy;
    }
}
