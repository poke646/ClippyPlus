using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ClippyPlus.App.Services;

public sealed class WindowPrivacyService : IWindowPrivacyService
{
    private const uint WdaNone = 0x00000000;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const nuint NsWindowSharingNone = 0;
    private const nuint NsWindowSharingReadOnly = 1;

    public void SetExcludedFromCapture(Window window, bool excluded)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetWindowDisplayAffinity(handle, excluded ? WdaExcludeFromCapture : WdaNone);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            objc_msgSend(handle, sel_registerName("setSharingType:"), excluded ? NsWindowSharingNone : NsWindowSharingReadOnly);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_msgSend(IntPtr receiver, IntPtr selector, nuint argument);
}
