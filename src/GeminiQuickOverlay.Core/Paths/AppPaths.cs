using GeminiQuickOverlay.Core;

namespace GeminiQuickOverlay.Core.Paths;

public static class AppPaths
{
    public static string BaseDirectory =>
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string ConfigPath => Path.Combine(BaseDirectory, AppConstants.ConfigFileName);

    public static string LogPath => Path.Combine(BaseDirectory, AppConstants.LogFileName);

    public static string UpdateStatePath => Path.Combine(BaseDirectory, AppConstants.UpdateStateFileName);

    public static string IconPath => Path.Combine(BaseDirectory, "Assets", AppConstants.IconFileName);

    public static string ChromePathOverridePath => Path.Combine(BaseDirectory, AppConstants.ChromePathFileName);
}
