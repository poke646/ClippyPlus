using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClippyPlus.App.Models;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

public sealed class ClipboardHistoryWindow : Window
{
    private const double PreferredWidth = 1480;
    private const double PreferredHeight = 950;
    private const double PreviewOffset = 14;
    private const int SearchActivationGuardMs = 300;
    private static readonly BoxShadows SearchShadow = BoxShadows.Parse("0 4 25 3 #A0000000");

    private readonly IClipboardHistoryStore _historyStore;
    private readonly IClipboardService _clipboardService;
    private readonly IClipboardHistoryListener _clipboardHistoryListener;
    private readonly IWindowPrivacyService _windowPrivacyService;
    private readonly RadialClipboardPanel _radialPanel;
    private readonly TextBox _search;
    private readonly Grid _tabSelector;
    private readonly Button _previousTab;
    private readonly Button _nextTab;
    private readonly Border _status;
    private readonly TextBlock _statusText;
    private readonly Canvas _previewCanvas;
    private readonly ContentControl _previewPopup;
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _searchTimer;
    private readonly IBrush _surface = RadialClipboardVisuals.Brush("ClippySurfaceBrush", new Color(255, 38, 38, 38));
    private readonly IBrush _border = RadialClipboardVisuals.Brush("ClippyBorderBrush", new Color(255, 69, 69, 69));
    private readonly IBrush _text = RadialClipboardVisuals.Brush("ClippyTextBrush", new Color(255, 242, 242, 242));
    private readonly IBrush _muted = RadialClipboardVisuals.Brush("ClippyMutedTextBrush", new Color(255, 176, 176, 176));
    private readonly IBrush _accent = RadialClipboardVisuals.Brush("ClippySelectionBorderBrush", new Color(255, 106, 158, 232));
    private IReadOnlyList<Group> _groups = [];
    private List<Group> _visibleGroups = [];
    private IReadOnlyList<ClipboardSection> _sections = [];
    private Group? _activeGroup;
    private Group? _selectedSection;
    private CancellationTokenSource? _load;
    private CancellationTokenSource? _groupAnimation;
    private ClipboardHistoryItem? _previewItem;
    private Group? _previewGroup;
    private Bitmap? _previewBitmap;
    private Point _previewPosition;
    private DateTime _ignoreSearchActivationUntil;
    private string _appliedQuery = string.Empty;
    private int _openVersion;
    private bool _hideSharingEnabled;
    private bool _dismissing;
    private bool _showingMessage;
    private bool _copying;
    private bool _loadFailed;

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

        Width = PreferredWidth;
        Height = PreferredHeight;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        TransparencyBackgroundFallback = Brushes.Transparent;
        Background = Brushes.Transparent;

