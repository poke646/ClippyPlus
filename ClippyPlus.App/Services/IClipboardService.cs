using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public interface IClipboardService
{
    Task CopyTextAsync(string text);

    Task CopyImageAsync(CapturedImage image);

    Task CopyImageAsync(byte[] pngBytes);

    Task<ClipboardHistoryEntry?> ReadCurrentEntryAsync();
}
