using System.Diagnostics;
using System.Windows;
using GeminiQuickOverlay.App.Capture;
using GeminiQuickOverlay.App.Hotkeys;
using GeminiQuickOverlay.App.Overlay;
using GeminiQuickOverlay.App.Services;
using GeminiQuickOverlay.App.Settings;
using GeminiQuickOverlay.App.Tray;
using GeminiQuickOverlay.Core;
using GeminiQuickOverlay.Core.Automation;
using GeminiQuickOverlay.Core.Chrome;
using GeminiQuickOverlay.Core.Config;
using GeminiQuickOverlay.Core.Logging;
using GeminiQuickOverlay.Core.Paths;
using GeminiQuickOverlay.Core.Updates;

namespace GeminiQuickOverlay.App;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private ConfigService? _config;
    private OverlayWindow? _overlay;
    private TrayService? _tray;
    private HotkeyService? _hotkeys;
    private GeminiAutomation? _automation;
    private UpdateService? _updates;
    private SettingsWindow? _settingsWindow;
    private CancellationTokenSource? _sendCancellation;
    private string _lastResponse = "";
    private DateTime _lastSendUtc;
    private bool _busy;
    private bool _exiting;
    private bool _restarting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppLog.Initialize();
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var createdNew = false;
        _mutex = new Mutex(true, AppConstants.MutexName, out createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Gemini Quick Assistant đang chạy rồi!",
                AppConstants.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _config = new ConfigService();
            _overlay = new OverlayWindow(_config);
            _automation = new GeminiAutomation(_config);
            _updates = new UpdateService();
            _tray = new TrayService();
            _hotkeys = new HotkeyService();

            _tray.ShowRequested += () => _overlay.ShowMessage(
                string.IsNullOrWhiteSpace(_lastResponse) ? "Chưa có đáp án nào!" : _lastResponse,
                _config.Current.DisplaySeconds);
            _tray.CopyRequested += OnCopyPressed;
            _tray.SettingsRequested += ShowSettings;
            _tray.CheckUpdatesRequested += () => _ = CheckUpdatesAsync(force: true);
            _tray.ShowChromeRequested += ShowChrome;
            _tray.RestartRequested += Restart;
            _tray.ExitRequested += ExitApp;

            _hotkeys.SendPressed += OnSendPressed;
            _hotkeys.ReplayPressed += OnReplayPressed;
            _hotkeys.CopyPressed += OnCopyPressed;
            _hotkeys.HidePressed += OnHidePressed;
            _hotkeys.CapturePressed += OnCapturePressed;
            _hotkeys.RegisterAll(_config.Current);
            _config.SettingsChanged += OnSettingsChanged;

            if (_config.Current.CheckUpdates)
            {
                _ = CheckUpdatesAsync(force: false);
            }

            if (e.Args.Any(argument => argument.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
            {
                ShowSettings();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Không khởi động được ứng dụng");
            System.Windows.MessageBox.Show(
                exception.Message,
                "Không khởi động được AIHelper",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        try
        {
            _sendCancellation?.Cancel();
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _overlay?.Close();
            _config?.Dispose();
            if (_automation is not null)
            {
                _automation.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Lỗi khi thoát");
        }
        finally
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
            }
            _mutex?.Dispose();
            _mutex = null;

            if (_restarting)
            {
                StartRestartedProcess();
            }
        }

        base.OnExit(e);
    }

    private async void OnSendPressed()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSendUtc).TotalMilliseconds < 400 || _busy)
        {
            return;
        }
        _lastSendUtc = now;

        var config = _config!.Current;
        var clipboard = ClipboardService.Read(config.ImageMaxSize);
        string prompt;
        var isImage = clipboard.HasImage;
        if (isImage)
        {
            prompt = "";
        }
        else if (!string.IsNullOrWhiteSpace(clipboard.Text))
        {
            prompt = config.PromptText + clipboard.Text;
        }
        else
        {
            _overlay!.ShowMessage("Clipboard trống!", 1);
            return;
        }

        await SendAsync(prompt, isImage, clipboard.ImagePng, pasteWithShortcut: true);
    }

    private async void OnCapturePressed()
    {
        if (_busy || ScreenCaptureService.IsActive)
        {
            return;
        }

        // Block F2 while selecting; the flag is released before SendAsync takes it again.
        _busy = true;
        byte[]? png = null;
        try
        {
            _overlay!.HideNow();
            await Task.Delay(150); // let the overlay disappear so it is not part of the screenshot
            png = await ScreenCaptureService.PickRegionPngAsync(_config!.Current.ImageMaxSize);
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Chụp vùng màn hình lỗi");
            _overlay!.ShowMessage($"Lỗi chụp màn hình: {exception.Message}", 5);
        }
        finally
        {
            _busy = false;
        }

        if (png is null)
        {
            return;
        }

        await SendAsync("", isImage: true, png, pasteWithShortcut: false);
    }

    private async Task SendAsync(string prompt, bool isImage, byte[]? imagePng, bool pasteWithShortcut)
    {
        var config = _config!.Current;
        _busy = true;
        _sendCancellation = new CancellationTokenSource();
        if (config.ShowPending)
        {
            _overlay!.ShowMessage("...", 2);
        }

        try
        {
            var outcome = await _automation!.SendAndGetResponseAsync(
                prompt,
                isImage,
                imagePng,
                _sendCancellation.Token,
                pasteWithShortcut);
            if (outcome.Status == AutomationStatus.Cancelled)
            {
                return;
            }
            if (outcome.Status == AutomationStatus.Error)
            {
                _overlay!.ShowMessage(outcome.Text, 6);
                return;
            }

            _lastResponse = outcome.Text;
            _overlay!.ShowMessage(outcome.Text, config.DisplaySeconds);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Lỗi xử lý câu hỏi");
            _overlay!.ShowMessage($"Lỗi: {exception.Message}", 6);
        }
        finally
        {
            _busy = false;
            _sendCancellation?.Dispose();
            _sendCancellation = null;
        }
    }

    private void OnReplayPressed()
    {
        _overlay!.ShowMessage(
            string.IsNullOrWhiteSpace(_lastResponse) ? "Chưa có đáp án nào!" : _lastResponse,
            _config!.Current.DisplaySeconds);
    }

    private void OnCopyPressed()
    {
        if (string.IsNullOrWhiteSpace(_lastResponse))
        {
            _overlay!.ShowMessage("Chưa có đáp án để copy!", 2);
            return;
        }

        if (ClipboardService.SetText(_lastResponse))
        {
            AppLog.Info("Đã copy đáp án.");
        }
        else
        {
            _overlay!.ShowMessage("Không copy được đáp án.", 3);
        }
    }

    private void OnHidePressed()
    {
        // Esc is a global hotkey, so the selection form never receives it: cancel the capture here.
        ScreenCaptureService.CancelActive();
        _sendCancellation?.Cancel();
        if (_automation is not null)
        {
            _ = _automation.CancelAsync();
        }
        _overlay!.HideNow();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_config!, _automation!, _updates!);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private async Task CheckUpdatesAsync(bool force, CancellationToken cancellationToken = default)
    {
        try
        {
            var update = await _updates!.CheckAsync(force, cancellationToken);
            if (update is null)
            {
                if (force)
                {
                    _overlay!.ShowMessage("Bạn đang dùng bản mới nhất.", 3);
                }
                return;
            }

            _tray!.SetUpdate(update);
            _overlay!.ShowMessage($"Có bản mới {update.Tag}. Mở menu tray để tải về.", 6);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Không kiểm tra được cập nhật: {exception.Message}");
            if (force)
            {
                _overlay!.ShowMessage("Không kiểm tra được cập nhật (mất mạng?).", 4);
            }
        }
    }

    private void ShowChrome()
    {
        if (!ChromeLauncher.ShowChrome(_config!.Current))
        {
            _overlay!.ShowMessage("Không tìm thấy Chrome hoặc không mở được Chrome.", 4);
        }
    }

    private void Restart()
    {
        _restarting = true;
        Shutdown();
    }

    private static void StartRestartedProcess()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = AppPaths.BaseDirectory,
            });
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Không khởi động lại được.");
        }
    }

    private void ExitApp() => Shutdown();

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_exiting)
            {
                return;
            }
            _overlay!.ApplyConfig();
            _hotkeys!.RegisterAll(_config!.Current);
        });
    }
}
