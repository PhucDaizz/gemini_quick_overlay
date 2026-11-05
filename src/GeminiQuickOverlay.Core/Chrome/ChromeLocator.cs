using GeminiQuickOverlay.Core.Paths;

namespace GeminiQuickOverlay.Core.Chrome;

public static class ChromeLocator
{
    public static string? FindChrome()
    {
        try
        {
            if (File.Exists(AppPaths.ChromePathOverridePath))
            {
                var overridePath = File.ReadLines(AppPaths.ChromePathOverridePath).FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
                {
                    return overridePath;
                }
            }
        }
        catch
        {
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var path = Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
