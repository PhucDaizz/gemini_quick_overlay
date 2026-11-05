using GeminiQuickOverlay.Core.Paths;

namespace GeminiQuickOverlay.Core.Logging;

public static class AppLog
{
    private const long MaxBytes = 1_000_000;
    private const int BackupCount = 3;
    private static readonly object Gate = new();
    private static bool _initialized;

    public static string LogPath => AppPaths.LogPath;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
            try
            {
                var directory = Path.GetDirectoryName(AppPaths.LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch
            {
            }
        }
    }

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(Exception exception, string message) =>
        Write("ERROR", $"{message}: {exception}");

    private static void Write(string level, string message)
    {
        Initialize();
        lock (Gate)
        {
            try
            {
                RotateIfNeeded();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(AppPaths.LogPath, line);
            }
            catch
            {
            }
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(AppPaths.LogPath) || new FileInfo(AppPaths.LogPath).Length < MaxBytes)
            {
                return;
            }

            var oldest = $"{AppPaths.LogPath}.{BackupCount}";
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var index = BackupCount - 1; index >= 1; index--)
            {
                var source = $"{AppPaths.LogPath}.{index}";
                var destination = $"{AppPaths.LogPath}.{index + 1}";
                if (File.Exists(source))
                {
                    File.Move(source, destination, true);
                }
            }

            File.Move(AppPaths.LogPath, $"{AppPaths.LogPath}.1", true);
        }
        catch
        {
        }
    }
}
