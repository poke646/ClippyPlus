namespace ClippyPlus.App.Models;

public sealed class ClipboardHistoryItem
{
    public long Id { get; init; }

    public ClipboardHistoryKind Kind { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string Preview { get; init; } = string.Empty;

    public string ContentHash { get; init; } = string.Empty;

    public string? TextContent { get; init; }

    public byte[]? ImageBytes { get; init; }

    public string KindText => Kind == ClipboardHistoryKind.Text ? "Tekst" : "Obraz";

    public bool IsText => Kind == ClipboardHistoryKind.Text;

    public bool IsImage => Kind == ClipboardHistoryKind.Image;

    public string UpdatedAtText => UpdatedAt.ToLocalTime().ToString("HH:mm:ss");
}
