using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClippyPlus.App.Models;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

internal sealed class RadialClipboardStack : Panel
{
    private const int VisibleCardCount = 12;
    private const int ThumbnailCacheSize = VisibleCardCount + 6;
    private const int ThumbnailLongEdge = 512;
    private const double PreferredCardHeight = 165;
    private static readonly TimeSpan MotionDuration = TimeSpan.FromMilliseconds(180);

    private readonly Func<ClipboardHistoryItem, byte[]?> _loadImage;
    private readonly string _title;
    private readonly StackPalette _palette = new();
    private readonly StackCard[] _cards = new StackCard[VisibleCardCount + 1];
    private readonly Dictionary<ImageKey, Thumbnail> _thumbnails = [];
    private readonly Button _header;
    private readonly DispatcherTimer _exitTimer;
    private StackCard? _exitingCard;
    private ClipboardHistoryItem? _firstClickItem;
    private Rect _cardBounds;
    private double _fanStep;
    private double _wheelRemainder;
    private int _selectedIndex = -1;
    private int _fanDirection;
    private int _thumbnailGeneration;
    private long _thumbnailAccess;
    private bool _selected;
    private bool _attached;
    private bool _thumbnailsEnabled;
    private bool _loadingThumbnail;

    public RadialClipboardStack(string title, RadialClipboardIcon icon, Func<ClipboardHistoryItem, byte[]?> loadImage, int fanDirection = 0)
    {
        _loadImage = loadImage;
        _title = title;
        _fanDirection = Math.Sign(fanDirection);
        Background = Brushes.Transparent;

        var label = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = _palette.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var symbol = RadialClipboardVisuals.CreateIcon(icon, 20, _palette.Accent);
        var headerContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 6,
            Children = { symbol, label },
        };
        Grid.SetColumn(label, 1);
        _header = new Button
        {
            Padding = new Thickness(12, 0),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = headerContent,
            ZIndex = VisibleCardCount + 2,
        };
        _palette.ApplyButtonInteraction(_header);
        AutomationProperties.SetName(_header, title);
        ToolTip.SetTip(_header, title);
        _header.Click += (_, _) => HeaderActivated?.Invoke(this);
        Children.Add(_header);

