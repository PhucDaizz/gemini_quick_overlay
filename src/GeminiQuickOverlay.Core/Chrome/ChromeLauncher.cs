using System.Diagnostics;
using GeminiQuickOverlay.Core.Logging;
using GeminiQuickOverlay.Core.Models;
using GeminiQuickOverlay.Core.Paths;

namespace GeminiQuickOverlay.Core.Chrome;

public static class ChromeLauncher
{
    private static readonly string[] ChromeArgs =
    {
        "--disable-background-timer-throttling",
        "--disable-backgrounding-occluded-windows",
        "--disable-renderer-backgrounding",
        "--disable-features=CalculateNativeWinOcclusion",
    };

    private static readonly SemaphoreSlim LaunchLock = new(1, 1);

    public static string ProfileDir(AppConfig config)
    {
        var profile = config.ProfileDir?.Trim();
        return string.IsNullOrWhiteSpace(profile) ? AppConstants.DefaultChromeProfile : profile;
    }

    /// <summary>
    /// Chrome keeps an exclusive handle on "lockfile" inside the profile while it runs.
    /// </summary>
    public static bool IsProfileInUse(string profileDir)
    {
        var lockFile = Path.Combine(profileDir, "lockfile");
        if (!File.Exists(lockFile))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<ChromeEnsureResult> EnsureChromeAsync(
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        var port = Math.Clamp(config.DebugPort, 1, 65535);
        if (DebugPortProbe.IsOpen(port) || !config.AutoLaunchChrome)
        {
            return ChromeEnsureResult.Success;
        }

        await LaunchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LaunchLockedAsync(config, port, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            LaunchLock.Release();
        }
    }

    private static async Task<ChromeEnsureResult> LaunchLockedAsync(
        AppConfig config,
        int port,
        CancellationToken cancellationToken)
    {
        if (DebugPortProbe.IsOpen(port))
        {
            return ChromeEnsureResult.Success;
        }

        var profile = ProfileDir(config);
        if (IsProfileInUse(profile))
        {
            // Launching again would only hand the command to that Chrome (which has no debug
            // port) and pop open yet another window on every hotkey press.
            return ChromeEnsureResult.Failure(
                "Đang có cửa sổ Chrome dùng profile của tool nhưng không bật cổng debug. " +
                "Hãy đóng hết Chrome đó rồi thử lại.");
        }

        var chrome = ChromeLocator.FindChrome();
        if (chrome is null)
        {
            return ChromeEnsureResult.Failure(
                "Không tìm thấy Chrome. Tạo chrome_path.txt chứa đường dẫn chrome.exe.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = chrome,
                // WindowStyle is only honoured with ShellExecute; CreateNoWindow does not hide GUI apps.
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppPaths.BaseDirectory,
                Arguments = string.Join(' ', BuildArguments(port, profile)),
            };

            using var process = Process.Start(startInfo);
            AppLog.Info($"Đã khởi động Chrome (port {port}).");
        }
        catch (Exception exception)
        {
            return ChromeEnsureResult.Failure($"Không khởi động được Chrome: {exception.Message}");
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (DebugPortProbe.IsOpen(port))
            {
                return ChromeEnsureResult.Success;
            }
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        return ChromeEnsureResult.Failure("Chrome khởi động nhưng chưa mở cổng debug.");
    }

    private static List<string> BuildArguments(int port, string profile)
    {
        var arguments = new List<string>
        {
            $"--remote-debugging-port={port}",
            $"\"--user-data-dir={profile}\"",
        };
        arguments.AddRange(ChromeArgs);
        return arguments;
    }

    public static bool ShowChrome(AppConfig config)
    {
        var chrome = ChromeLocator.FindChrome();
        if (chrome is null)
        {
            return false;
        }

        try
        {
            var port = Math.Clamp(config.DebugPort, 1, 65535);
            var arguments = BuildArguments(port, ProfileDir(config));
            arguments.Add(AppConstants.GeminiUrl);
            Process.Start(new ProcessStartInfo
            {
                FileName = chrome,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = AppPaths.BaseDirectory,
                Arguments = string.Join(' ', arguments),
            });
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error(exception, "Không mở được Chrome");
            return false;
        }
    }
}

public sealed record ChromeEnsureResult(bool Ok, string? Error)
{
    public static ChromeEnsureResult Success { get; } = new(true, null);

    public static ChromeEnsureResult Failure(string error) => new(false, error);
}
