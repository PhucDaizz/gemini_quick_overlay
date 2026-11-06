using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GeminiQuickOverlay.Core.Config;
using GeminiQuickOverlay.Core.Models;

namespace GeminiQuickOverlay.App.Overlay;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private readonly ConfigService _config;
    private DispatcherTimer? _hideTimer;

    public OverlayWindow(ConfigService config)
    {
        _config = config;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        ApplyConfig();
    }

    public void ApplyConfig()
    {
        var config = _config.Current;
        var foreground = ParseBrush(config.TextColor);
        MessageText.Foreground = foreground;
        if (MessageText.Effect is System.Windows.Media.Effects.DropShadowEffect halo &&
            foreground is SolidColorBrush solid)
        {
            // Dark text gets a light halo and light text a dark one, so it never blends into its own glow.
            var luminance = (0.299 * solid.Color.R + 0.587 * solid.Color.G + 0.114 * solid.Color.B) / 255;
            halo.Color = luminance < 0.5 ? Colors.White : Colors.Black;
        }
        MessageText.FontSize = Math.Clamp(config.FontSize, 8, 72);
        MessageText.MaxWidth = Math.Clamp(config.WrapLength, 200, 4096);
        MessageText.LineHeight = Math.Max(12, MessageText.FontSize * 1.35);
        Opacity = Math.Clamp(config.Opacity, 0.1, 1.0);
    }

    public void ShowMessage(string text, double durationSeconds)
    {
        StopHideTimer();
        ApplyConfig();
        MessageText.Text = text;
        Visibility = Visibility.Visible;
        UpdateLayout();
        PositionWindow();
        if (durationSeconds <= 0)
        {
            return;
        }

        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(durationSeconds),
        };
        _hideTimer.Tick += HideTimerOnTick;
        _hideTimer.Start();
    }

    public void HideNow()
    {
        StopHideTimer();
        Visibility = Visibility.Hidden;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopHideTimer();
        base.OnClosed(e);
    }

    private void PositionWindow()
    {
        var config = _config.Current;
        var workArea = ScreenArea.Bounds;
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);
        var left = Math.Clamp(
            workArea.Left + config.PositionLeftOffset,
            workArea.Left,
            Math.Max(workArea.Left, workArea.Right - width));
        var top = workArea.Bottom - height - config.PositionBottomOffset;
        Left = left;
        Top = Math.Max(workArea.Top, top);
    }

    private void HideTimerOnTick(object? sender, EventArgs e) => HideNow();

    private void StopHideTimer()
    {
        if (_hideTimer is null)
        {
            return;
        }
        _hideTimer.Stop();
        _hideTimer.Tick -= HideTimerOnTick;
        _hideTimer = null;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        style |= WsExTransparent | WsExToolWindow | WsExNoActivate;
        SetWindowLong(handle, GwlExStyle, style);
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
    }

    private static System.Windows.Media.Brush ParseBrush(string value)
    {
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(value) is System.Windows.Media.Color color)
            {
                return new SolidColorBrush(color);
            }
        }
        catch
        {
        }

        return System.Windows.Media.Brushes.Black;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
