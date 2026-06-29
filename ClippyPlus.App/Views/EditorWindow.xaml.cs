using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClippyPlus.App.Models;
using ClippyPlus.App.Services;
using SkiaSharp;

namespace ClippyPlus.App.Views;

public sealed class EditorWindow : Window
{
    private readonly CapturedImage _image;
    private readonly IClipboardService _clipboardService;
    private readonly DrawingSurface _drawingSurface;
    private readonly Button _undoButton;
    private readonly Button _clearButton;

    public EditorWindow(CapturedImage image, IClipboardService clipboardService)
    {
        _image = image;
        _clipboardService = clipboardService;

        Title = "ClippyPlus";
        MinWidth = 420;
        MinHeight = 320;
        Width = Math.Min(image.Width + 40, 1200);
        Height = Math.Min(image.Height + 80, 850);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.CreateWindowIcon();

        _drawingSurface = new DrawingSurface(image)
        {
            Width = image.Width,
            Height = image.Height,
        };
        _drawingSurface.StrokesChanged += (_, _) => RefreshButtons();

        _undoButton = new Button { Content = LocalizationService.T("Editor.Undo"), MinWidth = 80 };
        _undoButton.Click += (_, _) => _drawingSurface.UndoLastStroke();

        _clearButton = new Button { Content = LocalizationService.T("Editor.Clear"), MinWidth = 80 };
        _clearButton.Click += (_, _) => _drawingSurface.ClearStrokes();

        var copyButton = new Button { Content = LocalizationService.T("Editor.Copy"), MinWidth = 80 };
        copyButton.Click += async (_, _) => await CopyCompositedImageAsync();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8),
            Children = { _undoButton, _clearButton, copyButton },
        };

        Content = new DockPanel
        {
            Children =
            {
                toolbar,
                new ScrollViewer
                {
                    Background = Brushes.Black,
                    Content = _drawingSurface,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            },
        };
        DockPanel.SetDock(toolbar, Dock.Top);

        KeyDown += OnKeyDown;
        RefreshButtons();
    }

    private async Task CopyCompositedImageAsync()
    {
        var result = _drawingSurface.ExportCompositedImage();
        await _clipboardService.CopyImageAsync(result);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        var modifiers = e.KeyModifiers;
        if (!modifiers.HasFlag(KeyModifiers.Control) && !modifiers.HasFlag(KeyModifiers.Meta))
        {
            return;
        }

        if (e.Key == Key.Z)
        {
            _drawingSurface.UndoLastStroke();
            e.Handled = true;
        }
        else if (e.Key == Key.C)
        {
            await CopyCompositedImageAsync();
            e.Handled = true;
        }
    }

    private void RefreshButtons()
    {
        var hasStrokes = _drawingSurface.HasStrokes;
        _undoButton.IsEnabled = hasStrokes;
        _clearButton.IsEnabled = hasStrokes;
    }

    private sealed class DrawingSurface : Control
    {
        private readonly CapturedImage _image;
        private readonly Bitmap _bitmap;
        private readonly List<List<UiPoint>> _strokes = [];
        private List<UiPoint>? _activeStroke;
        private const double StrokeWidth = 8;

        public DrawingSurface(CapturedImage image)
        {
            _image = image;
            _bitmap = new Bitmap(new MemoryStream(image.PngBytes));
            ClipToBounds = true;
            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
        }

        public event EventHandler? StrokesChanged;

        public bool HasStrokes => _strokes.Count > 0;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.DrawImage(_bitmap, new Rect(0, 0, _image.Width, _image.Height));
            var pen = new Pen(
                Brushes.Red,
                StrokeWidth,
                lineCap: PenLineCap.Round,
                lineJoin: PenLineJoin.Round);
            foreach (var stroke in _strokes)
            {
                DrawStroke(context, pen, stroke);
            }

            if (_activeStroke is { } activeStroke)
            {
                DrawStroke(context, pen, activeStroke);
            }
        }

        public void UndoLastStroke()
        {
            if (_strokes.Count == 0)
            {
                return;
            }

            _strokes.RemoveAt(_strokes.Count - 1);
            InvalidateVisual();
            StrokesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearStrokes()
        {
            _strokes.Clear();
            InvalidateVisual();
            StrokesChanged?.Invoke(this, EventArgs.Empty);
        }

        public CapturedImage ExportCompositedImage()
        {
            using var source = SKBitmap.Decode(_image.PngBytes)
                ?? throw new InvalidOperationException(LocalizationService.T("Editor.ImageReadFailed"));
            using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(source, 0, 0);
            using var paint = new SKPaint
            {
                Color = SKColors.Red,
                StrokeWidth = (float)StrokeWidth,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };

            foreach (var stroke in _strokes)
            {
                DrawSkiaStroke(canvas, paint, stroke);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return CapturedImage.FromPng(data.ToArray());
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var point = e.GetPosition(this);
            _activeStroke = [new UiPoint(point.X, point.Y)];
            e.Pointer.Capture(this);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_activeStroke is null || e.Pointer.Captured != this)
            {
                return;
            }

            var point = e.GetPosition(this);
            _activeStroke.Add(new UiPoint(point.X, point.Y));
            InvalidateVisual();
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_activeStroke is null)
            {
                return;
            }

            e.Pointer.Capture(null);
            if (_activeStroke.Count > 1)
            {
                _strokes.Add(_activeStroke);
            }

            _activeStroke = null;
            InvalidateVisual();
            StrokesChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void DrawStroke(DrawingContext context, Pen pen, IReadOnlyList<UiPoint> stroke)
        {
            if (stroke.Count < 2)
            {
                return;
            }

            var geometry = CreateStrokeGeometry(stroke);
            context.DrawGeometry(null, pen, geometry);
        }

        private static void DrawSkiaStroke(SKCanvas canvas, SKPaint paint, IReadOnlyList<UiPoint> stroke)
        {
            if (stroke.Count < 2)
            {
                return;
            }

            using var path = CreateSkiaStrokePath(stroke);
            canvas.DrawPath(path, paint);
        }

        private static StreamGeometry CreateStrokeGeometry(IReadOnlyList<UiPoint> stroke)
        {
            var geometry = new StreamGeometry();
            using var geometryContext = geometry.Open();
            geometryContext.BeginFigure(ToPoint(stroke[0]), isFilled: false);
            for (var i = 1; i < stroke.Count - 1; i++)
            {
                geometryContext.QuadraticBezierTo(ToPoint(stroke[i]), ToMidPoint(stroke[i], stroke[i + 1]));
            }

            geometryContext.LineTo(ToPoint(stroke[^1]));
            geometryContext.EndFigure(isClosed: false);
            return geometry;
        }

        private static SKPath CreateSkiaStrokePath(IReadOnlyList<UiPoint> stroke)
        {
            var path = new SKPath();
            path.MoveTo((float)stroke[0].X, (float)stroke[0].Y);
            for (var i = 1; i < stroke.Count - 1; i++)
            {
                var midpoint = MidPoint(stroke[i], stroke[i + 1]);
                path.QuadTo((float)stroke[i].X, (float)stroke[i].Y, (float)midpoint.X, (float)midpoint.Y);
            }

            path.LineTo((float)stroke[^1].X, (float)stroke[^1].Y);
            return path;
        }

        private static Point ToPoint(UiPoint point)
        {
            return new Point(point.X, point.Y);
        }

        private static Point ToMidPoint(UiPoint first, UiPoint second)
        {
            var midpoint = MidPoint(first, second);
            return new Point(midpoint.X, midpoint.Y);
        }

        private static UiPoint MidPoint(UiPoint first, UiPoint second)
        {
            return new UiPoint((first.X + second.X) / 2, (first.Y + second.Y) / 2);
        }
    }
}
