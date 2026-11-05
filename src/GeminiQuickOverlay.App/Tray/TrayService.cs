using System.Diagnostics;
using System.IO;
using GeminiQuickOverlay.Core;
using GeminiQuickOverlay.Core.Logging;
using GeminiQuickOverlay.Core.Paths;
using GeminiQuickOverlay.Core.Updates;

namespace GeminiQuickOverlay.App.Tray;

public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _updateItem;
    private readonly System.Windows.Forms.ContextMenuStrip _menu;
    private UpdateInfo? _update;
    private bool _disposed;

    public TrayService()
    {
        _menu = new System.Windows.Forms.ContextMenuStrip();
        _menu.Items.Add(CreateItem("Hiện đáp án", () => ShowRequested?.Invoke(), true));
        _menu.Items.Add(CreateItem("Copy đáp án", () => CopyRequested?.Invoke()));
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _updateItem = CreateItem("", OpenUpdate);
        _updateItem.Visible = false;
        _menu.Items.Add(_updateItem);
        _menu.Items.Add(CreateItem(
            $"Kiểm tra cập nhật (v{AppConstants.AppVersion})",
            () => CheckUpdatesRequested?.Invoke()));
        _menu.Items.Add(CreateItem("Mở Chrome", () => ShowChromeRequested?.Invoke()));
        _menu.Items.Add(CreateItem("Cài đặt...", () => SettingsRequested?.Invoke()));
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _menu.Items.Add(CreateItem("Khởi động lại", () => RestartRequested?.Invoke()));
        _menu.Items.Add(CreateItem("Thoát", () => ExitRequested?.Invoke()));

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = AppConstants.AppName,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public event Action? ShowRequested;

    public event Action? CopyRequested;

    public event Action? SettingsRequested;

    public event Action? CheckUpdatesRequested;

    public event Action? ShowChromeRequested;

    public event Action? RestartRequested;

    public event Action? ExitRequested;

    public void SetUpdate(UpdateInfo? update)
    {
        _update = update;
        if (update is null)
        {
            _updateItem.Visible = false;
            return;
        }

        _updateItem.Text = $"Có bản mới {update.Tag} - Tải về";
        _updateItem.Visible = true;
    }

    public void ShowInfo(string title, string message)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(4000, title, message, System.Windows.Forms.ToolTipIcon.Info);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private void OpenUpdate()
    {
        if (_update is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_update.Url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Không mở được trang tải bản mới");
        }
    }

    private static System.Windows.Forms.ToolStripMenuItem CreateItem(
        string text,
        Action action,
        bool isDefault = false)
    {
        var item = new System.Windows.Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        if (isDefault)
        {
            item.Font = new System.Drawing.Font(item.Font, System.Drawing.FontStyle.Bold);
        }
        return item;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            if (File.Exists(AppPaths.IconPath))
            {
                return new System.Drawing.Icon(AppPaths.IconPath);
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Không load được icon tray từ file: {exception.Message}");
        }

        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/AIHelper.ico", UriKind.Absolute));
            if (resource?.Stream is not null)
            {
                using var stream = resource.Stream;
                return new System.Drawing.Icon(stream);
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Không load được icon tray nhúng: {exception.Message}");
        }

        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                return System.Drawing.Icon.ExtractAssociatedIcon(executable)
                    ?? System.Drawing.SystemIcons.Application;
            }
        }
        catch
        {
        }

        return System.Drawing.SystemIcons.Application;
    }
}
