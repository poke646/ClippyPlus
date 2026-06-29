using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public interface IScreenshotService
{
    Task<CapturedImage> CaptureMonitorAsync(MonitorSnapshot monitor);

    CapturedImage Crop(CapturedImage source, PixelRect selection);
}
