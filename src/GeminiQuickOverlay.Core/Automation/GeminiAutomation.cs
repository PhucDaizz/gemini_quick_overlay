using System.Text.Json;
using GeminiQuickOverlay.Core.Cdp;
using GeminiQuickOverlay.Core.Chrome;
using GeminiQuickOverlay.Core.Config;
using GeminiQuickOverlay.Core.Logging;
using GeminiQuickOverlay.Core.Models;

namespace GeminiQuickOverlay.Core.Automation;

public sealed class GeminiAutomation : IAsyncDisposable
{
    private readonly ConfigService _config;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly Dictionary<string, CdpClient> _clients = new();
    private string? _fatal;
    private string? _lastError;

    public GeminiAutomation(ConfigService config)
    {
        _config = config;
    }

    public async Task<AutomationOutcome> SendAndGetResponseAsync(
        string text,
        bool isImage,
        byte[]? imagePng,
        CancellationToken cancellationToken = default,
        bool pasteWithShortcut = true,
        string? providerId = null)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = _config.Current;
            var provider = ProviderCatalog.ById(providerId ?? config.Provider);
            if (isImage && !provider.SupportsImage)
            {
                return AutomationOutcome.Error(
                    $"Lỗi: {provider.DisplayName} chưa hỗ trợ gửi ảnh. Hãy đổi nhà cung cấp sang Gemini trong Cài đặt.");
            }

            if (!await EnsureClientLockedAsync(provider, cancellationToken).ConfigureAwait(false))
            {
                return AutomationOutcome.Error(
                    _fatal ?? _lastError ?? "Lỗi: chưa kết nối được Chrome.");
            }

            var client = _clients[provider.Id];
            var selectors = ProviderCatalog.EffectiveSelectors(provider, config);

