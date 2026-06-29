using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using ClippyPlus.App.Models;
using ClippyPlus.App.Services;

namespace ClippyPlus.App.Views;

public sealed class ClipboardDebugWindow : Window
{
    private const int MaxHistoryItems = 200;

    private readonly IClipboardDiagnosticsService _clipboardDiagnosticsService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly ObservableCollection<ClipboardUsageHistoryItem> _historyItems = [];
    private readonly TextBlock _capturedAtTextBlock = new();
    private readonly TextBlock _sequenceTextBlock = new();
    private readonly TextBlock _openStatusTextBlock = new();
    private readonly TextBlock _openProcessTextBlock = new();
    private readonly TextBlock _openPidTextBlock = new();
    private readonly TextBlock _openTitleTextBlock = new();
    private readonly TextBlock _openClassTextBlock = new();
    private readonly TextBlock _ownerStatusTextBlock = new();
    private readonly TextBlock _ownerProcessTextBlock = new();
    private readonly TextBlock _ownerPidTextBlock = new();
    private readonly TextBlock _ownerTitleTextBlock = new();
    private readonly TextBlock _ownerClassTextBlock = new();
    private readonly CheckBox _autoRefreshCheckBox = new() { Content = "Auto", IsChecked = true };
    private ClipboardUsageHistoryItem? _activeHistoryItem;
    private string? _activeHistoryKey;

