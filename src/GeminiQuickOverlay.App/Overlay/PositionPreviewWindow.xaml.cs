using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GeminiQuickOverlay.App.Overlay;

/// <summary>
/// Live, draggable stand-in for the answer overlay. It reports the same left/bottom offsets the real
/// overlay uses, so dragging it replaces typing pixel values.
/// </summary>
public partial class PositionPreviewWindow : Window
{
    private int _leftOffset;
    private int _bottomOffset;
    private bool _dragging;

    public PositionPreviewWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            if (!_dragging)
            {
                Reposition();
            }
        };
        LocationChanged += (_, _) =>
        {
            if (_dragging)
            {
                ReportOffsets();
            }
        };
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    /// <summary>Raised with (left, bottom) offsets in the same units as the overlay's config values.</summary>
    public event Action<int, int>? OffsetsChanged;

    public void SetAppearance(System.Windows.Media.Brush foreground, double fontSize, double wrapLength, double opacity)
    {
        MessageText.Foreground = foreground;
        MessageText.FontSize = Math.Clamp(fontSize, 8, 72);
        MessageText.MaxWidth = Math.Clamp(wrapLength, 200, 4096);
        MessageText.LineHeight = Math.Max(12, MessageText.FontSize * 1.35);
        Opacity = Math.Clamp(opacity, 0.1, 1.0);

        if (MessageText.Effect is System.Windows.Media.Effects.DropShadowEffect halo &&
            foreground is SolidColorBrush solid)
        {
            var luminance = (0.299 * solid.Color.R + 0.587 * solid.Color.G + 0.114 * solid.Color.B) / 255;
            halo.Color = luminance < 0.5 ? Colors.White : Colors.Black;
        }
    }

    public void SetOffsets(int leftOffset, int bottomOffset)
    {
        _leftOffset = Math.Max(0, leftOffset);
        _bottomOffset = Math.Max(0, bottomOffset);
        if (!_dragging && IsLoaded)
        {
            Reposition();
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Reposition();
    }

    private void Reposition()
    {
        var workArea = SystemParameters.WorkArea;
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);
        Left = Math.Clamp(
            workArea.Left + _leftOffset,
            workArea.Left,
            Math.Max(workArea.Left, workArea.Right - width));
        Top = Math.Max(workArea.Top, workArea.Bottom - height - _bottomOffset);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _dragging = false;
        }

        ReportOffsets();
    }

    private void ReportOffsets()
    {
        var workArea = SystemParameters.WorkArea;
        _leftOffset = Math.Max(0, (int)Math.Round(Left - workArea.Left));
        _bottomOffset = Math.Max(0, (int)Math.Round(workArea.Bottom - Top - ActualHeight));
        OffsetsChanged?.Invoke(_leftOffset, _bottomOffset);
    }
}