            try
            {
                var status = await WaitForReadyStatusAsync(provider, config, selectors, cancellationToken)
                    .ConfigureAwait(false);
                if (status is null)
                {
                    return AutomationOutcome.Error(
                        $"Lỗi: Không tìm thấy ô nhập {provider.DisplayName}. Có thể chưa đăng nhập " +
                        "hoặc giao diện đã đổi (kiểm tra selectors.input).");
                }
                if (status.SignedOut)
                {
                    return AutomationOutcome.Error(
                        $"Lỗi: Chưa đăng nhập {provider.DisplayName}. Hãy đăng nhập trong tab Chrome.");
                }
                if (!status.Input)
                {
                    return AutomationOutcome.Error(
                        $"Lỗi: Không tìm thấy ô nhập {provider.DisplayName}. Có thể chưa đăng nhập " +
                        "hoặc giao diện đã đổi (kiểm tra selectors.input).");
                }

                await client.EvaluateAsync(
                        "window.__geminiCancel=false;true",
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var (initialCount, initialText) =
                    await ProbeAsync(client, selectors.Response, cancellationToken).ConfigureAwait(false);
                if (initialCount > 0 && string.IsNullOrWhiteSpace(initialText))
                {
                    for (var index = 0; index < 10; index++)
                    {
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                        (initialCount, initialText) =
                            await ProbeAsync(client, selectors.Response, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(initialText))
                        {
                            break;
                        }
                    }
                }

                var focused = await EvaluateBoolAsync(
                        client,
                        JavaScriptLibrary.Focus(selectors.Input),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!focused)
                {
                    return AutomationOutcome.Error($"Lỗi: Không tìm thấy ô nhập của {provider.DisplayName}.");
                }

                if (isImage)
                {
                    await InsertTextAsync(client, selectors.Input, config.PromptImage, cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    await PasteImageAsync(client, selectors, config, imagePng, pasteWithShortcut, cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await InsertTextAsync(client, selectors.Input, text, cancellationToken)
                        .ConfigureAwait(false);
                }

                var sendClicked = false;
                if (status.Send)
                {
                    await WaitForSendReadyAsync(client, selectors.Send, cancellationToken)
                        .ConfigureAwait(false);
                    sendClicked = await EvaluateBoolAsync(
                            client,
                            JavaScriptLibrary.Send(selectors.Send),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!sendClicked)
                {
                    await client.DispatchKeyEventAsync(
                            "rawKeyDown",
                            "Enter",
                            "Enter",
                            13,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    await client.DispatchKeyEventAsync(
                            "keyUp",
                            "Enter",
                            "Enter",
                            13,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                AppLog.Info("Đang chờ phản hồi mới (CDP)...");
                var observer = JavaScriptLibrary.Observer(
                    selectors.Response,
                    initialCount,
                    initialText,
                    config.SettleMs,
                    config.ResponseTimeout * 1000);
                var result = await client.EvaluateAsync(
                        observer,
                        awaitPromise: true,
                        cancellationToken: cancellationToken,
                        timeout: TimeSpan.FromSeconds(config.ResponseTimeout + 10))
                    .ConfigureAwait(false);

                if (result is null || result.Value.ValueKind != JsonValueKind.Object)
                {
                    return AutomationOutcome.Error("Không lấy được phản hồi mới (kết quả rỗng).");
                }

                var responseElement = result.Value;
                var responseStatus = GetString(responseElement, "status");
                if (responseStatus == "ok")
                {
                    return AutomationOutcome.Success(GetString(responseElement, "text").Trim());
                }
                if (responseStatus == "cancelled")
                {
                    return AutomationOutcome.Cancelled;
                }

                return AutomationOutcome.Error(
                    "Không lấy được phản hồi mới (hết thời gian). " +
                    $"Kiểm tra selectors.response hoặc trạng thái {provider.DisplayName}.");
            }
            catch (OperationCanceledException)
            {
                await CancelAsync().ConfigureAwait(false);
                return AutomationOutcome.Cancelled;
            }
            catch (Exception exception)
            {
                AppLog.Error(exception, "Lỗi CDP khi tương tác");
                await ResetClientAsync(provider.Id).ConfigureAwait(false);
                return AutomationOutcome.Error($"Lỗi khi tương tác: {exception.Message}");
            }
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<string> TestConnectionAsync(
        CancellationToken cancellationToken = default,
        string? providerId = null)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = _config.Current;
            var provider = ProviderCatalog.ById(providerId ?? config.Provider);
            if (!await EnsureClientLockedAsync(provider, cancellationToken).ConfigureAwait(false))
            {
                return _fatal ?? _lastError ?? "Không kết nối được Chrome.";
            }

            var status = await WaitForReadyStatusAsync(
                    provider,
                    config,
                    ProviderCatalog.EffectiveSelectors(provider, config),
                    cancellationToken)
                .ConfigureAwait(false);
            if (status is null)
            {
                return $"Không tìm thấy ô nhập {provider.DisplayName}. Kiểm tra đăng nhập hoặc selectors.";
            }
            if (status.SignedOut)
            {
                return $"Chưa đăng nhập {provider.DisplayName}. Hãy đăng nhập trong Chrome.";
            }
            if (!status.Input)
            {
                return $"Không tìm thấy ô nhập {provider.DisplayName}. Kiểm tra đăng nhập hoặc selectors.";
            }
            return $"Kết nối {provider.DisplayName} OK.";
        }
        catch (Exception exception)
        {
            return $"Lỗi: {exception.Message}";
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task CancelAsync()
    {
        foreach (var client in _clients.Values.ToArray())
        {
            if (client.IsClosed)
            {
                continue;
            }

            try
            {
                await client.EvaluateAsync(
                        "window.__geminiCancel = true;",
                        cancellationToken: CancellationToken.None,
                        timeout: TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            await ResetClientAsync().ConfigureAwait(false);
        }
        finally
        {
            _operation.Release();
            _operation.Dispose();
        }
    }

    private async Task<bool> EnsureClientLockedAsync(ProviderProfile provider, CancellationToken cancellationToken)
    {
        if (_fatal is not null)
        {
            return false;
        }

        if (_clients.TryGetValue(provider.Id, out var existing))
        {
            if (!existing.IsClosed && await existing.EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
            await ResetClientAsync(provider.Id).ConfigureAwait(false);
        }

        var config = _config.Current;
        var chrome = await ChromeLauncher.EnsureChromeAsync(config, cancellationToken).ConfigureAwait(false);
        if (!chrome.Ok)
        {
            _lastError = chrome.Error;
            AppLog.Warn(chrome.Error ?? "Không khởi động được Chrome.");
            return false;
        }

        try
        {
            _clients[provider.Id] = await CdpClient.ConnectAsync(
                    config.DebugPort,
                    provider.TargetMatch,
                    provider.Url,
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (CdpUnavailableException exception)
        {
            _lastError = exception.Message;
            if (exception.Fatal)
            {
                _fatal = exception.Message;
            }
            AppLog.Warn(exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            _lastError = $"Không kết nối được Chrome: {exception.Message}";
            AppLog.Error(exception, _lastError);
            return false;
        }
    }

    /// <summary>Closes one provider's connection, or all of them when no id is given.</summary>
    private async Task ResetClientAsync(string? providerId = null)
    {
        var ids = providerId is null ? _clients.Keys.ToArray() : new[] { providerId };
        foreach (var id in ids)
        {
            if (_clients.Remove(id, out var client))
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<PageStatus?> WaitForReadyStatusAsync(
        ProviderProfile provider,
        AppConfig config,
        Selectors selectors,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(config.PageWait);
        while (DateTime.UtcNow < deadline)
        {
            if (!_clients.TryGetValue(provider.Id, out var client) || client.IsClosed)
            {
                return null;
            }

            try
            {
                var result = await client.EvaluateAsync(
                        JavaScriptLibrary.Status(selectors.Input, selectors.Send, provider.SignedOutPattern),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (result is not null && result.Value.ValueKind == JsonValueKind.Object)
                {
                    var statusElement = result.Value;
                    var status = new PageStatus(
                        GetBool(statusElement, "signedOut"),
                        GetBool(statusElement, "input"),
                        GetBool(statusElement, "send"),
                        GetString(statusElement, "url"));
                    if (status.Input || status.SignedOut)
                    {
                        return status;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AppLog.Debug($"Trang {provider.DisplayName} chưa sẵn sàng: {exception.Message}");
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task InsertTextAsync(
        CdpClient client,
        string selector,
        string text,
        CancellationToken cancellationToken)
    {
        await client.InsertTextAsync(text, cancellationToken).ConfigureAwait(false);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        var current = await client.EvaluateAsync(
                JavaScriptLibrary.InputText(selector),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(AsString(current)))
        {
            await client.EvaluateAsync(
                    JavaScriptLibrary.InsertFallback(selector, text),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task WaitForSendReadyAsync(
        CdpClient client,
        string selector,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (await EvaluateBoolAsync(client, JavaScriptLibrary.SendReady(selector), cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    // A pasted image adds a thumbnail component (several nodes); anything smaller is just noise.
    private const int MinNodesForPaste = 3;

    private async Task PasteImageAsync(
        CdpClient client,
        Selectors selectors,
        AppConfig config,
        byte[]? imagePng,
        bool pasteWithShortcut,
        CancellationToken cancellationToken)
    {
        var imageWait = Math.Max(1, config.ImageWait);
        var baseline = await client.EvaluateAsync(
                JavaScriptLibrary.PasteBaseline(selectors.ImagePreview),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var initialPreview = baseline is { ValueKind: JsonValueKind.Object } ? GetInt(baseline.Value, "preview") : 0;
        var initialNodes = baseline is { ValueKind: JsonValueKind.Object } ? GetInt(baseline.Value, "nodes") : 0;

        // A region capture is not on the system clipboard, so pasting it directly skips the Ctrl+V attempt.
        if (pasteWithShortcut)
        {
            await client.DispatchKeyEventAsync(
                    "rawKeyDown",
                    "v",
                    "KeyV",
                    86,
                    2,
                    cancellationToken)
                .ConfigureAwait(false);
            await client.DispatchKeyEventAsync(
                    "keyUp",
                    "v",
                    "KeyV",
                    86,
                    2,
                    cancellationToken)
                .ConfigureAwait(false);

            var wait = Math.Min(4, imageWait);
            var preview = await client.EvaluateAsync(
                    JavaScriptLibrary.WaitPaste(selectors.ImagePreview, initialPreview, initialNodes, MinNodesForPaste, wait * 1000),
                    awaitPromise: true,
                    cancellationToken: cancellationToken,
                    timeout: TimeSpan.FromSeconds(wait + 5))
                .ConfigureAwait(false);
            if (preview is { ValueKind: JsonValueKind.True })
            {
                return;
            }
        }

        if (imagePng is null)
        {
            return;
        }

        try
        {
            AppLog.Info(pasteWithShortcut
                ? "Ctrl+V không thấy ảnh, thử synthetic paste."
                : "Dán ảnh chụp vùng bằng synthetic paste.");
            var base64 = Convert.ToBase64String(imagePng);
            await client.EvaluateAsync(
                    JavaScriptLibrary.SyntheticPaste(selectors.Input, base64),
                    cancellationToken: cancellationToken,
                    timeout: TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            var syntheticWait = Math.Min(5, imageWait);
            var syntheticPreview = await client.EvaluateAsync(
                    JavaScriptLibrary.WaitPaste(selectors.ImagePreview, initialPreview, initialNodes, MinNodesForPaste, syntheticWait * 1000),
                    awaitPromise: true,
                    cancellationToken: cancellationToken,
                    timeout: TimeSpan.FromSeconds(syntheticWait + 5))
                .ConfigureAwait(false);
            if (syntheticPreview is { ValueKind: JsonValueKind.True })
            {
                return;
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Synthetic paste lỗi: {exception.Message}");
        }

        AppLog.Warn("Không xác nhận được thumbnail ảnh, vẫn thử gửi.");
    }

    private static async Task<bool> EvaluateBoolAsync(
        CdpClient client,
        string expression,
        CancellationToken cancellationToken,
        bool awaitPromise = false)
    {
        var result = await client.EvaluateAsync(
                expression,
                awaitPromise,
                cancellationToken)
            .ConfigureAwait(false);
        return result is { ValueKind: JsonValueKind.True };
    }

    private static async Task<(int Count, string Text)> ProbeAsync(
        CdpClient client,
        string selector,
        CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync(
                JavaScriptLibrary.Probe(selector),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is null || result.Value.ValueKind != JsonValueKind.Object)
        {
            return (0, "");
        }
        return (GetInt(result.Value, "count"), GetString(result.Value, "text"));
    }

    private static string AsString(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } ? element.Value.GetString() ?? "" : "";

    private static int AsInt(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Number } && element.Value.TryGetInt32(out var value)
            ? value
            : 0;

    private static bool GetBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private sealed record PageStatus(bool SignedOut, bool Input, bool Send, string Url);
}