    public ClipboardDebugWindow(IClipboardDiagnosticsService clipboardDiagnosticsService)
    {
        _clipboardDiagnosticsService = clipboardDiagnosticsService;
        Title = LocalizationService.T("Debug.Title");
        Width = 860;
        Height = 620;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.CreateWindowIcon();

        var refreshButton = new Button { Content = LocalizationService.T("Debug.Refresh") };
        refreshButton.Click += (_, _) => RefreshSnapshot();
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Avalonia.Thickness(8),
            Children = { refreshButton, _autoRefreshCheckBox },
        };

        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(16),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(12)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(12)),
                new RowDefinition(GridLength.Star),
            },
        };
        grid.Children.Add(new StackPanel
        {
            Children = { _capturedAtTextBlock, _sequenceTextBlock },
        });

        var cards = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(12)),
                new ColumnDefinition(GridLength.Star),
            },
        };
        var openCard = CreateInfoCard(LocalizationService.T("Debug.BlockingNow"), _openStatusTextBlock, _openProcessTextBlock, _openPidTextBlock, _openTitleTextBlock, _openClassTextBlock);
        var ownerCard = CreateInfoCard(LocalizationService.T("Debug.CurrentOwner"), _ownerStatusTextBlock, _ownerProcessTextBlock, _ownerPidTextBlock, _ownerTitleTextBlock, _ownerClassTextBlock);
        Grid.SetColumn(ownerCard, 2);
        cards.Children.Add(openCard);
        cards.Children.Add(ownerCard);
        Grid.SetRow(cards, 2);
        grid.Children.Add(cards);

        var historyGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = _historyItems,
            Columns =
            {
                new DataGridTextColumn { Header = LocalizationService.T("Debug.Start"), Binding = new Avalonia.Data.Binding(nameof(ClipboardUsageHistoryItem.StartedAtText)), Width = new DataGridLength(92) },
                new DataGridTextColumn { Header = LocalizationService.T("Debug.End"), Binding = new Avalonia.Data.Binding(nameof(ClipboardUsageHistoryItem.EndedAtText)), Width = new DataGridLength(92) },
                new DataGridTextColumn { Header = LocalizationService.T("Debug.Duration"), Binding = new Avalonia.Data.Binding(nameof(ClipboardUsageHistoryItem.DurationText)), Width = new DataGridLength(88) },
                new DataGridTextColumn { Header = LocalizationService.T("Debug.Type"), Binding = new Avalonia.Data.Binding(nameof(ClipboardUsageHistoryItem.Kind)), Width = new DataGridLength(92) },
                new DataGridTextColumn { Header = LocalizationService.T("Debug.Process"), Binding = new Avalonia.Data.Binding(nameof(ClipboardUsageHistoryItem.ProcessName)), Width = new DataGridLength(120) },
                new DataGridTextColumn { Header = LocalizationService.T("Debug.PID"), Binding = new Avalonia.Data.Binding(nameof(ClipboardUsageHistoryItem.ProcessId)), Width = new DataGridLength(72) },
                new DataGridTextColumn { Header = LocalizationService.T("Debug.Window"), Binding = new Avalonia.Data.Binding(nameof(ClipboardUsageHistoryItem.WindowTitle)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
                new DataGridTextColumn { Header = LocalizationService.T("Debug.ClassHwnd"), Binding = new Avalonia.Data.Binding(nameof(ClipboardUsageHistoryItem.WindowClass)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
            },
        };
        var historyBox = new GroupBox
        {
            Header = LocalizationService.T("Debug.UsageHistory"),
            Content = historyGrid,
        };
        Grid.SetRow(historyBox, 4);
        grid.Children.Add(historyBox);

        Content = new DockPanel
        {
            Children = { toolbar, grid },
        };
        DockPanel.SetDock(toolbar, Dock.Top);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _refreshTimer.Tick += (_, _) =>
        {
            if (_autoRefreshCheckBox.IsChecked == true)
            {
                RefreshSnapshot();
            }
        };
        Opened += (_, _) =>
        {
            RefreshSnapshot();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private static Control CreateInfoCard(string header, params TextBlock[] values)
    {
        var labels = new[]
        {
            LocalizationService.T("Debug.Status"),
            LocalizationService.T("Debug.Process"),
            LocalizationService.T("Debug.PID"),
            LocalizationService.T("Debug.Window"),
            LocalizationService.T("Debug.Class"),
        };
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(120)),
                new ColumnDefinition(GridLength.Star),
            },
        };
        for (var i = 0; i < values.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock
            {
                Text = labels[i],
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Margin = new Avalonia.Thickness(0, i == 0 ? 0 : 8, 0, 0),
            };
            values[i].TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            values[i].Margin = new Avalonia.Thickness(0, i == 0 ? 0 : 8, 0, 0);
            Grid.SetRow(label, i);
            Grid.SetRow(values[i], i);
            Grid.SetColumn(values[i], 1);
            grid.Children.Add(label);
            grid.Children.Add(values[i]);
        }

        return new GroupBox
        {
            Header = header,
            Padding = new Avalonia.Thickness(8),
            Content = grid,
        };
    }

    private void RefreshSnapshot()
    {
        var snapshot = _clipboardDiagnosticsService.Capture();

        _capturedAtTextBlock.Text = LocalizationService.Format("Debug.LastRead", snapshot.CapturedAt);
        _sequenceTextBlock.Text = LocalizationService.Format("Debug.SequenceNumber", snapshot.SequenceNumber);

        SetOpenClipboardInfo(snapshot.OpenClipboardWindow);
        SetOwnerInfo(snapshot.ClipboardOwner);
        UpdateHistory(snapshot);
    }

    private void SetOpenClipboardInfo(ClipboardWindowInfo? info)
    {
        if (info is null)
        {
            _openStatusTextBlock.Text = LocalizationService.T("Debug.NotOpen");
            _openProcessTextBlock.Text = "-";
            _openPidTextBlock.Text = "-";
            _openTitleTextBlock.Text = "-";
            _openClassTextBlock.Text = "-";
            return;
        }

        _openStatusTextBlock.Text = LocalizationService.T("Debug.ThisAppOpen");
        _openProcessTextBlock.Text = info.ProcessName;
        _openPidTextBlock.Text = FormatPid(info);
        _openTitleTextBlock.Text = info.WindowTitle;
        _openClassTextBlock.Text = FormatWindowClass(info);
    }

    private void SetOwnerInfo(ClipboardWindowInfo? info)
    {
        if (info is null)
        {
            _ownerStatusTextBlock.Text = LocalizationService.T("Debug.NoOwner");
            _ownerProcessTextBlock.Text = "-";
            _ownerPidTextBlock.Text = "-";
            _ownerTitleTextBlock.Text = "-";
            _ownerClassTextBlock.Text = "-";
            return;
        }

        _ownerStatusTextBlock.Text = LocalizationService.T("Debug.ThisWindowOwner");
        _ownerProcessTextBlock.Text = info.ProcessName;
        _ownerPidTextBlock.Text = FormatPid(info);
        _ownerTitleTextBlock.Text = info.WindowTitle;
        _ownerClassTextBlock.Text = FormatWindowClass(info);
    }

    private static string FormatPid(ClipboardWindowInfo info)
    {
        return info.ProcessId?.ToString() ?? LocalizationService.T("Debug.Unknown");
    }

    private static string FormatWindowClass(ClipboardWindowInfo info)
    {
        return $"{info.WindowClass} / HWND 0x{info.Handle.ToInt64():X}";
    }

    private void UpdateHistory(ClipboardDiagnosticsSnapshot snapshot)
    {
        var historySource = GetHistorySource(snapshot);
        if (historySource is not { } source)
        {
            CloseActiveHistoryItem(snapshot.CapturedAt);
            return;
        }

        if (_activeHistoryKey == source.Key)
        {
            _activeHistoryItem?.Refresh();
            return;
        }

        CloseActiveHistoryItem(snapshot.CapturedAt);
        _activeHistoryKey = source.Key;
        _activeHistoryItem = new ClipboardUsageHistoryItem
        {
            StartedAt = snapshot.CapturedAt,
            Kind = source.Kind,
            ProcessName = source.Info.ProcessName,
            ProcessId = FormatPid(source.Info),
            WindowTitle = source.Info.WindowTitle,
            WindowClass = FormatWindowClass(source.Info),
            Handle = $"0x{source.Info.Handle.ToInt64():X}",
        };

        _historyItems.Insert(0, _activeHistoryItem);
        TrimHistory();
    }

    private void CloseActiveHistoryItem(DateTimeOffset endedAt)
    {
        if (_activeHistoryItem is null)
        {
            _activeHistoryKey = null;
            return;
        }

        _activeHistoryItem.EndedAt = endedAt;
        _activeHistoryItem.Refresh();
        _activeHistoryItem = null;
        _activeHistoryKey = null;
    }

    private void TrimHistory()
    {
        while (_historyItems.Count > MaxHistoryItems)
        {
            _historyItems.RemoveAt(_historyItems.Count - 1);
        }
    }

    private static HistorySource? GetHistorySource(ClipboardDiagnosticsSnapshot snapshot)
    {
        if (snapshot.OpenClipboardWindow is { } openClipboardWindow)
        {
            return new HistorySource(
                LocalizationService.T("Debug.Block"),
                openClipboardWindow,
                $"open|{CreateWindowKey(openClipboardWindow)}");
        }

        if (snapshot.ClipboardOwner is { } clipboardOwner)
        {
            return new HistorySource(
                LocalizationService.T("Debug.Content"),
                clipboardOwner,
                $"owner|{snapshot.SequenceNumber}|{CreateWindowKey(clipboardOwner)}");
        }

        return null;
    }

    private static string CreateWindowKey(ClipboardWindowInfo info)
    {
        return $"{info.Handle.ToInt64():X}|{info.ProcessId?.ToString() ?? string.Empty}";
    }

    private sealed record HistorySource(string Kind, ClipboardWindowInfo Info, string Key);
}