        _search = new TextBox
        {
            MinWidth = 0,
            MinHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(7, 6, 12, 6),
            Background = _surface,
            BorderBrush = _border,
            Foreground = _text,
            PlaceholderForeground = _muted,
            CaretBrush = _text,
            SelectionBrush = RadialClipboardVisuals.Brush("ClippySelectionBrush", Color.FromRgb(45, 73, 112)),
            InnerLeftContent = new Border
            {
                Margin = new Thickness(11, 0, 2, 0),
                Child = RadialClipboardVisuals.CreateIcon(RadialClipboardIcon.Search, 17, _muted),
                IsHitTestVisible = false,
            },
        };
        _search.Resources["TextControlBackgroundFocused"] = _surface;
        _search.Resources["TextControlBackgroundPointerOver"] = _surface;
        _search.Resources["TextControlBorderBrushFocused"] = _accent;
        _search.Resources["TextControlBorderBrushPointerOver"] = _accent;
        _search.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        var searchFrame = new Border
        {
            MaxWidth = 300,
            Margin = new Thickness(12, 0, 12, 24),
            Background = _surface,
            CornerRadius = _search.CornerRadius,
            BoxShadow = SearchShadow,
            Child = _search,
        };
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplySearch();
        };
        _search.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };

        _previousTab = CreateTabButton(RadialClipboardIcon.Previous, -1);
        _nextTab = CreateTabButton(RadialClipboardIcon.Next, 1);
        _tabSelector = new Grid
        {
            IsVisible = false,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children = { _previousTab, _nextTab },
        };
        Grid.SetColumn(_nextTab, 2);

        _statusText = new TextBlock
        {
            Foreground = _muted,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
        };
        _status = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = _surface,
            BorderBrush = _border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(13, 7),
            Margin = new Thickness(12, 0, 12, 14),
            Child = _statusText,
        };

        _radialPanel = new RadialClipboardPanel(_accent, _tabSelector) { Margin = new Thickness(12, 0) };

        _previewPopup = new ContentControl { IsVisible = false };
        _previewCanvas = new Canvas
        {
            IsHitTestVisible = false,
            Children = { _previewPopup },
        };
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await ShowPreviewAsync();
        };

        var body = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            Children = { _radialPanel, searchFrame, _status },
        };
        Grid.SetRow(searchFrame, 1);
        Grid.SetRow(_status, 2);
        Content = new Grid { Children = { body, _previewCanvas } };

        UpdateLabels();
        Opened += (_, _) =>
        {
            CenterOnScreen();
            ResetForOpen();
            ApplyWindowDisplayAffinity();
        };
        Deactivated += (_, _) =>
        {
            if (!_showingMessage)
            {
                Dismiss();
            }
        };
        Closed += (_, _) => ClearSession();
        AddHandler(KeyDownEvent, OnEscapeKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
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

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        Width = Math.Min(PreferredWidth, area.Width / scale);
        Height = Math.Min(PreferredHeight, area.Height / scale);
        Position = new PixelPoint(
            area.X + (area.Width - (int)Math.Round(Width * scale)) / 2,
            area.Y + (area.Height - (int)Math.Round(Height * scale)) / 2);
    }

    public void ResetForOpen()
    {
        ClearSession();
        _ignoreSearchActivationUntil = DateTime.UtcNow.AddMilliseconds(SearchActivationGuardMs);
        _search.Text = string.Empty;
        _appliedQuery = string.Empty;
        UpdateLabels();
        StartLoading();
        FocusSearch();
    }

    public void ReloadSections()
    {
        if (IsVisible)
        {
            StartLoading();
        }
    }

    private async void StartLoading()
    {
        _load?.Cancel();
        using var load = new CancellationTokenSource();
        _load = load;
        _loadFailed = false;
        UpdateStatus();
        try
        {
            var snapshot = await Task.Run(() => LoadSnapshot(load.Token), load.Token);
            if (load.IsCancellationRequested)
            {
                return;
            }

            _sections = snapshot.Sections;
            _groups = snapshot.Groups.Select(data =>
            {
                var group = _groups.FirstOrDefault(group => group.SectionId == data.SectionId
                    && group.Icon == data.Icon && group.Label == data.Label)
                    ?? new Group(data.SectionId, data.Icon, data.Label);
                group.Items = data.Items;
                return group;
            }).ToArray();
            var firstLoad = _activeGroup is null;
            if (firstLoad && snapshot.LatestKind == ClipboardHistoryKind.Image)
            {
                _activeGroup = _groups[1];
            }

            ApplySearch(animate: firstLoad);
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!load.IsCancellationRequested)
            {
                Trace.TraceError("Unable to load clipboard history: {0}", error);
                _loadFailed = true;
            }
        }
        finally
        {
            if (_load == load)
            {
                _load = null;
                UpdateStatus();
            }
        }
    }

    private (IReadOnlyList<ClipboardSection> Sections, List<GroupData> Groups, ClipboardHistoryKind? LatestKind)
        LoadSnapshot(CancellationToken cancellationToken)
    {
        var recent = _historyStore.GetRecent(ClipboardHistoryStore.MaxHistoryItems, includeImageBytes: false);
        cancellationToken.ThrowIfCancellationRequested();
        var sections = _historyStore.GetSections();
        var groups = new List<GroupData>
        {
            new(null, RadialClipboardIcon.Text, LocalizationService.T("History.Texts"),
                recent.Where(item => item.IsText).ToArray()),
            new(null, RadialClipboardIcon.Image, LocalizationService.T("History.Images"),
                recent.Where(item => item.IsImage).ToArray()),
        };
        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            groups.Add(new GroupData(section.Id, RadialClipboardIcon.Section, section.Name,
                section.ItemCount == 0
                    ? []
                    : _historyStore.GetSectionItems(section.Id, section.ItemLimit, includeImageBytes: false)));
        }

        return (sections, groups, recent.FirstOrDefault()?.Kind);
    }

    private void ApplySearch(bool animate = false)
    {
        HidePreview();
        var query = _search.Text?.Trim() ?? string.Empty;
        var changed = !string.Equals(_appliedQuery, query, StringComparison.Ordinal);
        _appliedQuery = query;
        foreach (var group in _groups)
        {
            group.FilteredItems = query.Length == 0
                ? group.Items
                : group.Items.Where(item => item.TextContent?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true)
                    .ToArray();
            if (changed)
            {
                group.NeedsSelectionReset = true;
            }
        }

        var customGroups = _groups.Where(group => group.SectionId is not null).ToArray();
        if (_selectedSection is null || !customGroups.Contains(_selectedSection))
        {
            _selectedSection = customGroups.FirstOrDefault(group => group.Label.Equals("ID", StringComparison.OrdinalIgnoreCase))
                ?? customGroups.FirstOrDefault();
        }

        RenderGroups(preferMatchingGroup: changed, animate: animate);
        UpdateStatus();
    }

    private void ChangeSection(int direction)
    {
        ApplyPendingSearch();
        var customGroups = _groups.Where(group => group.SectionId is not null).ToArray();
        if (customGroups.Length < 2)
        {
            return;
        }

        var index = Array.IndexOf(customGroups, _selectedSection);
        var next = (index + direction + customGroups.Length) % customGroups.Length;
        SetActiveGroup(customGroups[next]);
        _selectedSection = customGroups[next];
        RenderGroups();
        UpdateStatus();
        FocusSearch();
    }

    private void RenderGroups(bool preferMatchingGroup = false, bool animate = false)
    {
        var visible = _groups.Take(2).ToList();
        if (_selectedSection is { } selected)
        {
            visible.Add(selected);
        }

        if (_activeGroup is null || !visible.Contains(_activeGroup)
            || preferMatchingGroup && _activeGroup.FilteredItems.Count == 0)
        {
            var active = visible.FirstOrDefault(group => group.FilteredItems.Count > 0) ?? visible.FirstOrDefault();
            SetActiveGroup(active);
        }

        _visibleGroups = visible;
        var children = _radialPanel.Children;
        RadialClipboardStack? newSection = null;
        for (var index = 0; index < visible.Count; index++)
        {
            var group = visible[index];
            var direction = index == 0 ? 1 : index == 1 ? -1 : 0;
            group.Stack ??= CreateStack(group, direction);
            if (group.NeedsSelectionReset || group.RenderedItems != group.FilteredItems)
            {
                group.Stack.SetItems(group.FilteredItems, reset: group.NeedsSelectionReset);
                group.RenderedItems = group.FilteredItems;
                group.NeedsSelectionReset = false;
            }

            group.Stack.SetHeaderToolTip(group.FilteredItems.Count == 0
                ? LocalizationService.T(group.SectionId is null ? "History.Empty" : "History.SectionEmpty")
                : null);
            group.Stack.SetSelected(group == _activeGroup);
            if (index < children.Count && children[index] == group.Stack)
            {
                continue;
            }

            if (index < children.Count)
            {
                children.RemoveAt(index);
            }
            children.Insert(index, group.Stack);
            if (group.SectionId is not null)
            {
                newSection = group.Stack;
            }
        }

        while (children.Count > visible.Count)
        {
            children.RemoveAt(children.Count - 1);
        }

        _tabSelector.IsVisible = _sections.Count > 1;
        if (animate && visible.Count > 0)
        {
            AnimateAppear(_radialPanel);
        }
        else if (newSection is not null)
        {
            AnimateAppear(newSection);
        }
    }

    private RadialClipboardStack CreateStack(Group group, int direction)
    {
        var stack = new RadialClipboardStack(group.Label, group.Icon, item => LoadImage(group, item), direction);
        stack.SelectionChanged += _ =>
        {
            SetActiveGroup(group);
            HidePreview();
        };
        stack.HeaderActivated += _ =>
        {
            SetActiveGroup(group);
            FocusSearch();
        };
        stack.Activated += async _ => await CopyItemAsync(group);
        stack.PreviewEntered += (item, e) => UpdatePreviewPointer(group, item, e.GetPosition(_previewCanvas));
        stack.PreviewMoved += (item, e) => UpdatePreviewPointer(group, item, e.GetPosition(_previewCanvas));
        stack.PreviewExited += HidePreview;
        stack.SetContextMenu(CreateContextMenu(group));
        return stack;
    }

    private void SetActiveGroup(Group? group)
    {
        if (_activeGroup == group)
        {
            return;
        }

        _activeGroup?.Stack?.SetSelected(false);
        _activeGroup = group;
        group?.Stack?.SetSelected(true);
    }

    private void MoveToAdjacentGroup(int direction)
    {
        if (_visibleGroups.Count == 0)
        {
            FocusSearch();
            return;
        }

        var current = _activeGroup is { } active ? _visibleGroups.IndexOf(active) : 0;
        var next = (current + direction + _visibleGroups.Count) % _visibleGroups.Count;
        SetActiveGroup(_visibleGroups[next]);
        FocusSearch();
    }

    private void OnEscapeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseOnEscape();
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var editingSearch = e.Source == _search || (e.Source is Visual source && _search.IsVisualAncestorOf(source));
        switch (e.Key)
        {
            case Key.Tab:
                e.Handled = true;
                var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    ChangeSection(direction);
                }
                else
                {
                    ApplyPendingSearch();
                    MoveToAdjacentGroup(direction);
                }
                break;
            case Key.Down:
            case Key.Up:
                e.Handled = true;
                ApplyPendingSearch();
                _activeGroup?.Stack?.MoveSelection(e.Key == Key.Down ? 1 : -1);
                break;
            case Key.Enter or Key.Return:
                if (e.Source is Button)
                {
                    break;
                }

                e.Handled = true;
                ApplyPendingSearch();
                if (_activeGroup is { } group)
                {
                    await CopyItemAsync(group);
                }
                break;
            case Key.Delete when !editingSearch && _activeGroup is { } selectedGroup:
                e.Handled = true;
                DeleteSelected(selectedGroup);
                break;
            case Key.F when !editingSearch && e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                if (DateTime.UtcNow >= _ignoreSearchActivationUntil)
                {
                    _search.Focus();
                    _search.SelectAll();
                }
                break;
        }
    }

    private void ApplyPendingSearch()
    {
        if (_searchTimer.IsEnabled)
        {
            _searchTimer.Stop();
            ApplySearch();
        }
    }

    private void FocusSearch()
    {
        var version = _openVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible && version == _openVersion)
            {
                _search.Focus();
                _search.CaretIndex = _search.Text?.Length ?? 0;
            }
        });
    }

    private Button CreateTabButton(RadialClipboardIcon icon, int direction)
    {
        var button = new Button
        {
            Content = RadialClipboardVisuals.CreateIcon(icon, 18, _text),
            Width = 36,
            Height = 36,
            MinWidth = 0,
            MinHeight = 0,
            Background = _surface,
            BorderBrush = _border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => ChangeSection(direction);
        return button;
    }

    private void UpdateLabels()
    {
        Title = LocalizationService.T("History.Title");
        _search.PlaceholderText = LocalizationService.T("History.Search");
        AutomationProperties.SetName(_search, _search.PlaceholderText);
        SetButtonLabel(_previousTab, LocalizationService.T("History.PreviousTab"));
        SetButtonLabel(_nextTab, LocalizationService.T("History.NextTab"));
    }

    private static void SetButtonLabel(Button button, string label)
    {
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
    }

    private void UpdateStatus()
    {
        string? key = null;
        if (_loadFailed)
        {
            key = "History.LoadFailed";
        }
        else if (_load is null && _groups.Count > 0)
        {
            if (_appliedQuery.Length > 0 && _visibleGroups.All(group => group.FilteredItems.Count == 0))
            {
                key = _groups.Any(group => group.FilteredItems.Count > 0)
                    ? "History.ResultsInOtherTabs"
                    : "History.NoResults";
            }
            else if (_groups.All(group => group.Items.Count == 0))
            {
                key = "History.Empty";
            }
        }

        _status.IsVisible = key is not null;
        _statusText.Text = key is null ? null : LocalizationService.T(key);
    }

    private async void AnimateAppear(Control target)
    {
        _groupAnimation?.Cancel();
        using var animation = new CancellationTokenSource();
        _groupAnimation = animation;
        try
        {
            await new Animation
            {
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(OpacityProperty, 0d) },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(OpacityProperty, 1d) },
                    },
                },
            }.RunAsync(target, animation.Token);
        }
        catch (OperationCanceledException) when (animation.IsCancellationRequested)
        {
        }
        finally
        {
            if (_groupAnimation == animation)
            {
                _groupAnimation = null;
            }
        }
    }

    internal void CloseOnEscape()
    {
        if (_dismissing || !IsVisible)
        {
            return;
        }

        _dismissing = true;
        try
        {
            Close();
        }
        finally
        {
            _dismissing = false;
        }
    }

    private void Dismiss()
    {
        if (_dismissing || !IsVisible)
        {
            return;
        }

        _dismissing = true;
        try
        {
            ClearSession();
            Hide();
        }
        finally
        {
            _dismissing = false;
        }
    }

    private void ClearSession()
    {
        _openVersion++;
        _load?.Cancel();
        _load = null;
        _groupAnimation?.Cancel();
        _searchTimer.Stop();
        HidePreview();
        _radialPanel.Children.Clear();
        _groups = [];
        _visibleGroups = [];
        _sections = [];
        _activeGroup = null;
        _selectedSection = null;
        _tabSelector.IsVisible = false;
        _status.IsVisible = false;
    }

    private ContextMenu CreateContextMenu(Group group)
    {
        var open = new MenuItem { Header = LocalizationService.T("History.Open") };
        open.Click += (_, _) =>
        {
            if (group.Stack?.SelectedItem?.TextContent is { } text && ExternalContentService.TryOpen(text))
            {
                Dismiss();
            }
        };
        var edit = new MenuItem { Header = LocalizationService.T("History.Edit") };
        edit.Click += async (_, _) => await OpenImageEditorAsync(group);
        var pin = new MenuItem { Header = LocalizationService.T("History.Pin") };
        var unpin = new MenuItem { Header = LocalizationService.T("History.Unpin") };
        unpin.Click += (_, _) =>
        {
            if (group.Stack?.SelectedItem is { } item && _historyStore.Unpin(item.Id))
            {
                ReloadSections();
            }
        };
        var delete = new MenuItem { Header = LocalizationService.T("History.Delete") };
        delete.Click += (_, _) => DeleteSelected(group);
        var deleteAll = new MenuItem { Header = LocalizationService.T("History.DeleteAll") };
        deleteAll.Click += (_, _) =>
        {
            _historyStore.DeleteAll();
            ReloadSections();
        };
        var menu = new ContextMenu { ItemsSource = new[] { open, edit, pin, unpin, delete, deleteAll } };
        menu.AddHandler(KeyDownEvent, OnEscapeKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        menu.Opening += (_, _) =>
        {
            HidePreview();
            SetActiveGroup(group);
            var item = group.Stack?.SelectedItem;
            var isHistory = group.SectionId is null;
            open.IsVisible = item?.TextContent is { } text && ExternalContentService.CanOpen(text);
            edit.IsVisible = item?.IsImage == true;
            pin.IsVisible = isHistory;
            unpin.IsVisible = !isHistory;
            deleteAll.IsVisible = isHistory;

            pin.Header = LocalizationService.T(_sections.Count == 0 ? "History.ConfigureSections" : "History.Pin");
            pin.IsEnabled = _sections.Count > 0;
            pin.ItemsSource = _sections.Select(section =>
            {
                var target = new MenuItem { Header = section.Name };
                target.Click += async (_, _) => await PinAsync(group, section.Id);
                return target;
            }).ToArray();
        };
        return menu;
    }

    private void DeleteSelected(Group group)
    {
        if (group.Stack?.SelectedItem is not { } item)
        {
            return;
        }

        if (group.SectionId is null)
        {
            _historyStore.Delete(item.Id);
        }
        else
        {
            _historyStore.DeletePinned(item.Id);
        }

        ReloadSections();
    }

    private async Task PinAsync(Group group, long sectionId)
    {
        if (group.Stack?.SelectedItem is not { } item)
        {
            return;
        }

        var result = _historyStore.Pin(item.Id, sectionId);
        if (result != PinClipboardItemResult.Success)
        {
            await ShowMessageAsync(result == PinClipboardItemResult.SectionFull ? "History.SectionFull" : "History.PinFailed");
        }

        ReloadSections();
    }

    private byte[]? LoadImage(Group group, ClipboardHistoryItem item)
    {
        return item.ImageBytes ?? _historyStore.GetImageBytes(item.Id, group.SectionId, item.ContentHash);
    }

    private async Task CopyItemAsync(Group group)
    {
        if (_copying || group.Stack?.SelectedItem is not { } item)
        {
            return;
        }

        var version = _openVersion;
        _copying = true;
        HidePreview();
        try
        {
            var image = item.IsImage ? await Task.Run(() => LoadImage(group, item)) : null;
            if (version != _openVersion)
            {
                return;
            }

            using (_clipboardHistoryListener.IgnoreClipboardChanges())
            {
                if (image is not null)
                {
                    await _clipboardService.CopyImageAsync(image);
                }
                else if (item.TextContent is { } text)
                {
                    await _clipboardService.CopyTextAsync(text);
                }
                else
                {
                    await ShowMessageAsync("History.CopyFailed");
                    return;
                }
            }

            if (version == _openVersion)
            {
                Dismiss();
            }
        }
        catch (Exception error)
        {
            Trace.TraceError("Unable to copy clipboard history item: {0}", error);
            if (version == _openVersion)
            {
                await ShowMessageAsync("History.CopyFailed");
            }
        }
        finally
        {
            _copying = false;
        }
    }

    private async Task OpenImageEditorAsync(Group group)
    {
        if (group.Stack?.SelectedItem is not { IsImage: true } item)
        {
            return;
        }

        HidePreview();
        var version = _openVersion;
        try
        {
            var bytes = await Task.Run(() => LoadImage(group, item));
            if (version != _openVersion)
            {
                return;
            }

            if (bytes is null)
            {
                await ShowMessageAsync("History.LoadFailed");
                return;
            }

            var editor = new EditorWindow(CapturedImage.FromPng(bytes), _clipboardService);
            Dismiss();
            editor.Show();
            editor.Activate();
        }
        catch (Exception error)
        {
            Trace.TraceError("Unable to open clipboard history image: {0}", error);
            if (version == _openVersion)
            {
                await ShowMessageAsync("History.LoadFailed");
            }
        }
    }

    private async Task ShowMessageAsync(string message)
    {
        _showingMessage = true;
        try
        {
            await MessageWindow.ShowAsync("ClippyPlus", LocalizationService.T(message), this, CloseOnEscape);
        }
        finally
        {
            _showingMessage = false;
        }
    }

    private void UpdatePreviewPointer(Group group, ClipboardHistoryItem item, Point position)
    {
        if (group != _previewGroup || item != _previewItem)
        {
            HidePreview();
            _previewGroup = group;
            _previewItem = item;
            _previewTimer.Start();
        }

        _previewPosition = position;
        if (_previewPopup.IsVisible)
        {
            PositionPreview();
        }
    }

    private async Task ShowPreviewAsync()
    {
        if (_previewGroup is not { } group || _previewItem is not { } item || !IsVisible)
        {
            return;
        }

        Control content;
        if (item.IsImage)
        {
            Bitmap? bitmap = null;
            try
            {
                bitmap = await Task.Run(() =>
                {
                    var bytes = LoadImage(group, item);
                    return bytes is null ? null : RadialClipboardVisuals.DecodeImage(bytes, 640);
                });
            }
            catch (Exception error)
            {
                Trace.TraceWarning("Unable to preview clipboard history image: {0}", error);
            }

            if (bitmap is null)
            {
                return;
            }

            if (group != _previewGroup || item != _previewItem || !IsVisible)
            {
                bitmap.Dispose();
                return;
            }

            _previewBitmap?.Dispose();
            _previewBitmap = bitmap;
            content = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        }
        else if (item.TextContent is { } text)
        {
            var excerpt = text.Length > 4096 ? text[..4096] + "…" : text;
            content = new TextBlock
            {
                Text = excerpt,
                Foreground = _text,
                FontFamily = FontFamily.Parse("monospace"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 15,
            };
        }
        else
        {
            return;
        }

        var timestamp = new TextBlock
        {
            Text = item.UpdatedAtText,
            Foreground = _muted,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };
        DockPanel.SetDock(timestamp, Dock.Bottom);
        _previewPopup.Content = new Border
        {
            Width = Math.Max(1, Math.Min(item.IsImage ? 400 : 520, _previewCanvas.Bounds.Width - 2 * PreviewOffset)),
            MaxHeight = Math.Max(1, Math.Min(360, _previewCanvas.Bounds.Height - 2 * PreviewOffset)),
            Padding = new Thickness(item.IsImage ? 7 : 12),
            Background = _surface,
            BorderBrush = _border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = new DockPanel { Children = { timestamp, content } },
        };
        _previewPopup.IsVisible = true;
        _previewPopup.Measure(_previewCanvas.Bounds.Size);
        PositionPreview();
    }

    private void PositionPreview()
    {
        var size = _previewPopup.DesiredSize;
        var maxLeft = Math.Max(0, _previewCanvas.Bounds.Width - size.Width);
        var maxTop = Math.Max(0, _previewCanvas.Bounds.Height - size.Height);
        var left = _previewPosition.X + PreviewOffset;
        var top = _previewPosition.Y + PreviewOffset;
        Canvas.SetLeft(_previewPopup, Math.Clamp(left <= maxLeft ? left : _previewPosition.X - size.Width - PreviewOffset, 0, maxLeft));
        Canvas.SetTop(_previewPopup, Math.Clamp(top <= maxTop ? top : _previewPosition.Y - size.Height - PreviewOffset, 0, maxTop));
    }

    private void HidePreview()
    {
        _previewTimer.Stop();
        _previewGroup = null;
        _previewItem = null;
        _previewPopup.IsVisible = false;
        _previewPopup.Content = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
    }

    private void ApplyWindowDisplayAffinity()
    {
        _windowPrivacyService.SetExcludedFromCapture(this, _hideSharingEnabled);
    }

    private sealed class Group(long? sectionId, RadialClipboardIcon icon, string label)
    {
        public long? SectionId { get; } = sectionId;
        public RadialClipboardIcon Icon { get; } = icon;
        public string Label { get; } = label;
        public IReadOnlyList<ClipboardHistoryItem> Items { get; set; } = [];
        public IReadOnlyList<ClipboardHistoryItem> FilteredItems { get; set; } = [];
        public IReadOnlyList<ClipboardHistoryItem>? RenderedItems { get; set; }
        public RadialClipboardStack? Stack { get; set; }
        public bool NeedsSelectionReset { get; set; }
    }

    private sealed record GroupData(
        long? SectionId, RadialClipboardIcon Icon, string Label, IReadOnlyList<ClipboardHistoryItem> Items);
}
