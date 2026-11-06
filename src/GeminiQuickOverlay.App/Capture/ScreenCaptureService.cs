using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GeminiQuickOverlay.App.Capture;

public static class ScreenCaptureService
{
    private static RegionSelectionForm? _active;

    public static bool IsActive => _active is not null;

    /// <summary>Cancels an in-progress selection (the global Esc hotkey swallows the key before the form sees it).</summary>
    public static void CancelActive() => _active?.Cancel();

    /// <summary>
    /// Freezes the screen, lets the user drag a region and returns it as PNG bytes (downscaled so the
    /// longest side is at most <paramref name="maxSide"/>), or null when cancelled. Must run on the UI thread.
    /// </summary>
    public static async Task<byte[]?> PickRegionPngAsync(int maxSide)
    {
        if (_active is not null)
        {
            return null;
        }

        var bounds = SystemInformation.VirtualScreen;
        using var frozen = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        CopyScreen(frozen, bounds);

        using var form = new RegionSelectionForm(frozen, bounds);
        _active = form;
        try
        {
            form.Show();
            form.Activate();
            var selection = await form.Result.ConfigureAwait(true);
            if (selection is null)
            {
                return null;
            }

            using var crop = frozen.Clone(selection.Value, PixelFormat.Format32bppArgb);
            return EncodePng(crop, maxSide);
        }
        finally
        {
            _active = null;
        }
    }

    private static void CopyScreen(Bitmap target, Rectangle bounds)
    {
        // Graphics.CopyFromScreen rejects SRCCOPY | CAPTUREBLT, which is what includes layered windows
        // (translucent overlays, tooltips), so call BitBlt directly.
        using var g = Graphics.FromImage(target);
        var destination = g.GetHdc();
        var screen = GetDC(IntPtr.Zero);
        try
        {
            if (!BitBlt(destination, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, SrcCopy | CaptureBlt))
            {
                throw new InvalidOperationException("Không chụp được màn hình (BitBlt thất bại).");
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
            g.ReleaseHdc(destination);
        }
    }

    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    private static byte[] EncodePng(Bitmap source, int maxSide)
    {
        var largest = Math.Max(source.Width, source.Height);
        using var stream = new MemoryStream();
        if (maxSide > 0 && largest > maxSide)
        {
            var scale = maxSide / (double)largest;
            using var resized = new Bitmap(
                Math.Max(1, (int)Math.Round(source.Width * scale)),
                Math.Max(1, (int)Math.Round(source.Height * scale)));
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, resized.Width, resized.Height);
            }
            resized.Save(stream, ImageFormat.Png);
        }
        else
        {
            source.Save(stream, ImageFormat.Png);
        }

        return stream.ToArray();
    }
}
