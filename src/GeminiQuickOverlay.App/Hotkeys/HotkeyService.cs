using System.Runtime.InteropServices;
using System.Windows.Interop;
using GeminiQuickOverlay.Core.Logging;
using GeminiQuickOverlay.Core.Models;

namespace GeminiQuickOverlay.App.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _registered = new();
    private bool _disposed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("GeminiQuickOverlayHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event Action? SendPressed;

    public event Action? ReplayPressed;

    public event Action? CopyPressed;

    public event Action? HidePressed;

    public event Action? CapturePressed;

    public void RegisterAll(AppConfig config)
    {
        UnregisterAll();
        Register(1, config.HotkeySend, "send");
        Register(2, config.HotkeyReplay, "replay");
        Register(3, config.HotkeyCopy, "copy");
        Register(4, config.HotkeyHide, "hide");
        Register(5, config.HotkeyCapture, "capture");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private void Register(int id, string? spec, string name)
    {
        if (!HotkeyParser.TryParse(spec, out var modifiers, out var virtualKey))
        {
            AppLog.Warn($"Hotkey {name} không hợp lệ: {spec}");
            return;
        }

        if (!RegisterHotKey(_source.Handle, id, modifiers | HotkeyParser.ModNoRepeat, virtualKey))
        {
            AppLog.Warn($"Không đăng ký được hotkey {name}={spec} (có thể bị ứng dụng khác chiếm).");
            return;
        }

        _registered[id] = name;
        AppLog.Info($"Hotkey {name}: {spec}");
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        handled = true;
        switch (wParam.ToInt32())
        {
            case 1:
                SendPressed?.Invoke();
                break;
            case 2:
                ReplayPressed?.Invoke();
                break;
            case 3:
                CopyPressed?.Invoke();
                break;
            case 4:
                HidePressed?.Invoke();
                break;
            case 5:
                CapturePressed?.Invoke();
                break;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
