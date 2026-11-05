using System.Windows.Input;

namespace GeminiQuickOverlay.App.Hotkeys;

public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public static bool TryParse(string? value, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < parts.Length - 1; index++)
        {
            switch (parts[index].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= ModWin;
                    break;
                default:
                    return false;
            }
        }

        var keyName = parts[^1].Trim();
        var key = ParseKey(keyName);
        if (key == System.Windows.Forms.Keys.None)
        {
            return false;
        }

        virtualKey = (uint)key;
        return true;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("ctrl");
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("alt");
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("shift");
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("win");
        }

        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }

    private static System.Windows.Forms.Keys ParseKey(string keyName)
    {
        if (keyName.Equals("esc", StringComparison.OrdinalIgnoreCase))
        {
            return System.Windows.Forms.Keys.Escape;
        }
        if (keyName.Equals("space", StringComparison.OrdinalIgnoreCase))
        {
            return System.Windows.Forms.Keys.Space;
        }
        if (keyName.Length == 1 && char.IsDigit(keyName[0]))
        {
            return (System.Windows.Forms.Keys)((int)System.Windows.Forms.Keys.D0 + (keyName[0] - '0'));
        }
        if (keyName.Length == 1 && char.IsLetter(keyName[0]))
        {
            return (System.Windows.Forms.Keys)char.ToUpperInvariant(keyName[0]);
        }

        return Enum.TryParse<System.Windows.Forms.Keys>(keyName, true, out var key)
            ? key
            : System.Windows.Forms.Keys.None;
    }

    private static string FormatKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((int)key - (int)Key.NumPad0).ToString();
        }
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString().ToLowerInvariant();
        }
        if (key == Key.Escape)
        {
            return "esc";
        }
        if (key == Key.Space)
        {
            return "space";
        }

        return key.ToString().ToLowerInvariant();
    }
}
