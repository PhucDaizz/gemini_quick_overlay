using System.Text.Json;
using GeminiQuickOverlay.Core;

namespace GeminiQuickOverlay.Core.Cdp;

internal sealed class CdpHttpClient : IDisposable
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(2),
    };

    public async Task<JsonElement> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    public Task<JsonElement> GetVersionAsync(string netloc, CancellationToken cancellationToken) =>
        GetJsonAsync($"http://{netloc}/json/version", cancellationToken);

    public async Task<CdpTarget?> FindTargetAsync(
        string netloc,
        string targetMatch,
        CancellationToken cancellationToken)
    {
        var targets = await GetJsonAsync($"http://{netloc}/json", cancellationToken).ConfigureAwait(false);
        if (targets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var target in targets.EnumerateArray())
        {
            if (!target.TryGetProperty("type", out var type) ||
                type.GetString() != "page" ||
                !target.TryGetProperty("url", out var urlElement))
            {
                continue;
            }

            var url = urlElement.GetString() ?? "";
            if (!url.Contains(targetMatch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new CdpTarget(
                target.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                "page",
                url,
                target.TryGetProperty("webSocketDebuggerUrl", out var ws)
                    ? ws.GetString()
                    : null);
        }

        return null;
    }

    public async Task<CdpTarget?> CreateTargetAsync(
        string netloc,
        string targetMatch,
        CancellationToken cancellationToken)
    {
        var created = false;
        foreach (var url in new[]
                 {
                     $"http://{netloc}/json/new?{AppConstants.GeminiUrl}",
                     $"http://{netloc}/json/new?{Uri.EscapeDataString(AppConstants.GeminiUrl)}",
                 })
        {
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Get })
            {
                try
                {
                    using var request = new HttpRequestMessage(method, url);
                    using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        created = true;
                        break;
                    }
                }
                catch
                {
                }
            }

            if (created)
            {
                break;
            }
        }

        if (!created)
        {
            return null;
        }

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var target = await FindTargetAsync(netloc, targetMatch, cancellationToken).ConfigureAwait(false);
            if (target is not null)
            {
                return target;
            }
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}
