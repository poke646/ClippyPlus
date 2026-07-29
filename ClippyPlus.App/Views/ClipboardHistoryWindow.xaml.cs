using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClippyPlus.App.Models;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

public sealed class ClipboardHistoryWindow : Window
{
    private const int MaxHistoryItems = ClipboardHistoryStore.MaxHistoryItems;
    private const int HistoryPreviewDelayMs = 1000;
    private const int SearchActivationGuardMs = 300;
    private const double HistoryPreviewOffset = 14;
    private const double HistoryItemHeight = 52;
    private const double HistoryImagePreviewWidth = 76;
    private const double HistoryImagePreviewHeight = 44;

    private readonly IClipboardHistoryStore _historyStore;
    private readonly IClipboardService _clipboardService;
    private readonly IClipboardHistoryListener _clipboardHistoryListener;
    private readonly IWindowPrivacyService _windowPrivacyService;
    private readonly ListBox _historyListBox;
    private readonly TextBlock _emptyTextBlock;
    private readonly TextBox _searchTextBox;
    private readonly StackPanel _tabsPanel;
    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly DispatcherTimer _previewTimer;
    private readonly Canvas _previewCanvas;
    private readonly ContentControl _previewPopup;
    private ClipboardHistoryItem? _pendingPreviewItem;
    private long? _visiblePreviewItemId;
    private Point _previewPointerPosition;
    private bool _hideSharingEnabled;
    private bool _changingSearchText;
    private long? _activeSectionId;
    private IReadOnlyList<ClipboardSection> _sections = [];
    private string _historyQuery = string.Empty;
    private readonly Dictionary<long, string> _sectionQueries = [];
    private DateTime _ignoreSearchActivationUntil;

