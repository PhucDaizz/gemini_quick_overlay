using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeminiQuickOverlay.Core.Models;

public sealed class AppConfig
{
    public string TextColor { get; set; } = "black";

    public string BackgroundColor { get; set; } = "#abcdef";

    public double Opacity { get; set; } = 0.5;

    public double FontSize { get; set; } = 11;

    public int PositionBottomOffset { get; set; } = 50;

    public int PositionLeftOffset { get; set; } = 20;

    public string PromptText { get; set; } = "chỉ trả lời đáp án không giải thích: ";

    public string PromptImage { get; set; } = "hãy giải bài trong ảnh, chỉ trả lời đáp án ngắn gọn: ";

    public int WrapLength { get; set; } = 800;

    public double PageWait { get; set; } = 5.0;

    public int ResponseTimeout { get; set; } = 60;

    public int DisplaySeconds { get; set; } = 15;

    public int ImageWait { get; set; } = 10;

    public int ImageMaxSize { get; set; } = 1600;

    public double PollInterval { get; set; } = 0.35;

    public int SettleMs { get; set; } = 1500;

    public int DebugPort { get; set; } = 9222;

    public bool AutoLaunchChrome { get; set; } = true;

    public bool CheckUpdates { get; set; } = true;

    public string ProfileDir { get; set; } = "";

    public string HotkeySend { get; set; } = "f2";

    public string HotkeyReplay { get; set; } = "f4";

    public string HotkeyCopy { get; set; } = "f3";

    public string HotkeyHide { get; set; } = "esc";

    public string HotkeyCapture { get; set; } = "f6";

    public bool ShowPending { get; set; } = true;

    public Selectors Selectors { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }

    public AppConfig Clone()
    {
        var clone = (AppConfig)MemberwiseClone();
        clone.Selectors = Selectors.Clone();
        if (ExtraData is not null)
        {
            clone.ExtraData = new Dictionary<string, JsonElement>(ExtraData);
        }
        return clone;
    }
}
