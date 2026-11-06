using System.Windows;

namespace GeminiQuickOverlay.App.Overlay;

internal static class ScreenArea
{
    /// <summary>
    /// Full primary screen, taskbar included, so the overlay can sit on top of the taskbar
    /// (offsets are measured from the physical screen edges, as in the Python version).
    /// </summary>
    public static Rect Bounds => new(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
}
