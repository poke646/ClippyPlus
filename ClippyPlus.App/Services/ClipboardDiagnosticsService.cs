using System.Diagnostics;
using System.Text;
using ClippyPlus.App.Interop;
using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public sealed class ClipboardDiagnosticsService : IClipboardDiagnosticsService
{
    private const int MaxTextLength = 512;

    public ClipboardDiagnosticsSnapshot Capture()
    {
        var openClipboardWindow = NativeMethods.GetOpenClipboardWindow();
        var clipboardOwner = NativeMethods.GetClipboardOwner();

        return new ClipboardDiagnosticsSnapshot(
            DateTimeOffset.Now,
            NativeMethods.GetClipboardSequenceNumber(),
            CreateWindowInfo(openClipboardWindow),
            CreateWindowInfo(clipboardOwner));
    }

    private static ClipboardWindowInfo? CreateWindowInfo(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        var id = processId == 0 ? null : (int?)processId;

        return new ClipboardWindowInfo(
            hWnd,
            id,
            GetProcessName(id),
            GetWindowText(hWnd),
            GetClassName(hWnd));
    }

    private static string GetProcessName(int? processId)
    {
        if (processId is not { } id)
        {
            return LocalizationService.T("Debug.Unknown");
        }

        try
        {
            using var process = Process.GetProcessById(id);
            return string.IsNullOrWhiteSpace(process.ProcessName)
                ? LocalizationService.T("Debug.Unknown")
                : process.ProcessName;
        }
        catch (ArgumentException)
        {
            return LocalizationService.T("Diagnostics.ProcessEnded");
        }
        catch (InvalidOperationException)
        {
            return LocalizationService.T("Diagnostics.ProcessUnavailable");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return LocalizationService.T("Diagnostics.AccessDenied");
        }
    }

    private static string GetWindowText(IntPtr hWnd)
    {
        var buffer = new StringBuilder(MaxTextLength);
        var length = NativeMethods.GetWindowText(hWnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : LocalizationService.T("Diagnostics.NoTitle");
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var buffer = new StringBuilder(MaxTextLength);
        var length = NativeMethods.GetClassName(hWnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : LocalizationService.T("Diagnostics.NoClass");
    }
}
