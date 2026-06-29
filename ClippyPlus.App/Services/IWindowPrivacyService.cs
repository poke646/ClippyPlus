using Avalonia.Controls;

namespace ClippyPlus.App.Services;

public interface IWindowPrivacyService
{
    void SetExcludedFromCapture(Window window, bool excluded);
}
