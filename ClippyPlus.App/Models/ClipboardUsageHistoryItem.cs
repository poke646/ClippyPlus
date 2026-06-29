using ClippyPlus.App.Services;

namespace ClippyPlus.App.Models;

public sealed class ClipboardUsageHistoryItem : System.ComponentModel.INotifyPropertyChanged
{
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; set; }
    public required string Kind { get; init; }
    public required string ProcessName { get; init; }
    public required string ProcessId { get; init; }
    public required string WindowTitle { get; init; }
    public required string WindowClass { get; init; }
    public required string Handle { get; init; }

    public string StartedAtText => StartedAt.ToString("HH:mm:ss.fff");
    public string EndedAtText => EndedAt?.ToString("HH:mm:ss.fff") ?? LocalizationService.T("General.InProgress");
    public string DurationText => FormatDuration((EndedAt ?? DateTimeOffset.Now) - StartedAt);

    public void Refresh()
    {
        OnPropertyChanged(nameof(EndedAtText));
        OnPropertyChanged(nameof(DurationText));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalSeconds < 1
            ? $"{duration.TotalMilliseconds:0} ms"
            : $"{duration.TotalSeconds:0.000} s";
    }
}
