namespace ClippyPlus.App.Models;

public sealed record ClipboardDiagnosticsSnapshot(
    DateTimeOffset CapturedAt,
    uint SequenceNumber,
    ClipboardWindowInfo? OpenClipboardWindow,
    ClipboardWindowInfo? ClipboardOwner);

