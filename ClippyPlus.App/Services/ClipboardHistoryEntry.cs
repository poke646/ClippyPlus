using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public sealed record ClipboardHistoryEntry(
    ClipboardHistoryKind Kind,
    string Preview,
    string ContentHash,
    string? TextContent,
    byte[]? ImageBytes);
