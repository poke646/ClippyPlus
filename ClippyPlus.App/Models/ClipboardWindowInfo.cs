namespace ClippyPlus.App.Models;

public sealed record ClipboardWindowInfo(
    IntPtr Handle,
    int? ProcessId,
    string ProcessName,
    string WindowTitle,
    string WindowClass);

