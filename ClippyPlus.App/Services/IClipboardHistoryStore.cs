using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public interface IClipboardHistoryStore
{
    IReadOnlyList<ClipboardHistoryItem> GetRecent(int limit);

    IReadOnlyList<ClipboardHistoryItem> GetRecent(int limit, bool includeImageBytes);

    IReadOnlyList<ClipboardHistoryItem> SearchText(string query, int limit);

    IReadOnlyList<ClipboardSection> GetSections();

    IReadOnlyList<ClipboardHistoryItem> GetSectionItems(long sectionId, int limit);

    IReadOnlyList<ClipboardHistoryItem> GetSectionItems(long sectionId, int limit, bool includeImageBytes);

    byte[]? GetImageBytes(long itemId, long? sectionId, string contentHash);

    IReadOnlyList<ClipboardHistoryItem> SearchSectionText(long sectionId, string query, int limit);

    void ApplySections(IReadOnlyList<ClipboardSectionDraft> sections);

    void Upsert(ClipboardHistoryEntry entry);

    PinClipboardItemResult Pin(long historyItemId, long sectionId);

    bool Unpin(long pinnedItemId);

    void Delete(long id);

    void DeletePinned(long id);

    void DeleteAll();
}
