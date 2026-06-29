using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public sealed class ClipboardService : IClipboardService
{
    private readonly IClipboard _clipboard;

    public ClipboardService(IClipboard clipboard)
    {
        _clipboard = clipboard;
    }

    public async Task CopyTextAsync(string text)
    {
        await _clipboard.SetTextAsync(text);
        await _clipboard.FlushAsync();
    }

    public async Task CopyImageAsync(CapturedImage image)
    {
        await using var stream = new MemoryStream(image.PngBytes);
        using var bitmap = new Bitmap(stream);
        await _clipboard.SetBitmapAsync(bitmap);
        await _clipboard.FlushAsync();
    }

    public Task CopyImageAsync(byte[] pngBytes)
    {
        return CopyImageAsync(CapturedImage.FromPng(pngBytes));
    }

    public async Task<ClipboardHistoryEntry?> ReadCurrentEntryAsync()
    {
        var text = await _clipboard.TryGetTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return ClipboardHistoryEntryFactory.FromText(text);
        }

        var bitmap = await _clipboard.TryGetBitmapAsync();
        if (bitmap is null)
        {
            return null;
        }

        await using var stream = new MemoryStream();
        bitmap.Save(stream);
        return ClipboardHistoryEntryFactory.FromImageBytes(stream.ToArray());
    }
}
