using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GeminiQuickOverlay.App.Hotkeys;

public sealed class HotkeyTextBox : System.Windows.Controls.TextBox
{
    public HotkeyTextBox()
    {
        IsReadOnly = true;
        TextAlignment = TextAlignment.Center;
        ToolTip = "Bấm tổ hợp phím để đổi. Tab để rời ô.";
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        Text = HotkeyParser.Format(key, Keyboard.Modifiers);
        CaretIndex = Text.Length;
        e.Handled = true;
    }
}
