using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public interface IClipboardDiagnosticsService
{
    ClipboardDiagnosticsSnapshot Capture();
}

