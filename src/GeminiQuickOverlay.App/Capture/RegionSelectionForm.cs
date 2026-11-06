using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GeminiQuickOverlay.App.Capture;

/// <summary>
/// Full-screen frozen-frame selector. It paints a dimmed copy of the screenshot and lets the user
/// drag a rectangle; the selection is reported in screenshot pixel coordinates. WinForms is used on
/// purpose: with per-monitor DPI awareness its bounds are physical pixels, which is what the screen
/// capture needs, whereas WPF would work in scaled units.
/// </summary>
internal sealed class RegionSelectionForm : Form
{
    private const int MinSelectionSize = 8;

    private readonly Bitmap _frozen;
    private readonly Bitmap _dimmed;
    private readonly Rectangle _screenBounds;
    private readonly TaskCompletionSource<Rectangle?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Point _start;
    private Rectangle _selection;
    private bool _dragging;

    public RegionSelectionForm(Bitmap frozen, Rectangle screenBounds)
    {
        _frozen = frozen;
        _screenBounds = screenBounds;
        _dimmed = CreateDimmed(frozen);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        Bounds = screenBounds;
    }

    /// <summary>Completes with the selected rectangle, or null when cancelled.</summary>
    public Task<Rectangle?> Result => _result.Task;

    public void Cancel()
    {
        _result.TrySetResult(null);
        Close();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Re-apply: the window manager may adjust bounds when the handle is created on a mixed-DPI setup.
        Bounds = _screenBounds;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Cancel();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            Cancel();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _start = e.Location;
            _selection = Rectangle.Empty;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        var previous = _selection;
        _selection = Rectangle.FromLTRB(
            Math.Min(_start.X, e.X),
            Math.Min(_start.Y, e.Y),
            Math.Max(_start.X, e.X),
            Math.Max(_start.Y, e.Y));
        Invalidate(Rectangle.Inflate(Rectangle.Union(previous, _selection), 4, 36));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;
        var selection = Rectangle.Intersect(_selection, new Rectangle(Point.Empty, _frozen.Size));
        if (selection.Width < MinSelectionSize || selection.Height < MinSelectionSize)
        {
            Cancel();
            return;
        }

        _result.TrySetResult(selection);
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_dimmed, 0, 0);
        if (_selection.Width <= 0 || _selection.Height <= 0)
        {
            DrawHint(g);
            return;
        }

        g.DrawImage(_frozen, _selection, _selection, GraphicsUnit.Pixel);
        using (var pen = new Pen(Color.FromArgb(0x3B, 0x82, 0xF6), 2))
        {
            g.DrawRectangle(pen, _selection);
        }

        var label = $"{_selection.Width} × {_selection.Height}";
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        var size = g.MeasureString(label, font);
        var box = new RectangleF(
            _selection.Left,
            _selection.Top - size.Height - 8 >= 0 ? _selection.Top - size.Height - 8 : _selection.Bottom + 4,
            size.Width + 12,
            size.Height + 4);
        using (var fill = new SolidBrush(Color.FromArgb(0xE0, 0x1F, 0x29, 0x37)))
        {
            g.FillRectangle(fill, box);
        }
        g.DrawString(label, font, Brushes.White, box.Left + 6, box.Top + 2);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _result.TrySetResult(null);
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmed.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawHint(Graphics g)
    {
        const string hint = "Kéo chuột để chọn vùng cần gửi  •  Esc hoặc chuột phải để hủy";
        using var font = new Font("Segoe UI", 12f, FontStyle.Bold);
        var size = g.MeasureString(hint, font);
        var primary = Screen.PrimaryScreen?.Bounds ?? _screenBounds;
        var x = primary.Left - _screenBounds.Left + (primary.Width - size.Width) / 2;
        var y = primary.Top - _screenBounds.Top + 48;
        var box = new RectangleF(x - 14, y - 8, size.Width + 28, size.Height + 16);
        using var fill = new SolidBrush(Color.FromArgb(0xE0, 0x1F, 0x29, 0x37));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillRectangle(fill, box);
        g.DrawString(hint, font, Brushes.White, x, y);
    }

    private static Bitmap CreateDimmed(Bitmap source)
    {
        var dimmed = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dimmed);
        g.DrawImageUnscaled(source, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        g.FillRectangle(shade, 0, 0, dimmed.Width, dimmed.Height);
        return dimmed;
    }
}
