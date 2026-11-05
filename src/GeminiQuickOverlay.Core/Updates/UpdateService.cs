using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GeminiQuickOverlay.Core.Logging;
using GeminiQuickOverlay.Core.Paths;

namespace GeminiQuickOverlay.Core.Updates;

public sealed partial class UpdateService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(6),
    };

    private readonly string _statePath;

    public UpdateService(string? statePath = null)
    {
        _statePath = statePath ?? AppPaths.UpdateStatePath;
    }

    public async Task<UpdateInfo?> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var state = LoadState();

        string? tag;
        string? url;
        if (!force && state.CheckedAt is not null && now - state.CheckedAt.Value < CheckInterval)
        {
            tag = state.Tag;
            url = state.Url;
        }
        else
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, AppConstants.ReleasesApiUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.UserAgent.ParseAdd($"{AppConstants.AppName}/{AppConstants.AppVersion}");
                using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                tag = document.RootElement.TryGetProperty("tag_name", out var tagElement)
                    ? tagElement.GetString()
                    : null;
                url = document.RootElement.TryGetProperty("html_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
                SaveState(new UpdateState { CheckedAt = now, Tag = tag, Url = url });
            }
            catch (Exception exception)
            {
                AppLog.Info($"Không kiểm tra được cập nhật: {exception.Message}");
                if (force)
                {
                    throw;
                }
                return null;
            }
        }

        if (tag is null || !IsNewer(tag))
        {
            return null;
        }

        return new UpdateInfo(tag, url ?? AppConstants.ReleasesPageUrl);
    }

    public static bool IsNewer(string latestTag, string? current = null)
    {
        var latest = ParseVersion(latestTag);
        var now = ParseVersion(current ?? CurrentVersion());
        return latest is not null && now is not null && latest.CompareTo(now) > 0;
    }

    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }
        var match = VersionRegex().Match(tag);
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
    }

    public static string CurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
                      ?? typeof(UpdateService).Assembly.GetName().Version;
        return version is null
            ? AppConstants.AppVersion
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    private UpdateState LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return new UpdateState();
            }
            return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(_statePath)) ?? new UpdateState();
        }
        catch
        {
            return new UpdateState();
        }
    }

    private void SaveState(UpdateState state)
    {
        try
        {
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state));
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)*)")]
    private static partial Regex VersionRegex();
}

public sealed record UpdateInfo(string Tag, string Url);

public sealed class UpdateState
{
    [JsonPropertyName("checked_at")]
    public DateTimeOffset? CheckedAt { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
