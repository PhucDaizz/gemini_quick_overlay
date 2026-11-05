namespace GeminiQuickOverlay.Core;

public static class AppConstants
{
    public const string AppName = "AIHelper";
    public const string AppVersion = "1.2.1";
    public const string GeminiUrl = "https://gemini.google.com/app";
    public const string ConfigFileName = "config.json";
    public const string LogFileName = "gemini_helper.log";
    public const string UpdateStateFileName = "update_state.json";
    public const string IconFileName = "AIHelper.ico";
    public const string GithubRepo = "PhucDaizz/gemini_quick_overlay";
    public const string ReleasesApiUrl = $"https://api.github.com/repos/{GithubRepo}/releases/latest";
    public const string ReleasesPageUrl = $"https://github.com/{GithubRepo}/releases/latest";
    public const string MutexName = @"Local\GeminiQuickOverlayAssistant";
    public const string ChromePathFileName = "chrome_path.txt";
    public const string DefaultChromeProfile = @"C:\selenium\ChromeProfile";
    public const string Cancelled = "__CANCELLED__";
}
