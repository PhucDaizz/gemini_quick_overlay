using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GeminiQuickOverlay.App.Services;

public static class ImageUtil
{
    public static byte[] EncodePng(BitmapSource source, int maxSide)
    {
        BitmapSource output = source;
        var largestSide = Math.Max(source.PixelWidth, source.PixelHeight);
        if (maxSide > 0 && largestSide > maxSide)
        {
            var scale = maxSide / (double)largestSide;
            var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            resized.Freeze();
            output = resized;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