        for (var index = 0; index < _cards.Length; index++)
        {
            var card = new StackCard(_palette);
            card.SelectButton.Click += (_, _) => SelectCard(card);
            card.SelectButton.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Return && SelectCard(card))
                {
                    e.Handled = true;
                    Activated?.Invoke(this);
                }
            }, RoutingStrategies.Tunnel);
            card.CopyButton.Click += (_, _) =>
            {
                if (SelectCard(card))
                {
                    Activated?.Invoke(this);
                }
            };
            card.SelectButton.PointerEntered += (_, e) =>
            {
                if (card.Item is { } item) PreviewEntered?.Invoke(item, e);
            };
            card.SelectButton.PointerMoved += (_, e) =>
            {
                if (card.Item is { } item) PreviewMoved?.Invoke(item, e);
            };
            card.SelectButton.PointerExited += (_, _) => PreviewExited?.Invoke();
            _cards[index] = card;
            Children.Add(card);
        }

        _exitTimer = new DispatcherTimer { Interval = MotionDuration };
        _exitTimer.Tick += (_, _) => FinishExit();
        AddHandler(PointerPressedEvent, OnStackPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnStackPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, OnStackContextRequested, RoutingStrategies.Tunnel);
        SetSelected(false);
    }

    public event Action<RadialClipboardStack>? SelectionChanged;
    public event Action<RadialClipboardStack>? Activated;
    public event Action<RadialClipboardStack>? HeaderActivated;
    public event Action<ClipboardHistoryItem, PointerEventArgs>? PreviewEntered;
    public event Action<ClipboardHistoryItem, PointerEventArgs>? PreviewMoved;
    public event Action? PreviewExited;

    public IReadOnlyList<ClipboardHistoryItem> Items { get; private set; } = [];
    public ClipboardHistoryItem? SelectedItem => _selectedIndex >= 0 ? Items[_selectedIndex] : null;

    public int FanDirection
    {
        get => _fanDirection;
        set
        {
            var direction = Math.Sign(value);
            if (_fanDirection == direction) return;

            _fanDirection = direction;
            ApplyCardPositions();
        }
    }

    public void SetContextMenu(ContextMenu? menu) => ContextMenu = menu;

    public void SetHeaderToolTip(string? text) => ToolTip.SetTip(_header, text ?? _title);

    public void SetSelected(bool selected)
    {
        _selected = selected;
        _header.Background = selected ? _palette.SelectedHeader : _palette.Header;
        _header.BorderBrush = selected ? _palette.Accent : _palette.Outline;
        foreach (var card in _cards)
        {
            card.SetGroupSelected(selected);
        }
    }

    public void SetItems(IReadOnlyList<ClipboardHistoryItem> items, bool reset = false)
    {
        var selectedId = reset ? null : SelectedItem?.Id;
        Items = items;
        var previousIndex = FindItemIndex(selectedId);
        _selectedIndex = items.Count == 0 ? -1 : Math.Max(0, previousIndex);
        _wheelRemainder = 0;
        _firstClickItem = null;
        _thumbnailsEnabled = true;
        UpdateCards(0);
    }

    public bool MoveSelection(int delta)
    {
        if (_selectedIndex < 0 || delta == 0)
        {
            return false;
        }

        return SelectIndex(Math.Clamp(_selectedIndex + delta, 0, Items.Count - 1));
    }

    public void ReleaseThumbnails()
    {
        _thumbnailsEnabled = false;
        _thumbnailGeneration++;
        foreach (var card in _cards)
        {
            card.SetThumbnail(null);
        }

        foreach (var thumbnail in _thumbnails.Values)
        {
            thumbnail.Bitmap?.Dispose();
        }

        _thumbnails.Clear();
        FinishExit();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : 240,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 176);
        var (header, card, fanStep) = GetLayout(size);
        _header.Measure(header.Size);
        foreach (var slot in _cards)
        {
            slot.SetPreviewMetrics(fanStep, card.Height);
            slot.Measure(card.Size);
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (header, card, fanStep) = GetLayout(finalSize);
        var headerWidth = Math.Min(header.Width, Math.Max(92, _header.DesiredSize.Width));
        _header.Arrange(new Rect((finalSize.Width - headerWidth) / 2, header.Y, headerWidth, header.Height));
        foreach (var slot in _cards)
        {
            slot.Arrange(card);
        }

        if (card != _cardBounds || Math.Abs(fanStep - _fanStep) > 0.1)
        {
            _cardBounds = card;
            _fanStep = fanStep;
            ApplyCardPositions();
        }

        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _thumbnailsEnabled = true;
        LoadVisibleThumbnails();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ReleaseThumbnails();
        base.OnDetachedFromVisualTree(e);
    }

    private static (Rect Header, Rect Card, double FanStep) GetLayout(Size size)
    {
        var headerHeight = Math.Min(34, size.Height * 0.25);
        var headerWidth = Math.Min(200, size.Width - Math.Min(size.Width * 0.06, 12));
        var margin = Math.Min(18, size.Width * 0.055);
        var cardWidth = Math.Max(0, size.Width - margin * 2);
        var cardHeight = Math.Min(PreferredCardHeight, Math.Min(size.Height * 0.49, cardWidth * 0.423));
        var cardTop = Math.Max(headerHeight + 6, size.Height - cardHeight - 7);
        var fanStep = Math.Clamp((cardTop - headerHeight - 7) / (VisibleCardCount - 1), 0, 23);
        return (
            new Rect((size.Width - headerWidth) / 2, 0, headerWidth, headerHeight),
            new Rect(margin, cardTop, cardWidth, cardHeight),
            fanStep);
    }

    private void OnStackPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_selectedIndex < 0 || e.Delta.Y == 0)
        {
            return;
        }

        _wheelRemainder += e.Delta.Y;
        var steps = (int)_wheelRemainder;
        _wheelRemainder -= steps;
        if (steps == 0)
        {
            SelectIndex(_selectedIndex);
        }
        else
        {
            MoveSelection(steps);
        }

        e.Handled = true;
    }

    private void OnStackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Visual;
        var card = FindCard(source);
        var pointer = e.GetCurrentPoint(this).Properties;
        var button = source as Button ?? source?.FindAncestorOfType<Button>();
        if (!pointer.IsLeftButtonPressed || card is not null && ReferenceEquals(button, card.CopyButton))
        {
            _firstClickItem = null;
            return;
        }

        // After the first click, a rear card moves out from under the pointer.
        if (e.ClickCount == 2 && _firstClickItem is not null && FindItemIndex(_firstClickItem.Id) is var index && index >= 0)
        {
            _firstClickItem = null;
            SelectIndex(index);
            Activated?.Invoke(this);
            e.Handled = true;
            return;
        }

        _firstClickItem = card?.Item;
    }

    private void OnStackContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (FindCard(e.Source as Visual) is not { } card || !SelectCard(card))
        {
            e.Handled = true;
        }
    }

    private static StackCard? FindCard(Visual? source) => source as StackCard ?? source?.FindAncestorOfType<StackCard>();

    private bool SelectCard(StackCard card)
    {
        var index = card.Item is null ? -1 : _selectedIndex + card.Depth;
        if (index < 0 || index >= Items.Count) return false;

        SelectIndex(index);
        return true;
    }

    private bool SelectIndex(int index)
    {
        var focusedCard = FindCard(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual);
        var movement = index - _selectedIndex;
        if (movement != 0)
        {
            _selectedIndex = index;
            PreviewExited?.Invoke();
            UpdateCards(movement);
        }

        if (focusedCard is not null)
        {
            var frontCard = _cards.First(card => card.IsHitTestVisible && card.Item is not null && card.Depth == 0);
            if (focusedCard != frontCard)
            {
                frontCard.SelectButton.Focus();
            }
        }

        SelectionChanged?.Invoke(this);
        return movement != 0;
    }

    private int FindItemIndex(long? id)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index].Id == id) return index;
        }

        return -1;
    }

    private void UpdateCards(int movement)
    {
        _exitTimer.Stop();
        var count = _selectedIndex < 0 ? 0 : Math.Min(VisibleCardCount, Items.Count - _selectedIndex);
        var items = new ArraySegment<ClipboardHistoryItem>(new ClipboardHistoryItem[VisibleCardCount], 0, count);
        for (var depth = 0; depth < count; depth++) items[depth] = Items[_selectedIndex + depth];

        var animate = movement != 0 && _attached && IsEffectivelyVisible;
        _exitingCard = !animate ? null : _cards
            .Where(card => card.IsHitTestVisible && card.Item is { } item && !items.Any(next => next.Id == item.Id))
            .MinBy(card => movement > 0 ? card.Depth : -card.Depth);
        foreach (var card in _cards)
        {
            if (card.Item is not { } item || items.Any(next => next.Id == item.Id) || ReferenceEquals(card, _exitingCard))
            {
                continue;
            }

            card.Clear();
        }

        if (_exitingCard is { } exiting)
        {
            exiting.IsHitTestVisible = false;
            exiting.ZIndex = movement > 0 ? VisibleCardCount + 1 : 0;
            SetPose(exiting, movement > 0 ? -1 : VisibleCardCount, 0, true);
            _exitTimer.Start();
        }

        for (var depth = 0; depth < count; depth++)
        {
            var item = items[depth];
            var card = _cards.FirstOrDefault(slot => slot.Item?.Id == item.Id);
            var isNew = card is null;
            card ??= _cards.First(slot => slot.Item is null);
            card.Bind(item, depth);
            card.ZIndex = VisibleCardCount - depth;
            card.IsHitTestVisible = true;
            card.SetGroupSelected(_selected);
            var key = ImageKey.For(item);
            if (item.IsImage && _thumbnails.TryGetValue(key, out var cached))
            {
                cached.LastAccess = ++_thumbnailAccess;
                card.SetThumbnail(cached.Bitmap);
            }

            if (animate && isNew)
            {
                SetPose(card, movement > 0 ? VisibleCardCount : -1, 0, false);
            }

            SetPose(card, depth, 1, animate);
        }

        LoadVisibleThumbnails();
    }

    private void ApplyCardPositions()
    {
        FinishExit();
        foreach (var card in _cards)
        {
            if (card.Item is not null) SetPose(card, card.Depth, 1, false);
        }
    }

    private void FinishExit()
    {
        _exitTimer.Stop();
        _exitingCard?.Clear();
        _exitingCard = null;
    }

    private void SetPose(StackCard card, int depth, double opacity, bool animate)
    {
        card.Transitions = animate ? card.Motion : null;
        card.SetPreviewMetrics(_fanStep, _cardBounds.Height);
        var transform = TransformOperations.CreateBuilder(3);
        var scale = 1 - Math.Max(0, depth) * 0.013;
        transform.AppendScale(scale, scale);
        transform.AppendRotate(_fanDirection * depth * 0.35 * Math.PI / 180);
        transform.AppendTranslate(_fanDirection * depth * Math.Min(2.5, _cardBounds.Width * 0.007), -depth * _fanStep);
        card.RenderTransform = transform.Build();
        card.Opacity = opacity;
    }

    private async void LoadVisibleThumbnails()
    {
        if (_loadingThumbnail || !_attached || !_thumbnailsEnabled)
        {
            return;
        }

        _loadingThumbnail = true;
        try
        {
            while (_attached && _thumbnailsEnabled)
            {
                var card = _cards.Where(slot => slot.IsHitTestVisible && slot.Item is { IsImage: true } item
                    && !_thumbnails.ContainsKey(ImageKey.For(item))).MinBy(slot => slot.Depth);
                if (card?.Item is not { } item) break;

                var key = ImageKey.For(item);
                var generation = _thumbnailGeneration;
                var bitmap = await Task.Run(() => DecodeThumbnail(item));
                if (generation != _thumbnailGeneration || !_cards.Any(slot => slot.Item is { } current && ImageKey.For(current) == key))
                {
                    bitmap?.Dispose();
                    continue;
                }

                _thumbnails[key] = new Thumbnail(bitmap, ++_thumbnailAccess);
                foreach (var slot in _cards)
                {
                    if (slot.Item is { IsImage: true } current && ImageKey.For(current) == key) slot.SetThumbnail(bitmap);
                }

                TrimThumbnails();
            }
        }
        finally
        {
            _loadingThumbnail = false;
        }
    }

    private Bitmap? DecodeThumbnail(ClipboardHistoryItem item)
    {
        try
        {
            return _loadImage(item) is { Length: > 0 } bytes
                ? RadialClipboardVisuals.DecodeImage(bytes, ThumbnailLongEdge)
                : null;
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard thumbnail unavailable ({error.GetType().Name}).");
            return null;
        }
    }

    private void TrimThumbnails()
    {
        if (_thumbnails.Count <= ThumbnailCacheSize) return;

        var expired = _thumbnails
            .Where(entry => !_cards.Any(card => card.Item is { } item && ImageKey.For(item) == entry.Key))
            .MinBy(entry => entry.Value.LastAccess);
        expired.Value.Bitmap?.Dispose();
        _thumbnails.Remove(expired.Key);
    }

    private readonly record struct ImageKey(long Id, string Hash)
    {
        public static ImageKey For(ClipboardHistoryItem item) => new(item.Id, item.ContentHash);
    }

    private sealed class Thumbnail(Bitmap? bitmap, long lastAccess)
    {
        public Bitmap? Bitmap { get; } = bitmap;
        public long LastAccess { get; set; } = lastAccess;
    }

    private sealed class StackPalette
    {
        public IBrush Outline { get; } = RadialClipboardVisuals.Brush("ClippyBorderBrush", Color.FromRgb(69, 69, 69));
        public IBrush Text { get; } = RadialClipboardVisuals.Brush("ClippyTextBrush", Color.FromRgb(242, 242, 242));
        public IBrush Muted { get; } = RadialClipboardVisuals.Brush("ClippyMutedTextBrush", Color.FromRgb(176, 176, 176));
        public IBrush Accent { get; } = RadialClipboardVisuals.Brush("ClippySelectionBorderBrush", Color.FromRgb(106, 158, 232));
        public IBrush Surface { get; } = RadialClipboardVisuals.Brush("ClippySurfaceBrush", Color.FromRgb(38, 38, 38));
        public IBrush Face { get; } = RadialClipboardVisuals.Brush("ClippyElevatedBrush", Color.FromRgb(48, 48, 48));
        public IBrush Hover { get; } = RadialClipboardVisuals.Brush("ClippyHoverBrush", Color.FromRgb(54, 54, 54));
        public IBrush Header { get; }
        public IBrush SelectedHeader { get; }

        public StackPalette()
        {
            var selection = RadialClipboardVisuals.Brush("ClippySelectionBrush", Color.FromRgb(45, 73, 112));
            Header = RadialClipboardVisuals.Mix(Face, selection, 0.46);
            SelectedHeader = RadialClipboardVisuals.Mix(Face, selection, 0.8);
        }

        public void ApplyButtonInteraction(Button button)
        {
            button.Resources["ButtonBackgroundPointerOver"] = Hover;
            button.Resources["ButtonBackgroundPressed"] = SelectedHeader;
            button.Resources["ButtonBorderBrushPointerOver"] = Accent;
            button.Resources["ButtonBorderBrushPressed"] = Accent;
            button.Resources["ButtonForegroundPointerOver"] = Text;
            button.Resources["ButtonForegroundPressed"] = Text;
        }
    }

    private sealed class StackCard : Grid
    {
        private const int FrontPreviewLength = 512;
        private const int FrontPreviewMaxLines = 6;
        private const double FrontPreviewLineHeight = 20;
        private const double FrontPreviewVerticalInset = 7;
        private static readonly CornerRadius CardRadius = new(20);
        private static readonly BoxShadows FrontShadow = BoxShadows.Parse("0 4 34 3 #A0000000");
        private static readonly BoxShadows RearShadow = BoxShadows.Parse("0 2 18 1 #68000000");
        private static readonly BoxShadows DeepShadow = BoxShadows.Parse("0 1 9 0 #30000000");
        private readonly StackPalette _palette;
        private readonly Border _frame;
        private readonly Border _outline;
        private readonly TextBlock _preview;
        private readonly Image _image;
        private readonly Control _imagePlaceholder;
        private bool _selected;

        public StackCard(StackPalette palette)
        {
            _palette = palette;
            IsVisible = false;
            IsHitTestVisible = false;
            RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
            Motion =
            [
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = MotionDuration, Easing = new CubicEaseOut() },
                new DoubleTransition { Property = OpacityProperty, Duration = MotionDuration, Easing = new CubicEaseOut() },
            ];
            _preview = new TextBlock
            {
                Foreground = palette.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _image = new Image { Stretch = Stretch.UniformToFill };
            _imagePlaceholder = RadialClipboardVisuals.CreateIcon(RadialClipboardIcon.Image, 36, palette.Muted);
            _frame = new Border { CornerRadius = CardRadius, IsHitTestVisible = false };
            var film = new Border
            {
                CornerRadius = CardRadius,
                ClipToBounds = true,
                IsHitTestVisible = false,
                Child = new Grid { Children = { _image, _imagePlaceholder, _preview } },
            };
            _outline = new Border { CornerRadius = CardRadius, BorderThickness = new Thickness(1), IsHitTestVisible = false };

            SelectButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.Hand),
                Template = new FuncControlTemplate<Button>((_, _) => new Border { CornerRadius = CardRadius, Background = Brushes.Transparent }),
            };
            SelectButton.PointerEntered += (_, _) => UpdateStyle(true);
            SelectButton.PointerExited += (_, _) => UpdateStyle(false);
            SelectButton.GotFocus += (_, _) => UpdateStyle(SelectButton.IsPointerOver);
            SelectButton.LostFocus += (_, _) => UpdateStyle(SelectButton.IsPointerOver);

            var copyLabel = LocalizationService.T("History.Copy");
            CopyButton = new Button
            {
                Width = 42,
                Height = 42,
                HorizontalAlignment = HorizontalAlignment.Right,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = RadialClipboardVisuals.CreateIcon(RadialClipboardIcon.Copy, 27, palette.Text),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(0),
                Cursor = SelectButton.Cursor,
            };
            palette.ApplyButtonInteraction(CopyButton);
            AutomationProperties.SetName(CopyButton, copyLabel);
            ToolTip.SetTip(CopyButton, copyLabel);

            Children.Add(_frame);
            Children.Add(film);
            Children.Add(_outline);
            Children.Add(SelectButton);
            Children.Add(CopyButton);
        }

        public ClipboardHistoryItem? Item { get; private set; }
        public int Depth { get; private set; }
        public Button SelectButton { get; }
        public Button CopyButton { get; }
        public Transitions Motion { get; }

        public void Bind(ClipboardHistoryItem item, int depth)
        {
            if (Item?.Id != item.Id || Item.ContentHash != item.ContentHash) SetThumbnail(null);

            var isFront = depth == 0;
            var isImage = item.IsImage;
            if (!ReferenceEquals(Item, item) || isFront != (Depth == 0))
            {
                _preview.Text = isImage ? null : isFront ? CreateFrontPreview(item) : item.Preview;
            }

            Item = item;
            Depth = depth;
            _preview.IsVisible = !isImage;
            _preview.TextWrapping = isFront ? TextWrapping.Wrap : TextWrapping.NoWrap;
            _preview.VerticalAlignment = isFront ? VerticalAlignment.Center : VerticalAlignment.Top;
            _preview.Margin = isFront
                ? new Thickness(22, FrontPreviewVerticalInset, 62, FrontPreviewVerticalInset)
                : new Thickness(20, 1, 20, 0);
            _preview.Foreground = isFront ? _palette.Text : _palette.Muted;
            _image.IsVisible = isImage;
            _image.Stretch = isFront ? Stretch.Uniform : Stretch.UniformToFill;
            _imagePlaceholder.IsVisible = isImage && _image.Source is null;
            CopyButton.IsVisible = isFront;
            CopyButton.Margin = new Thickness(0, 0, 12, isImage ? 10 : 0);
            CopyButton.VerticalAlignment = isImage ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            CopyButton.Background = isImage ? _palette.Surface : Brushes.Transparent;
            _frame.BoxShadow = depth switch
            {
                0 => FrontShadow,
                <= 3 => RearShadow,
                _ => DeepShadow,
            };
            AutomationProperties.SetName(SelectButton, isImage ? LocalizationService.T("History.Images") : item.Preview);
            AutomationProperties.SetHelpText(SelectButton, item.UpdatedAtText);
            IsVisible = true;
        }

        public void SetPreviewMetrics(double fanStep, double cardHeight)
        {
            var isFront = Depth == 0;
            var rearLineHeight = Math.Clamp(fanStep - 1, 7, 12);
            var availableHeight = cardHeight - 2 * FrontPreviewVerticalInset;
            _preview.MaxLines = isFront
                ? Math.Clamp((int)(availableHeight / FrontPreviewLineHeight), 1, FrontPreviewMaxLines)
                : 1;
            _preview.FontSize = isFront ? 16 : rearLineHeight - 1;
            _preview.LineHeight = isFront ? FrontPreviewLineHeight : rearLineHeight;
        }

        private static string CreateFrontPreview(ClipboardHistoryItem item)
        {
            var text = item.TextContent ?? item.Preview;
            var excerpt = text.AsSpan().Trim();
            if (excerpt.Length <= FrontPreviewLength)
            {
                return excerpt.Length == text.Length ? text : excerpt.ToString();
            }

            var length = char.IsHighSurrogate(excerpt[FrontPreviewLength - 1])
                ? FrontPreviewLength - 1
                : FrontPreviewLength;
            return string.Concat(excerpt[..length].TrimEnd(), "…");
        }

        public void SetGroupSelected(bool selected)
        {
            _selected = selected;
            UpdateStyle(SelectButton.IsPointerOver);
        }

        public void SetThumbnail(Bitmap? thumbnail)
        {
            _image.Source = thumbnail;
            _imagePlaceholder.IsVisible = Item is { IsImage: true } && thumbnail is null;
        }

        public void Clear()
        {
            IsVisible = false;
            IsHitTestVisible = false;
            Transitions = null;
            Item = null;
            _preview.Text = null;
            SetThumbnail(null);
        }

        private void UpdateStyle(bool hover)
        {
            _frame.Background = hover ? _palette.Hover : Depth == 0 ? _palette.Face : _palette.Surface;
            _outline.BorderBrush = Depth == 0 && (_selected || SelectButton.IsFocused) || hover ? _palette.Accent : _palette.Outline;
        }
    }
}
