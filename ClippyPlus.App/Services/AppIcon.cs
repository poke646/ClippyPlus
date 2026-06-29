using Avalonia.Controls;
using Avalonia.Platform;

namespace ClippyPlus.App.Services;

public static class AppIcon
{
    public static WindowIcon CreateWindowIcon()
    {
        return new WindowIcon(AssetLoader.Open(new Uri("avares://ClippyPlus.App/icon.png")));
    }
}
