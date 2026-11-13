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

    /// <summary>Công tắc cổng API nội bộ kiểu OpenAI cho app khác dùng Gemini (mặc định TẮT).</summary>
    public bool ApiServerEnabled { get; set; } = false;

    public int ApiServerPort { get; set; } = 8765;

    public string ProfileDir { get; set; } = "";

    public string HotkeySend { get; set; } = "f2";

    public string HotkeyReplay { get; set; } = "f4";

    public string HotkeyCopy { get; set; } = "f3";

    public string HotkeyHide { get; set; } = "esc";

    public string HotkeyCapture { get; set; } = "f6";

    public bool ShowPending { get; set; } = true;

    public Selectors Selectors { get; set; } = new();

    /// <summary>Which chat the hotkeys use: "gemini" or "deepseek" (the API picks one per request via "model").</summary>
    public string Provider { get; set; } = ProviderCatalog.DefaultId;

    /// <summary>
    /// Several providers share one Chrome window, so all but one tab are hidden and the browser throttles hidden
    /// tabs. When on, the provider's tab is brought to the front before sending if it is currently hidden.
    /// </summary>
    public bool ActivateProviderTab { get; set; } = true;

    /// <summary>Optional per-provider selector overrides: { "deepseek": { "response": "...", "send": "" } }.</summary>
    public Dictionary<string, Dictionary<string, string>> ProviderSelectors { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }

    public AppConfig Clone()
    {
        var clone = (AppConfig)MemberwiseClone();
        clone.Selectors = Selectors.Clone();
        clone.ProviderSelectors = ProviderSelectors?.ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<string, string>(pair.Value),
            StringComparer.OrdinalIgnoreCase) ?? new();
        if (ExtraData is not null)
        {
            clone.ExtraData = new Dictionary<string, JsonElement>(ExtraData);
        }
        return clone;
    }
}
