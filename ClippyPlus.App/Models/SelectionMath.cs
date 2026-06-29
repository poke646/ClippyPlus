namespace ClippyPlus.App.Models;

public static class SelectionMath
{
    public static UiRect Normalize(UiPoint start, UiPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);

        return new UiRect(left, top, right - left, bottom - top);
    }

    public static PixelRect ToPixelRect(UiRect selection, double surfaceWidth, double surfaceHeight, int pixelWidth, int pixelHeight)
    {
        if (selection.Width < 1 || selection.Height < 1 || surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            return PixelRect.Empty;
        }

        var scaleX = pixelWidth / surfaceWidth;
        var scaleY = pixelHeight / surfaceHeight;

        var x = Clamp((int)Math.Floor(selection.X * scaleX), 0, pixelWidth);
        var y = Clamp((int)Math.Floor(selection.Y * scaleY), 0, pixelHeight);
        var right = Clamp((int)Math.Ceiling(selection.Right * scaleX), 0, pixelWidth);
        var bottom = Clamp((int)Math.Ceiling(selection.Bottom * scaleY), 0, pixelHeight);

        var width = Math.Max(0, right - x);
        var height = Math.Max(0, bottom - y);

        return width == 0 || height == 0 ? PixelRect.Empty : new PixelRect(x, y, width, height);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}
