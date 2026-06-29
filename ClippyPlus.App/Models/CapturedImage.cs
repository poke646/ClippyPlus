using System.Security.Cryptography;
using ClippyPlus.App.Services;
using SkiaSharp;

namespace ClippyPlus.App.Models;

public sealed record CapturedImage(byte[] PngBytes, int Width, int Height)
{
    public static CapturedImage FromPng(byte[] pngBytes)
    {
        using var bitmap = SKBitmap.Decode(pngBytes)
            ?? throw new InvalidOperationException(LocalizationService.T("Image.ReadPngFailed"));
        return new CapturedImage(pngBytes, bitmap.Width, bitmap.Height);
    }

    public string ContentHash => Convert.ToHexString(SHA256.HashData(PngBytes));
}
