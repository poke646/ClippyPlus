namespace ClippyPlus.App.Models;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect Empty { get; } = new(0, 0, 0, 0);
}