    public ClipboardHistoryWindow(
        IClipboardHistoryStore historyStore,
        IClipboardService clipboardService,
        IClipboardHistoryListener clipboardHistoryListener,
        bool hideSharingEnabled,
        IWindowPrivacyService windowPrivacyService)
    {
        _historyStore = historyStore;
        _clipboardService = clipboardService;
        _clipboardHistoryListener = clipboardHistoryListener;
        _hideSharingEnabled = hideSharingEnabled;
        _windowPrivacyService = windowPrivacyService;

        Title = LocalizationService.T("History.Title");
        Width = 720;
        Height = 460;
        MinWidth = 480;
        MinHeight = 300;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;

        _emptyTextBlock = new TextBlock
        {
            Text = LocalizationService.T("History.Empty"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray,
        };

        _historyListBox = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<ClipboardHistoryItem>((item, _) => CreateHistoryItemView(item)),
            SelectionMode = SelectionMode.Single,
        };
        _historyListBox.DoubleTapped += async (_, _) => await CopySelectedItemAndCloseAsync();
        _historyListBox.AddHandler(KeyDownEvent, OnHistoryListBoxKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _historyListBox.PointerPressed += OnHistoryListBoxPointerPressed;
        _historyListBox.ContextMenu = CreateContextMenu();

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30),
        };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RefreshItems();
        };

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(HistoryPreviewDelayMs),
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            ShowPreview();
        };

        _searchTextBox = new TextBox
        {
            PlaceholderText = LocalizationService.T("History.Search"),
            Margin = new Avalonia.Thickness(8),
        };
        _searchTextBox.TextChanged += (_, _) =>
        {
            if (_changingSearchText)
            {
                return;
            }

            SetActiveQuery(_searchTextBox.Text ?? string.Empty);
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        };

        _tabsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Avalonia.Thickness(8, 4),
        };
        var tabsScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _tabsPanel,
        };

        var contentGrid = new Grid
        {
            Children =
            {
                _emptyTextBlock,
                _historyListBox,
            },
        };

        _previewPopup = new ContentControl
        {
            IsVisible = false,
            IsHitTestVisible = false,
        };
        _previewCanvas = new Canvas
        {
            IsHitTestVisible = false,
            Children = { _previewPopup },
        };

        var windowBody = new Border
        {
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Avalonia.Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                },
                Children =
                {
                    contentGrid,
                    tabsScrollViewer,
                    _searchTextBox,
                },
            },
        };
        Grid.SetRow(tabsScrollViewer, 1);
        Grid.SetRow(_searchTextBox, 2);
        Content = new Grid
        {
            Children =
            {
                windowBody,
                _previewCanvas,
            },
        };
        Opened += (_, _) =>
        {
            ResetForOpen();
            CenterOnScreen();
            HidePreview();
            ApplyWindowDisplayAffinity();
            FocusSearch();
        };
        Deactivated += (_, _) =>
        {
            HidePreview();
            Hide();
        };
        AddHandler(KeyDownEvent, OnWindowKeyDownTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyDown += OnKeyDown;
    }

    public void SetHideSharingEnabled(bool enabled)
    {
        _hideSharingEnabled = enabled;
        ApplyWindowDisplayAffinity();
    }

    public void CenterOnScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scale = RenderScaling;
        var width = Math.Max(1, (int)Math.Round((Bounds.Width > 0 ? Bounds.Width : Width) * scale));
        var height = Math.Max(1, (int)Math.Round((Bounds.Height > 0 ? Bounds.Height : Height) * scale));
        var workingArea = screen.WorkingArea;
        Position = new PixelPoint(
            workingArea.X + Math.Max(0, (workingArea.Width - width) / 2),
            workingArea.Y + Math.Max(0, (workingArea.Height - height) / 2));
    }

    public void GuardSearchActivation()
    {
        _ignoreSearchActivationUntil = DateTime.UtcNow.AddMilliseconds(SearchActivationGuardMs);
    }

    public void ReloadSections()
    {
        _sections = _historyStore.GetSections();
        if (_activeSectionId is { } activeId && _sections.All(section => section.Id != activeId))
        {
            _activeSectionId = null;
            _changingSearchText = true;
            _searchTextBox.Text = _historyQuery;
            _changingSearchText = false;
        }

        RebuildTabs();
        RefreshItems();
    }

    public void ResetForOpen()
    {
        GuardSearchActivation();
        _searchDebounceTimer.Stop();
        _historyQuery = string.Empty;
        _sectionQueries.Clear();
        _activeSectionId = null;
        _changingSearchText = true;
        _searchTextBox.Text = string.Empty;
        _changingSearchText = false;
        ReloadSections();
        FocusSearch();
    }

    private void RefreshItems()
    {
        HidePreview();
        var query = _searchTextBox.Text;
        IReadOnlyList<ClipboardHistoryItem> items;
        if (_activeSectionId is { } sectionId)
        {
            items = string.IsNullOrWhiteSpace(query)
                ? _historyStore.GetSectionItems(sectionId, MaxHistoryItems)
                : _historyStore.SearchSectionText(sectionId, query, MaxHistoryItems);
        }
        else
        {
            items = string.IsNullOrWhiteSpace(query)
                ? _historyStore.GetRecent(MaxHistoryItems)
                : _historyStore.SearchText(query, MaxHistoryItems);
        }

        _historyListBox.ItemsSource = items;
        _emptyTextBlock.Text = _activeSectionId is null
            ? LocalizationService.T("History.Empty")
            : LocalizationService.T("History.SectionEmpty");
        _emptyTextBlock.IsVisible = items.Count == 0;
        _historyListBox.IsVisible = items.Count > 0;
        if (items.Count > 0)
        {
            _historyListBox.SelectedIndex = 0;
            _historyListBox.ScrollIntoView(items[0]);
        }
    }

    private void RebuildTabs()
    {
        _tabsPanel.Children.Clear();
        _tabsPanel.Children.Add(CreateTabButton(LocalizationService.T("History.TabHistory"), null));
        foreach (var section in _sections)
        {
            _tabsPanel.Children.Add(CreateTabButton(section.Name, section.Id));
        }
    }

    private Button CreateTabButton(string label, long? sectionId)
    {
        var isActive = _activeSectionId == sectionId;
        var button = new Button
        {
            Content = label,
            FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal,
            Background = isActive
                ? new SolidColorBrush(Color.FromRgb(60, 60, 60))
                : Brushes.Transparent,
            Padding = new Avalonia.Thickness(12, 5),
        };
        button.Click += (_, _) => ActivateSection(sectionId);
        return button;
    }

    private void ActivateSection(long? sectionId)
    {
        if (_activeSectionId == sectionId)
        {
            FocusSearch();
            return;
        }

        SetActiveQuery(_searchTextBox.Text ?? string.Empty);
        _activeSectionId = sectionId;
        var query = sectionId is { } id && _sectionQueries.TryGetValue(id, out var sectionQuery)
            ? sectionQuery
            : sectionId is null ? _historyQuery : string.Empty;
        _changingSearchText = true;
        _searchTextBox.Text = query;
        _changingSearchText = false;
        RebuildTabs();
        RefreshItems();
        FocusSearch();
    }

    private void MoveToAdjacentSection(int delta)
    {
        var tabIds = new long?[] { null }
            .Concat(_sections.Select(section => (long?)section.Id))
            .ToList();
        var currentIndex = tabIds.FindIndex(sectionId => sectionId == _activeSectionId);
        var nextIndex = (currentIndex + delta + tabIds.Count) % tabIds.Count;
        var query = _searchTextBox.Text ?? string.Empty;

        SetActiveQuery(query);
        _activeSectionId = tabIds[nextIndex];
        SetActiveQuery(query);
        RebuildTabs();
        RefreshItems();
        FocusSearch();
    }

    private void SetActiveQuery(string query)
    {
        if (_activeSectionId is { } sectionId)
        {
            _sectionQueries[sectionId] = query;
        }
        else
        {
            _historyQuery = query;
        }
    }

    private void FocusSearch()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _searchTextBox.Focus();
            _searchTextBox.CaretIndex = _searchTextBox.Text?.Length ?? 0;
        });
    }

    private async void OnWindowKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            MoveToAdjacentSection(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Source is TextBox)
        {
            if (IsConfirmKey(e.Key))
            {
                await CopySelectedItemAndCloseAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                MoveSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSelection(-1);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None)
        {
            if (DateTime.UtcNow < _ignoreSearchActivationUntil)
            {
                e.Handled = true;
                return;
            }

            _searchTextBox.Focus();
            _searchTextBox.SelectAll();
            e.Handled = true;
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (IsConfirmKey(e.Key))
        {
            await CopySelectedItemAndCloseAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private async void OnHistoryListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsConfirmKey(e.Key))
        {
            await CopySelectedItemAndCloseAsync();
            e.Handled = true;
        }
    }

    private void DeleteSelected()
    {
        if (_historyListBox.SelectedItem is not ClipboardHistoryItem item)
        {
            return;
        }

        if (_activeSectionId is null)
        {
            _historyStore.Delete(item.Id);
            RefreshItems();
        }
        else
        {
            _historyStore.DeletePinned(item.Id);
            ReloadSections();
        }
    }

    private void DeleteAll()
    {
        _historyStore.DeleteAll();
        RefreshItems();
    }

    private void UnpinSelected()
    {
        if (_activeSectionId is null || _historyListBox.SelectedItem is not ClipboardHistoryItem item)
        {
            return;
        }

        _historyStore.Unpin(item.Id);
        ReloadSections();
    }

    private async Task PinSelectedAsync(long sectionId)
    {
        if (_activeSectionId is not null || _historyListBox.SelectedItem is not ClipboardHistoryItem item)
        {
            return;
        }

        var result = _historyStore.Pin(item.Id, sectionId);
        if (result == PinClipboardItemResult.SectionFull)
        {
            await MessageWindow.ShowAsync(
                "ClippyPlus",
                LocalizationService.T("History.SectionFull"),
                this);
        }
        else if (result != PinClipboardItemResult.Success)
        {
            await MessageWindow.ShowAsync(
                "ClippyPlus",
                LocalizationService.T("History.PinFailed"),
                this);
        }

        ReloadSections();
    }

    private async Task CopySelectedItemAndCloseAsync()
    {
        if (_historyListBox.SelectedItem is not ClipboardHistoryItem item)
        {
            return;
        }

        using (_clipboardHistoryListener.IgnoreClipboardChanges())
        {
            if (item.Kind == ClipboardHistoryKind.Text && item.TextContent is { } text)
            {
                await _clipboardService.CopyTextAsync(text);
            }
            else if (item.Kind == ClipboardHistoryKind.Image && item.ImageBytes is { } imageBytes)
            {
                await _clipboardService.CopyImageAsync(imageBytes);
            }
        }

        HidePreview();
        Hide();
    }

    private void ApplyWindowDisplayAffinity()
    {
        _windowPrivacyService.SetExcludedFromCapture(this, _hideSharingEnabled);
    }

    private ContextMenu CreateContextMenu()
    {
        var editImageItem = new MenuItem { Header = LocalizationService.T("History.Edit") };
        editImageItem.Click += (_, _) => OpenSelectedImageEditor();
        var pinItem = new MenuItem { Header = LocalizationService.T("History.Pin") };
        var unpinItem = new MenuItem { Header = LocalizationService.T("History.Unpin") };
        unpinItem.Click += (_, _) => UnpinSelected();
        var deleteItem = new MenuItem { Header = LocalizationService.T("History.Delete") };
        deleteItem.Click += (_, _) => DeleteSelected();
        var deleteAllItem = new MenuItem { Header = LocalizationService.T("History.DeleteAll") };
        deleteAllItem.Click += (_, _) => DeleteAll();
        var menu = new ContextMenu
        {
            ItemsSource = new[] { editImageItem, pinItem, unpinItem, deleteItem, deleteAllItem },
        };
        menu.Opening += (_, _) =>
        {
            editImageItem.IsVisible = IsSelectedImage();
            var isHistory = _activeSectionId is null;
            pinItem.IsVisible = isHistory;
            unpinItem.IsVisible = !isHistory;
            deleteAllItem.IsVisible = isHistory;

            if (_sections.Count == 0)
            {
                pinItem.Header = LocalizationService.T("History.ConfigureSections");
                pinItem.ItemsSource = null;
                pinItem.IsEnabled = false;
            }
            else
            {
                pinItem.Header = LocalizationService.T("History.Pin");
                pinItem.IsEnabled = true;
                pinItem.ItemsSource = _sections.Select(section =>
                {
                    var target = new MenuItem { Header = section.Name };
                    target.Click += async (_, _) => await PinSelectedAsync(section.Id);
                    return target;
                }).ToArray();
            }
        };
        return menu;
    }

    private bool IsSelectedImage()
    {
        return _historyListBox.SelectedItem is ClipboardHistoryItem { IsImage: true, ImageBytes: not null };
    }

    private void OpenSelectedImageEditor()
    {
        if (_historyListBox.SelectedItem is not ClipboardHistoryItem { ImageBytes: { } imageBytes })
        {
            return;
        }

        HidePreview();
        Hide();

        var editor = new EditorWindow(CapturedImage.FromPng(imageBytes), _clipboardService);
        editor.Show();
        editor.Activate();
    }

    private void OnHistoryListBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_historyListBox).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (FindParent<ListBoxItem>(e.Source as Control) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void MoveSelection(int delta)
    {
        if (_historyListBox.ItemCount == 0)
        {
            return;
        }

        var current = _historyListBox.SelectedIndex < 0 ? 0 : _historyListBox.SelectedIndex;
        var next = Math.Clamp(current + delta, 0, _historyListBox.ItemCount - 1);
        _historyListBox.SelectedIndex = next;
        if (_historyListBox.SelectedItem is { } selectedItem)
        {
            _historyListBox.ScrollIntoView(selectedItem);
        }
    }

    private static T? FindParent<T>(Control? current)
        where T : Control
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current.Parent as Control;
        }

        return null;
    }

    private static bool IsConfirmKey(Key key)
    {
        return key is Key.Enter or Key.Return;
    }

    private Control CreateHistoryItemView(ClipboardHistoryItem? item)
    {
        if (item is null)
        {
            return new TextBlock();
        }

        var preview = new TextBlock
        {
            Text = item.IsText ? item.TextContent : string.Empty,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            MaxLines = 3,
        };
        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { preview },
        };

        var grid = new Grid
        {
            MinHeight = HistoryItemHeight,
            Margin = new Avalonia.Thickness(6, 2),
            Background = Brushes.Transparent,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(84)),
            },
        };

        if (item.IsImage && item.ImageBytes is { } imageBytes)
        {
            var imagePreview = new Border
            {
                Width = HistoryImagePreviewWidth,
                Height = HistoryImagePreviewHeight,
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                ClipToBounds = true,
                Child = new Image
                {
                    Source = new Bitmap(new MemoryStream(imageBytes)),
                    Stretch = Stretch.UniformToFill,
                },
            };
            grid.Children.Add(imagePreview);
        }
        else
        {
            grid.Children.Add(new Border { Width = 0 });
        }

        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var time = new TextBlock
        {
            Text = item.UpdatedAtText,
            Foreground = Brushes.Gray,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(time, 2);
        grid.Children.Add(time);
        AttachPreviewHover(grid, item);
        return grid;
    }

    private void AttachPreviewHover(Control target, ClipboardHistoryItem item)
    {
        target.PointerEntered += (_, e) => StartPreviewTimer(item, e.GetPosition(_previewCanvas));
        target.PointerMoved += (_, e) => UpdatePreviewPointer(item, e.GetPosition(_previewCanvas));
        target.PointerExited += (_, _) => HidePreview();
        target.DetachedFromVisualTree += (_, _) => HidePreview();
    }

    private void StartPreviewTimer(ClipboardHistoryItem item, Point pointerPosition)
    {
        _pendingPreviewItem = item;
        _previewPointerPosition = pointerPosition;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void UpdatePreviewPointer(ClipboardHistoryItem item, Point pointerPosition)
    {
        if (_pendingPreviewItem?.Id != item.Id && _visiblePreviewItemId != item.Id)
        {
            return;
        }

        _previewPointerPosition = pointerPosition;
        if (_visiblePreviewItemId == item.Id)
        {
            PositionPreview(pointerPosition, item);
        }
    }

    private void ShowPreview()
    {
        if (_pendingPreviewItem is not { } item)
        {
            return;
        }

        var preview = CreatePreviewContent(item);
        if (preview is null)
        {
            return;
        }

        _previewPopup.Content = preview;
        _previewPopup.IsVisible = true;
        _visiblePreviewItemId = item.Id;
        ApplyWindowDisplayAffinity();
        PositionPreview(_previewPointerPosition, item);
    }

    private void HidePreview()
    {
        _previewTimer.Stop();
        _pendingPreviewItem = null;
        _visiblePreviewItemId = null;
        _previewPopup.IsVisible = false;
        _previewPopup.Content = null;
    }

    private void PositionPreview(Point pointerPosition, ClipboardHistoryItem item)
    {
        var previewSize = GetPreviewSize(item);
        var left = pointerPosition.X + HistoryPreviewOffset;
        var top = pointerPosition.Y + HistoryPreviewOffset;
        var maxLeft = Math.Max(0, _previewCanvas.Bounds.Width - previewSize.Width - HistoryPreviewOffset);
        var maxTop = Math.Max(0, _previewCanvas.Bounds.Height - previewSize.Height - HistoryPreviewOffset);

        if (left > maxLeft)
        {
            left = Math.Max(0, pointerPosition.X - previewSize.Width - HistoryPreviewOffset);
        }

        if (top > maxTop)
        {
            top = Math.Max(0, pointerPosition.Y - previewSize.Height - HistoryPreviewOffset);
        }

        Canvas.SetLeft(_previewPopup, Math.Min(left, maxLeft));
        Canvas.SetTop(_previewPopup, Math.Min(top, maxTop));
    }

    private static Size GetPreviewSize(ClipboardHistoryItem item)
    {
        return item.IsImage ? new Size(400, 400) : new Size(600, 300);
    }

    private static Control? CreatePreviewContent(ClipboardHistoryItem item)
    {
        if (item.IsImage && item.ImageBytes is { } imageBytes)
        {
            return CreateImagePreview(imageBytes);
        }

        if (item.IsText && item.TextContent is { } text)
        {
            return CreateTextPreview(text);
        }

        return null;
    }

    private static Control CreateImagePreview(byte[] imageBytes)
    {
        return new Border
        {
            Width = 400,
            Height = 400,
            Padding = new Avalonia.Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new Image
            {
                Source = new Bitmap(new MemoryStream(imageBytes)),
                Stretch = Stretch.Uniform,
            },
        };
    }

    private static Control CreateTextPreview(string text)
    {
        return new Border
        {
            Width = 600,
            Height = 300,
            Padding = new Avalonia.Thickness(10),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontFamily = FontFamily.Parse("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap,
                },
            },
        };
    }
}
