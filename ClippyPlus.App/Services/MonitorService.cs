using System.Runtime.InteropServices;
using Avalonia.Controls;
using ClippyPlus.App.Interop;
using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public sealed class MonitorService : IMonitorService
{
    private readonly Func<Window?> _windowProvider;

    public MonitorService(Func<Window?> windowProvider)
    {
        _windowProvider = windowProvider;
    }

    public MonitorSnapshot GetMonitorContainingCursor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsMonitorContainingCursor();
        }

        var window = _windowProvider();
        var screen = window?.Screens.Primary;
        if (screen is null)
        {
            return new MonitorSnapshot("main", new ScreenBounds(0, 0, 1440, 900));
        }

        return new MonitorSnapshot(
            screen.DisplayName ?? "main",
            new ScreenBounds(
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height));
    }

    private static MonitorSnapshot GetWindowsMonitorContainingCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            throw new InvalidOperationException("Could not read cursor position.");
        }

        var monitorHandle = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not resolve monitor for cursor position.");
        }

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
            DeviceName = string.Empty,
        };

        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
        {
            throw new InvalidOperationException("Could not read monitor information.");
        }

        var bounds = new ScreenBounds(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top);

        return new MonitorSnapshot(info.DeviceName, bounds);
    }
}
