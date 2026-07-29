using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClippyPlus.App.Models;

namespace ClippyPlus.App.Views;

public sealed class CaptureOverlayWindow : Window
{
    private readonly MonitorSnapshot _monitor;
    private readonly Canvas _surface;
    private readonly Rectangle _dimTop;
    private readonly Rectangle _dimLeft;
    private readonly Rectangle _dimRight;
    private readonly Rectangle _dimBottom;
    private readonly Rectangle _selectionRectangle;
    private readonly Border _dimensionLabel;
    private readonly TextBlock _dimensionText;
    private UiPoint? _startPoint;
    private IPointer? _capturedPointer;

    public CaptureOverlayWindow(MonitorSnapshot monitor, CapturedImage frozenFrame)
    {
        _monitor = monitor;
        Title = "ClippyPlus Capture";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(monitor.Bounds.Left, monitor.Bounds.Top);
        Width = frozenFrame.Width;
        Height = frozenFrame.Height;

        var image = new Image
        {
            Source = CreateBitmap(frozenFrame),
            Stretch = Stretch.Fill,
        };

        var dimBrush = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0));
        _dimTop = CreateDimRectangle(dimBrush);
        _dimLeft = CreateDimRectangle(dimBrush);
        _dimRight = CreateDimRectangle(dimBrush);
        _dimBottom = CreateDimRectangle(dimBrush);

        _selectionRectangle = new Rectangle
        {
            Stroke = Brushes.DeepSkyBlue,
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 6, 4 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            IsVisible = false,
        };

        _dimensionText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
        };
        _dimensionLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Avalonia.Thickness(8, 4),
            Child = _dimensionText,
            IsHitTestVisible = false,
            IsVisible = false,
        };

        _surface = new Canvas
        {
            Background = Brushes.Transparent,
            Children = { _dimTop, _dimLeft, _dimRight, _dimBottom, _selectionRectangle, _dimensionLabel },
            Focusable = true,
        };
        _surface.PointerPressed += OnPointerPressed;
        _surface.PointerMoved += OnPointerMoved;
        _surface.PointerReleased += OnPointerReleased;
        _surface.SizeChanged += (_, _) => DrawDimming(null);

        Content = new Grid
        {
            Children =
            {
                image,
                _surface,
            },
        };
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += (_, _) =>
        {
            Activate();
            Focus();
            _surface.Focus();
        };
    }

    public ClippyPlus.App.Models.PixelRect Selection { get; private set; } = ClippyPlus.App.Models.PixelRect.Empty;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(_surface);
        _startPoint = new UiPoint(point.X, point.Y);
        e.Pointer.Capture(_surface);
        _capturedPointer = e.Pointer;
        _selectionRectangle.IsVisible = true;
        DrawSelection(_startPoint.Value, _startPoint.Value);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_startPoint is not { } start || e.Pointer.Captured != _surface)
        {
            return;
        }

        var point = e.GetPosition(_surface);
        DrawSelection(start, new UiPoint(point.X, point.Y));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_startPoint is not { } start)
        {
            return;
        }

        e.Pointer.Capture(null);
        _capturedPointer = null;
        var point = e.GetPosition(_surface);
        var rect = SelectionMath.Normalize(start, new UiPoint(point.X, point.Y));
        Selection = SelectionMath.ToPixelRect(rect, _surface.Bounds.Width, _surface.Bounds.Height, _monitor.Bounds.Width, _monitor.Bounds.Height);
        Close(!Selection.IsEmpty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        HandleKeyDown(e);
        if (!e.Handled)
        {
            base.OnKeyDown(e);
        }
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        HandleKeyDown(e);
    }

    private void HandleKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelCapture();
            e.Handled = true;
        }
    }

    public void CancelCapture()
    {
        _startPoint = null;
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        _selectionRectangle.IsVisible = false;
        _dimensionLabel.IsVisible = false;
        DrawDimming(null);
        Selection = ClippyPlus.App.Models.PixelRect.Empty;
        Close(false);
    }

    private void DrawSelection(UiPoint start, UiPoint end)
    {
        var rect = SelectionMath.Normalize(start, end);
        _selectionRectangle.Width = rect.Width;
        _selectionRectangle.Height = rect.Height;
        Canvas.SetLeft(_selectionRectangle, rect.Left);
        Canvas.SetTop(_selectionRectangle, rect.Top);
        DrawDimming(rect);
        DrawDimensionLabel(rect);
    }

    private void DrawDimensionLabel(UiRect rect)
    {
        var pixelRect = SelectionMath.ToPixelRect(
            rect,
            _surface.Bounds.Width,
            _surface.Bounds.Height,
            _monitor.Bounds.Width,
            _monitor.Bounds.Height);
        if (pixelRect.IsEmpty)
        {
            _dimensionLabel.IsVisible = false;
            return;
        }

        _dimensionText.Text = $"{pixelRect.Width} × {pixelRect.Height} px";
        _dimensionLabel.IsVisible = true;
        _dimensionLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelWidth = _dimensionLabel.DesiredSize.Width;
        var labelHeight = _dimensionLabel.DesiredSize.Height;
        var surfaceWidth = Math.Max(0, _surface.Bounds.Width);
        var surfaceHeight = Math.Max(0, _surface.Bounds.Height);
        var displayRect = ClampToSurface(rect, surfaceWidth, surfaceHeight);
        const double offset = 8;

        var left = displayRect.Left + ((displayRect.Width - labelWidth) / 2);
        left = Math.Clamp(left, 0, Math.Max(0, surfaceWidth - labelWidth));
        var below = displayRect.Bottom + offset;
        var top = below + labelHeight <= surfaceHeight
            ? below
            : displayRect.Top - labelHeight - offset;
        top = Math.Clamp(top, 0, Math.Max(0, surfaceHeight - labelHeight));
        Canvas.SetLeft(_dimensionLabel, left);
        Canvas.SetTop(_dimensionLabel, top);
    }

    private static Bitmap CreateBitmap(CapturedImage image)
    {
        return new Bitmap(new MemoryStream(image.PngBytes));
    }

    private static Rectangle CreateDimRectangle(IBrush fill)
    {
        return new Rectangle
        {
            Fill = fill,
            IsHitTestVisible = false,
        };
    }

    private void DrawDimming(UiRect? selection)
    {
        var surfaceWidth = Math.Max(0, _surface.Bounds.Width);
        var surfaceHeight = Math.Max(0, _surface.Bounds.Height);
        var rect = selection is { Width: > 0, Height: > 0 } selected
            ? ClampToSurface(selected, surfaceWidth, surfaceHeight)
            : new UiRect(0, surfaceHeight, 0, 0);

        SetRect(_dimTop, 0, 0, surfaceWidth, rect.Top);
        SetRect(_dimBottom, 0, rect.Bottom, surfaceWidth, surfaceHeight - rect.Bottom);
        SetRect(_dimLeft, 0, rect.Top, rect.Left, rect.Height);
        SetRect(_dimRight, rect.Right, rect.Top, surfaceWidth - rect.Right, rect.Height);
    }

    private static UiRect ClampToSurface(UiRect rect, double surfaceWidth, double surfaceHeight)
    {
        var left = Math.Clamp(rect.Left, 0, surfaceWidth);
        var top = Math.Clamp(rect.Top, 0, surfaceHeight);
        var right = Math.Clamp(rect.Right, left, surfaceWidth);
        var bottom = Math.Clamp(rect.Bottom, top, surfaceHeight);

        return new UiRect(left, top, right - left, bottom - top);
    }

    private static void SetRect(Rectangle rectangle, double left, double top, double width, double height)
    {
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
    }
}
