using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClippyPlus.App.Models;
using SkiaSharp;

namespace ClippyPlus.App.Services;

public sealed class ScreenshotService : IScreenshotService
{
    public async Task<CapturedImage> CaptureMonitorAsync(MonitorSnapshot monitor)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await CaptureMacAsync();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CaptureWindows(monitor);
        }

        throw new PlatformNotSupportedException(LocalizationService.T("Screenshot.PlatformNotSupported"));
    }

    public CapturedImage Crop(CapturedImage source, PixelRect selection)
    {
        if (selection.IsEmpty)
        {
            throw new ArgumentException("Selection cannot be empty.", nameof(selection));
        }

        using var sourceBitmap = SKBitmap.Decode(source.PngBytes)
            ?? throw new InvalidOperationException(LocalizationService.T("Screenshot.ReadFailed"));
        var safeRect = SKRectI.Intersect(
            new SKRectI(0, 0, sourceBitmap.Width, sourceBitmap.Height),
            new SKRectI(selection.X, selection.Y, selection.X + selection.Width, selection.Y + selection.Height));
        if (safeRect.IsEmpty)
        {
            throw new ArgumentException("Selection is outside the image.", nameof(selection));
        }

        using var cropped = new SKBitmap(safeRect.Width, safeRect.Height, sourceBitmap.ColorType, sourceBitmap.AlphaType);
        sourceBitmap.ExtractSubset(cropped, safeRect);
        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return CapturedImage.FromPng(data.ToArray());
    }

    [SupportedOSPlatform("windows")]
    private static CapturedImage CaptureWindows(MonitorSnapshot monitor)
    {
        using var bitmap = new Bitmap(monitor.Bounds.Width, monitor.Bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                monitor.Bounds.Left,
                monitor.Bounds.Top,
                0,
                0,
                new Size(monitor.Bounds.Width, monitor.Bounds.Height),
                CopyPixelOperation.SourceCopy);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return CapturedImage.FromPng(stream.ToArray());
    }

    private static async Task<CapturedImage> CaptureMacAsync()
    {
        var file = Path.Combine(Path.GetTempPath(), $"clippyplus-{Guid.NewGuid():N}.png");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/screencapture",
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("-x");
            startInfo.ArgumentList.Add(file);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(LocalizationService.T("Screenshot.StartFailed"));
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !File.Exists(file))
            {
                throw new InvalidOperationException(LocalizationService.T("Screenshot.CaptureFailedMac"));
            }

            return CapturedImage.FromPng(await File.ReadAllBytesAsync(file));
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
