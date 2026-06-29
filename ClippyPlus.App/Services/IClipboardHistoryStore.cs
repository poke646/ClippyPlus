using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public interface IClipboardHistoryStore
{
    IReadOnlyList<ClipboardHistoryItem> GetRecent(int limit);

    IReadOnlyList<ClipboardHistoryItem> SearchText(string query, int limit);

    void Upsert(ClipboardHistoryEntry entry);

    void Delete(long id);

    void DeleteAll();
}
