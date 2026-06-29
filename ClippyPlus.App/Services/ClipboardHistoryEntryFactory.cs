using System.Security.Cryptography;
using System.Text;
using ClippyPlus.App.Models;

namespace ClippyPlus.App.Services;

public static class ClipboardHistoryEntryFactory
{
    private const int PreviewLength = 180;

    public static ClipboardHistoryEntry? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new ClipboardHistoryEntry(
            ClipboardHistoryKind.Text,
            CreateTextPreview(text),
            CreateHash("text", Encoding.UTF8.GetBytes(text)),
            text,
            null);
    }

    public static ClipboardHistoryEntry? FromImageBytes(byte[]? imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return null;
        }

        return new ClipboardHistoryEntry(
            ClipboardHistoryKind.Image,
            "Obraz",
            CreateHash("image", imageBytes),
            null,
            imageBytes);
    }

    public static string CreateTextPreview(string text)
    {
        var normalized = string.Join(
            " ",
            text.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= PreviewLength)
        {
            return normalized;
        }

        return normalized[..PreviewLength] + "...";
    }

    private static string CreateHash(string prefix, byte[] bytes)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var combined = new byte[prefixBytes.Length + 1 + bytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, combined, 0, prefixBytes.Length);
        combined[prefixBytes.Length] = 0;
        Buffer.BlockCopy(bytes, 0, combined, prefixBytes.Length + 1, bytes.Length);

        return Convert.ToHexString(SHA256.HashData(combined));
    }
}
