namespace ClippyPlus.App.Models;

public sealed record ClipboardSection(
    long Id,
    string Name,
    int ItemLimit,
    int ItemCount);

public sealed record ClipboardSectionDraft(
    long? Id,
    string Name,
    int ItemLimit);

public enum PinClipboardItemResult
{
    Success,
    HistoryItemNotFound,
    SectionNotFound,
    SectionFull,
}
