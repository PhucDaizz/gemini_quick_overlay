using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeminiQuickOverlay.App.Hotkeys;
using GeminiQuickOverlay.App.Overlay;
using GeminiQuickOverlay.Core;
using GeminiQuickOverlay.Core.Automation;
using GeminiQuickOverlay.Core.Chrome;
using GeminiQuickOverlay.Core.Config;
using GeminiQuickOverlay.Core.Logging;
using GeminiQuickOverlay.Core.Models;
using GeminiQuickOverlay.Core.Paths;
using GeminiQuickOverlay.Core.Updates;

namespace GeminiQuickOverlay.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _config;
    private readonly GeminiAutomation _automation;
    private readonly UpdateService _updates;
    private readonly CancellationTokenSource _lifetime = new();
    private AppConfig _draft;
    private bool _loading;
    private bool _updatingOffsets;
    private PositionPreviewWindow? _positionPreview;
    private System.Windows.Controls.TextBox? _colorTarget;

    public SettingsWindow(ConfigService config, GeminiAutomation automation, UpdateService updates)
    {
        _config = config;
        _automation = automation;
        _updates = updates;
        _draft = config.GetSnapshot();
        _loading = true;
        InitializeComponent();
        Populate();
        _loading = false;
        ColorTextChanged(this, new RoutedEventArgs());
        ColorPanel.ColorChanged += OnPickedColorChanged;
        UpdateDetectedChrome();
        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/AIHelper.ico", UriKind.Absolute));
        }
        catch
        {
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        HidePositionPreview();
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    private void Populate()
    {
        PromptTextTextBox.Text = _draft.PromptText;
        PromptImageTextBox.Text = _draft.PromptImage;
        DisplaySecondsBox.Text = _draft.DisplaySeconds.ToString(CultureInfo.CurrentCulture);
        ResponseTimeoutBox.Text = _draft.ResponseTimeout.ToString(CultureInfo.CurrentCulture);
        PageWaitBox.Text = _draft.PageWait.ToString(CultureInfo.CurrentCulture);
        ImageWaitBox.Text = _draft.ImageWait.ToString(CultureInfo.CurrentCulture);
        ImageMaxSizeBox.Text = _draft.ImageMaxSize.ToString(CultureInfo.CurrentCulture);
        SettleMsBox.Text = _draft.SettleMs.ToString(CultureInfo.CurrentCulture);
        ShowPendingCheck.IsChecked = _draft.ShowPending;
        CheckUpdatesCheck.IsChecked = _draft.CheckUpdates;
        AutoLaunchCheck.IsChecked = _draft.AutoLaunchChrome;
        ApiServerCheck.IsChecked = _draft.ApiServerEnabled;
        ApiServerPortBox.Text = _draft.ApiServerPort.ToString(CultureInfo.CurrentCulture);

        TextColorBox.Text = _draft.TextColor;
        BackgroundColorBox.Text = _draft.BackgroundColor;
        FontSizeBox.Text = _draft.FontSize.ToString(CultureInfo.CurrentCulture);
        WrapLengthBox.Text = _draft.WrapLength.ToString(CultureInfo.CurrentCulture);
        LeftOffsetBox.Text = _draft.PositionLeftOffset.ToString(CultureInfo.CurrentCulture);
        BottomOffsetBox.Text = _draft.PositionBottomOffset.ToString(CultureInfo.CurrentCulture);
        OpacitySlider.Value = Math.Clamp(_draft.Opacity, 0.1, 1.0);
        OpacityLabel.Text = _draft.Opacity.ToString("P0", CultureInfo.CurrentCulture);

        HotkeySendBox.Text = _draft.HotkeySend;
        HotkeyReplayBox.Text = _draft.HotkeyReplay;
        HotkeyCopyBox.Text = _draft.HotkeyCopy;
        HotkeyHideBox.Text = _draft.HotkeyHide;
        HotkeyCaptureBox.Text = _draft.HotkeyCapture;

        ProfileDirBox.Text = _draft.ProfileDir;
        DebugPortBox.Text = _draft.DebugPort.ToString(CultureInfo.CurrentCulture);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildDraft(out var draft, out var error))
        {
            System.Windows.MessageBox.Show(this, error, "Cấu hình chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _config.Save(draft);
            Close();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Không lưu được cấu hình", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Author_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenPath(e.Uri.AbsoluteUri, "Could not open the link.");
        e.Handled = true;
    }

    private void BrowseProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Chọn thư mục profile Chrome",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            ProfileDirBox.Text = dialog.FolderName;
        }
    }

    private void ShowChrome_Click(object sender, RoutedEventArgs e)
    {
        if (!ChromeLauncher.ShowChrome(_config.Current))
        {
            System.Windows.MessageBox.Show(
                this,
                "Không tìm thấy Chrome hoặc không mở được Chrome.",
                "Mở Chrome",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestStatusText.Text = "Đang kiểm tra kết nối...";
        try
        {
            var result = await _automation.TestConnectionAsync(_lifetime.Token);
            TestStatusText.Text = result;
        }
        catch (OperationCanceledException)
        {
            TestStatusText.Text = "Đã hủy.";
        }
        catch (Exception exception)
        {
            TestStatusText.Text = $"Lỗi: {exception.Message}";
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Đang kiểm tra...";
        try
        {
            var update = await _updates.CheckAsync(force: true, _lifetime.Token);
            UpdateStatusText.Text = update is null
                ? "Bạn đang dùng bản mới nhất."
                : $"Có bản mới {update.Tag}: {update.Url}";
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "Đã hủy.";
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"Không kiểm tra được (mất mạng?): {exception.Message}";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(_config.Path, "Không mở được config.json.");
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        OpenPath(File.Exists(AppLog.LogPath) ? AppLog.LogPath : AppPaths.BaseDirectory, "Không mở được thư mục log.");
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _draft = new AppConfig();
        Populate();
        UpdatePreview();
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        HotkeySendBox.Text = "f2";
        HotkeyReplayBox.Text = "f4";
        HotkeyCopyBox.Text = "f3";
        HotkeyHideBox.Text = "esc";
        HotkeyCaptureBox.Text = "f6";
    }

    private void AppearanceChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void OpacityChanged(object sender, RoutedEventArgs e)
    {
        if (OpacityLabel is null)
        {
            return;
        }
        OpacityLabel.Text = OpacitySlider.Value.ToString("P0", CultureInfo.CurrentCulture);
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        _positionPreview?.SetAppearance(
            ParseBrush(TextColorBox.Text),
            TryDouble(FontSizeBox.Text, 11, out var fontSize) ? fontSize : 11,
            TryInt(WrapLengthBox.Text, 800, out var wrap) ? wrap : 800,
            OpacitySlider.Value);
    }

    private void Tabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs))
        {
            return;
        }

        if (Tabs.SelectedItem is System.Windows.Controls.TabItem { Header: "Giao diện" })
        {
            ShowPositionPreview();
        }
        else
        {
            HidePositionPreview();
        }
    }

    private void ShowPositionPreview()
    {
        if (_positionPreview is null)
        {
            _positionPreview = new PositionPreviewWindow();
            _positionPreview.OffsetsChanged += OnPreviewOffsetsChanged;
        }

        UpdatePreview();
        SyncPreviewOffsets();
        if (!_positionPreview.IsVisible)
        {
            _positionPreview.Show();
        }
    }

    private void HidePositionPreview()
    {
        if (_positionPreview is null)
        {
            return;
        }

        _positionPreview.OffsetsChanged -= OnPreviewOffsetsChanged;
        _positionPreview.Close();
        _positionPreview = null;
    }

    private void OnPreviewOffsetsChanged(int left, int bottom)
    {
        _updatingOffsets = true;
        try
        {
            LeftOffsetBox.Text = left.ToString(CultureInfo.CurrentCulture);
            BottomOffsetBox.Text = bottom.ToString(CultureInfo.CurrentCulture);
        }
        finally
        {
            _updatingOffsets = false;
        }
    }

    private void OffsetsTyped(object sender, RoutedEventArgs e)
    {
        if (!_loading && !_updatingOffsets)
        {
            SyncPreviewOffsets();
        }
    }

    private void SyncPreviewOffsets()
    {
        if (_positionPreview is null)
        {
            return;
        }

        if (TryInt(LeftOffsetBox.Text, 0, out var left) && TryInt(BottomOffsetBox.Text, 0, out var bottom))
        {
            _positionPreview.SetOffsets(left, bottom);
        }
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppConfig();
        LeftOffsetBox.Text = defaults.PositionLeftOffset.ToString(CultureInfo.CurrentCulture);
        BottomOffsetBox.Text = defaults.PositionBottomOffset.ToString(CultureInfo.CurrentCulture);
    }

    private void ColorTextChanged(object sender, RoutedEventArgs e)
    {
        if (TextColorSwatch is null || BackgroundColorSwatch is null)
        {
            return;
        }

        TextColorSwatch.Background = ParseBrush(TextColorBox.Text);
        BackgroundColorSwatch.Background = ParseBrush(BackgroundColorBox.Text);
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void PickTextColor_Click(object sender, RoutedEventArgs e) =>
        OpenColorPicker(TextColorBox, TextColorButton);

    private void PickBackgroundColor_Click(object sender, RoutedEventArgs e) =>
        OpenColorPicker(BackgroundColorBox, BackgroundColorButton);

    private void OpenColorPicker(System.Windows.Controls.TextBox target, System.Windows.Controls.Button anchor)
    {
        // Theme-aware solid background: light popup under dark text, dark popup under light text.
        var textBrush = Foreground as SolidColorBrush;
        var light = textBrush is null ||
                    (0.299 * textBrush.Color.R + 0.587 * textBrush.Color.G + 0.114 * textBrush.Color.B) / 255 < 0.5;
        ColorPopupBorder.Background = new SolidColorBrush(light
            ? System.Windows.Media.Color.FromRgb(0xFB, 0xFB, 0xFB)
            : System.Windows.Media.Color.FromRgb(0x2B, 0x2B, 0x2B));
        ColorPopupBorder.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, light ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White);

        ColorPopup.PlacementTarget = anchor;
        ColorPanel.SetColor(ParseColor(target.Text));
        _colorTarget = target;
        ColorPopup.IsOpen = true;
    }

    private void OnPickedColorChanged(System.Windows.Media.Color color)
    {
        if (_colorTarget is not null)
        {
            _colorTarget.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    private static System.Windows.Media.Color ParseColor(string? value) =>
        ParseBrush(value) is SolidColorBrush solid ? solid.Color : Colors.Black;

    private void UpdateDetectedChrome()
    {
        var chrome = ChromeLocator.FindChrome();
        DetectedChromeText.Text = chrome is null
            ? "Chưa tìm thấy Chrome. Tạo chrome_path.txt cạnh exe nếu Chrome ở vị trí khác."
            : $"Chrome: {chrome}";
    }

    private bool TryBuildDraft(out AppConfig draft, out string error)
    {
        draft = _draft.Clone();
        error = "";

        if (!TryInt(DisplaySecondsBox.Text, _draft.DisplaySeconds, out var displaySeconds, 1, 3600))
        {
            error = "Thời gian hiện đáp án phải từ 1 đến 3600 giây.";
            return false;
        }
        if (!TryInt(ResponseTimeoutBox.Text, _draft.ResponseTimeout, out var responseTimeout, 5, 600))
        {
            error = "Chờ phản hồi tối đa phải từ 5 đến 600 giây.";
            return false;
        }
        if (!TryDouble(PageWaitBox.Text, _draft.PageWait, out var pageWait, 0.5, 60))
        {
            error = "Chờ trang Gemini phải từ 0.5 đến 60 giây.";
            return false;
        }
        if (!TryInt(ImageWaitBox.Text, _draft.ImageWait, out var imageWait, 1, 60))
        {
            error = "Chờ ảnh dán phải từ 1 đến 60 giây.";
            return false;
        }
        if (!TryInt(ImageMaxSizeBox.Text, _draft.ImageMaxSize, out var imageMaxSize, 200, 4096))
        {
            error = "Cạnh ảnh tối đa phải từ 200 đến 4096 px.";
            return false;
        }
        if (!TryInt(SettleMsBox.Text, _draft.SettleMs, out var settleMs, 100, 30000))
        {
            error = "Thời gian chờ văn bản ổn định phải từ 100 đến 30000 ms.";
            return false;
        }
        if (!TryDouble(FontSizeBox.Text, _draft.FontSize, out var fontSize, 8, 72))
        {
            error = "Cỡ chữ phải từ 8 đến 72.";
            return false;
        }
        if (!TryInt(WrapLengthBox.Text, _draft.WrapLength, out var wrapLength, 200, 4096))
        {
            error = "Độ rộng tối đa phải từ 200 đến 4096.";
            return false;
        }
        if (!TryInt(LeftOffsetBox.Text, _draft.PositionLeftOffset, out var leftOffset, 0, 10000))
        {
            error = "Khoảng cách trái phải từ 0 đến 10000.";
            return false;
        }
        if (!TryInt(BottomOffsetBox.Text, _draft.PositionBottomOffset, out var bottomOffset, 0, 10000))
        {
            error = "Khoảng cách đáy phải từ 0 đến 10000.";
            return false;
        }
        if (!TryInt(DebugPortBox.Text, _draft.DebugPort, out var debugPort, 1, 65535))
        {
            error = "Cổng debug phải từ 1 đến 65535.";
            return false;
        }
        if (!TryInt(ApiServerPortBox.Text, _draft.ApiServerPort, out var apiPort, 1024, 65535) || apiPort == debugPort)
        {
            error = "Cổng API phải từ 1024 đến 65535 và khác cổng debug Chrome.";
            return false;
        }

        var hotkeys = new[]
        {
            (Name: "Gửi", Value: HotkeySendBox.Text),
            (Name: "Xem lại", Value: HotkeyReplayBox.Text),
            (Name: "Copy", Value: HotkeyCopyBox.Text),
            (Name: "Ẩn overlay", Value: HotkeyHideBox.Text),
            (Name: "Chụp vùng", Value: HotkeyCaptureBox.Text),
        };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hotkey in hotkeys)
        {
            if (!HotkeyParser.TryParse(hotkey.Value, out _, out _))
            {
                error = $"Phím tắt \"{hotkey.Name}\" không hợp lệ: {hotkey.Value}";
                return false;
            }
            if (!used.Add(hotkey.Value.Trim()))
            {
                error = $"Phím tắt bị trùng: {hotkey.Value}";
                return false;
            }
        }

        draft.PromptText = PromptTextTextBox.Text;
        draft.PromptImage = PromptImageTextBox.Text;
        draft.DisplaySeconds = displaySeconds;
        draft.ResponseTimeout = responseTimeout;
        draft.PageWait = pageWait;
        draft.ImageWait = imageWait;
        draft.ImageMaxSize = imageMaxSize;
        draft.SettleMs = settleMs;
        draft.ShowPending = ShowPendingCheck.IsChecked == true;
        draft.CheckUpdates = CheckUpdatesCheck.IsChecked == true;
        draft.AutoLaunchChrome = AutoLaunchCheck.IsChecked == true;
        draft.ApiServerEnabled = ApiServerCheck.IsChecked == true;
        draft.ApiServerPort = apiPort;

        draft.TextColor = TextColorBox.Text.Trim();
        draft.BackgroundColor = BackgroundColorBox.Text.Trim();
        draft.FontSize = fontSize;
        draft.WrapLength = wrapLength;
        draft.PositionLeftOffset = leftOffset;
        draft.PositionBottomOffset = bottomOffset;
        draft.Opacity = Math.Clamp(OpacitySlider.Value, 0.1, 1.0);

        draft.HotkeySend = HotkeySendBox.Text.Trim();
        draft.HotkeyReplay = HotkeyReplayBox.Text.Trim();
        draft.HotkeyCopy = HotkeyCopyBox.Text.Trim();
        draft.HotkeyHide = HotkeyHideBox.Text.Trim();
        draft.HotkeyCapture = HotkeyCaptureBox.Text.Trim();

        draft.ProfileDir = ProfileDirBox.Text.Trim();
        draft.DebugPort = debugPort;
        return true;
    }

    private static bool TryInt(string text, int fallback, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) ||
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryInt(string text, int fallback, out int value, int min, int max) =>
        TryInt(text, fallback, out value) && value >= min && value <= max;

    private static bool TryDouble(string text, double fallback, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryDouble(string text, double fallback, out double value, double min, double max) =>
        TryDouble(text, fallback, out value) && value >= min && value <= max;

    private static System.Windows.Media.Brush ParseBrush(string? value)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                System.Windows.Media.ColorConverter.ConvertFromString(value) is System.Windows.Media.Color color)
            {
                return new SolidColorBrush(color);
            }
        }
        catch
        {
        }

        return System.Windows.Media.Brushes.Black;
    }

    private static void OpenPath(string path, string error)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(exception.Message, error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
