using Avalonia;
using Avalonia.Media;

namespace ClippyPlus.App.Services;

public static class ThemeService
{
    public static void Apply(Application application)
    {
        application.Resources["ClippyWindowBrush"] = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        application.Resources["ClippySurfaceBrush"] = new SolidColorBrush(Color.FromRgb(38, 38, 38));
        application.Resources["ClippyElevatedBrush"] = new SolidColorBrush(Color.FromRgb(48, 48, 48));
        application.Resources["ClippyBorderBrush"] = new SolidColorBrush(Color.FromRgb(69, 69, 69));
        application.Resources["ClippyTextBrush"] = new SolidColorBrush(Color.FromRgb(242, 242, 242));
        application.Resources["ClippyMutedTextBrush"] = new SolidColorBrush(Color.FromRgb(176, 176, 176));
        application.Resources["ClippySelectionBrush"] = new SolidColorBrush(Color.FromRgb(45, 73, 112));
        application.Resources["ClippySelectionBorderBrush"] = new SolidColorBrush(Color.FromRgb(106, 158, 232));
        application.Resources["ClippyHoverBrush"] = new SolidColorBrush(Color.FromRgb(54, 54, 54));
        application.Resources["ClippyScrollbarBrush"] = new SolidColorBrush(Color.FromRgb(138, 138, 138));
    }
}
