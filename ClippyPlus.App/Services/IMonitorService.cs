using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public interface IMonitorService
{
    MonitorSnapshot GetMonitorContainingCursor();
}
