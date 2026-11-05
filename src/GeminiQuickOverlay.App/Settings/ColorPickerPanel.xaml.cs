using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GeminiQuickOverlay.App.Settings;

/// <summary>Drag-to-pick colour panel: saturation/value square, hue bar, hex box and presets.</summary>
public partial class ColorPickerPanel : System.Windows.Controls.UserControl
{
    private static readonly string[] PresetColors =
    {
        "#000000", "#FFFFFF", "#FF3B30", "#FF9500", "#FFCC00", "#34C759",
        "#00C7BE", "#007AFF", "#5856D6", "#AF52DE", "#FF2D55", "#8E8E93",
    };

    private double _hue;
    private double _saturation;
    private double _value = 1;
    private bool _updating;

    public event Action<System.Windows.Media.Color>? ColorChanged;

    public ColorPickerPanel()
    {
        InitializeComponent();
        BuildPresets();
        Loaded += (_, _) => UpdateVisuals(updateHex: true);
    }

    public System.Windows.Media.Color Color => FromHsv(_hue, _saturation, _value);

    public void SetColor(System.Windows.Media.Color color)
    {
        ToHsv(color, out _hue, out _saturation, out _value);
        UpdateVisuals(updateHex: true);
    }

    private void BuildPresets()
    {
        foreach (var hex in PresetColors)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var swatch = new System.Windows.Controls.Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = hex,
                Content = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0x80, 0x80, 0x80)),
                    BorderThickness = new Thickness(1),
                },
            };
            swatch.Click += (_, _) =>
            {
                SetColor(color);
                ColorChanged?.Invoke(Color);
            };
            Presets.Children.Add(swatch);
        }
    }

    private void SvArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Mouse.Capture(SvArea);
        PickSv(e.GetPosition(SvArea));
    }

    private void SvArea_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && Mouse.Captured == SvArea)
        {
            PickSv(e.GetPosition(SvArea));
        }
    }

    private void HueBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Mouse.Capture(HueBar);
        PickHue(e.GetPosition(HueBar));
    }

    private void HueBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && Mouse.Captured == HueBar)
        {
            PickHue(e.GetPosition(HueBar));
        }
    }

    private void Area_MouseUp(object sender, MouseButtonEventArgs e) => Mouse.Capture(null);

    private void PickSv(System.Windows.Point point)
    {
        _saturation = Math.Clamp(point.X / SvArea.Width, 0, 1);
        _value = 1 - Math.Clamp(point.Y / SvArea.Height, 0, 1);
        UpdateVisuals(updateHex: true);
        ColorChanged?.Invoke(Color);
    }

    private void PickHue(System.Windows.Point point)
    {
        _hue = Math.Clamp(point.X / HueBar.Width, 0, 1) * 360;
        UpdateVisuals(updateHex: true);
        ColorChanged?.Invoke(Color);
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (text.Length is not (7 or 9))
        {
            return;
        }

        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(text) is System.Windows.Media.Color color)
            {
                ToHsv(color, out _hue, out _saturation, out _value);
                UpdateVisuals(updateHex: false);
                ColorChanged?.Invoke(Color);
            }
        }
        catch (FormatException)
        {
        }
    }

    private void UpdateVisuals(bool updateHex)
    {
        _updating = true;
        try
        {
            var color = Color;
            SvArea.Background = new SolidColorBrush(FromHsv(_hue, 1, 1));
            Canvas.SetLeft(SvThumb, _saturation * SvArea.Width - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1 - _value) * SvArea.Height - SvThumb.Height / 2);
            Canvas.SetLeft(HueThumb, _hue / 360 * HueBar.Width - HueThumb.Width / 2);
            CurrentSwatch.Background = new SolidColorBrush(color);
            if (updateHex)
            {
                HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private static System.Windows.Media.Color FromHsv(double hue, double saturation, double value)
    {
        var h = (hue % 360 + 360) % 360 / 60;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(h % 2 - 1));
        var m = value - c;
        var (r, g, b) = (int)Math.Floor(h) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static void ToHsv(System.Windows.Media.Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        value = max;
        saturation = max <= 0 ? 0 : delta / max;
        if (delta <= 0)
        {
            hue = 0;
            return;
        }

        hue = max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (hue < 0)
        {
            hue += 360;
        }
    }
}
