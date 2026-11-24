using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using SkiaSharp;
using Path = Avalonia.Controls.Shapes.Path;

namespace ClippyPlus.App.Views;

internal enum RadialClipboardIcon
{
    Text,
    Image,
    Section,
    Clipboard,
    Search,
    Copy,
    Previous,
    Next,
}

internal static class RadialClipboardVisuals
{
    private static readonly Geometry[] Icons =
    [
        Geometry.Parse("M6 2.5 H14.5 L19 7 V19.5 Q19 21.5 17 21.5 H6 Q4 21.5 4 19.5 V4.5 Q4 2.5 6 2.5 Z M14 2.5 V7.5 H19 M7.5 12 H15.5 M7.5 16 H14"),
        Geometry.Parse("M4.5 3 H19.5 Q21 3 21 4.5 V19.5 Q21 21 19.5 21 H4.5 Q3 21 3 19.5 V4.5 Q3 3 4.5 3 Z M3 17.5 L8 12.5 L11.5 16 L15.5 11.5 L21 17 M9.5 8 A1.5 1.5 0 1 1 6.5 8 A1.5 1.5 0 1 1 9.5 8 Z"),
        Geometry.Parse("M4.5 5 H9 L11.5 7.5 H19.5 Q21 7.5 21 9 V18.5 Q21 20 19.5 20 H4.5 Q3 20 3 18.5 V6.5 Q3 5 4.5 5 Z M7 12 H17 M7 15.5 H14"),
        Geometry.Parse("M8 5 H5.5 Q3.5 5 3.5 7 V20 Q3.5 22 5.5 22 H18.5 Q20.5 22 20.5 20 V7 Q20.5 5 18.5 5 H16 M9 4.5 H10 V3.75 A2 2 0 1 1 14 3.75 V4.5 H15 Q16 4.5 16 5.5 V7.5 Q16 8.5 15 8.5 H9 Q8 8.5 8 7.5 V5.5 Q8 4.5 9 4.5 Z"),
        Geometry.Parse("M16.5 10 A6.5 6.5 0 1 1 3.5 10 A6.5 6.5 0 1 1 16.5 10 Z M15 15 L20.5 20.5"),
        Geometry.Parse("M8 3.5 H17.5 Q19.5 3.5 19.5 5.5 V15 M6 7.5 H14 Q16 7.5 16 9.5 V18.5 Q16 20.5 14 20.5 H6 Q4 20.5 4 18.5 V9.5 Q4 7.5 6 7.5 Z"),
        Geometry.Parse("M14.5 6.5 L9 12 L14.5 17.5"),
        Geometry.Parse("M9.5 6.5 L15 12 L9.5 17.5"),
    ];

    public static Control CreateIcon(RadialClipboardIcon icon, double size = 20, IBrush? stroke = null)
    {
        return new Viewbox
        {
            Width = size,
            Height = size,
            IsHitTestVisible = false,
            Child = new Canvas
            {
                Width = 24,
                Height = 24,
                Children =
                {
                    new Path
                    {
                        Data = Icons[(int)icon],
                        Stroke = stroke ?? Brush("ClippySelectionBorderBrush", Color.FromRgb(106, 158, 232)),
                        StrokeThickness = 1.7,
                        StrokeLineCap = PenLineCap.Round,
                        StrokeJoin = PenLineJoin.Round,
                    },
                },
            },
        };
    }

    public static IBrush Brush(string resourceKey, Color fallback)
    {
        return Application.Current?.TryFindResource(resourceKey, out var value) == true && value is IBrush brush
            ? brush
            : new ImmutableSolidColorBrush(fallback);
    }

    public static IBrush Mix(IBrush first, IBrush second, double weight)
    {
        if (first is not ISolidColorBrush start || second is not ISolidColorBrush end)
        {
            return first;
        }

        static byte Blend(byte start, byte end, double weight) => (byte)Math.Round(start + (end - start) * weight);

        return new ImmutableSolidColorBrush(Color.FromRgb(
            Blend(start.Color.R, end.Color.R, weight),
            Blend(start.Color.G, end.Color.G, weight),
            Blend(start.Color.B, end.Color.B, weight)));
    }

    public static Bitmap? DecodeImage(byte[] bytes, int longEdge)
    {
        using var metadataStream = new MemoryStream(bytes);
        using var codec = SKCodec.Create(metadataStream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }

        using var imageStream = new MemoryStream(bytes);
        var imageSize = codec.Info.Size;
        if (Math.Max(imageSize.Width, imageSize.Height) <= longEdge)
        {
            return new Bitmap(imageStream);
        }

        return imageSize.Width >= imageSize.Height
            ? Bitmap.DecodeToWidth(imageStream, longEdge)
            : Bitmap.DecodeToHeight(imageStream, longEdge);
    }
}
