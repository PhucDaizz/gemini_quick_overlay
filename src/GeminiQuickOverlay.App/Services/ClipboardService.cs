using System.Runtime.InteropServices;
using System.Windows;

namespace GeminiQuickOverlay.App.Services;

public sealed record ClipboardContent(bool HasImage, byte[]? ImagePng, string Text)
{
    public static ClipboardContent Empty { get; } = new(false, null, "");
}

public static class ClipboardService
{
    public static ClipboardContent Read(int maxImageSide)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var image = System.Windows.Clipboard.GetImage();
                    if (image is not null)
                    {
                        return new ClipboardContent(true, ImageUtil.EncodePng(image, maxImageSide), "");
                    }
                }

                if (System.Windows.Clipboard.ContainsText())
                {
                    return new ClipboardContent(false, null, System.Windows.Clipboard.GetText().Trim());
                }

                return ClipboardContent.Empty;
            }
            catch (COMException)
            {
                Thread.Sleep(60);
            }
            catch (ExternalException)
            {
                Thread.Sleep(60);
            }
            catch
            {
                return ClipboardContent.Empty;
            }
        }

        return ClipboardContent.Empty;
    }

    public static bool SetText(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(60);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
