namespace ClippyPlus.App.Models;

public readonly record struct ScreenBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}
