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
    private CdpClient? _client;
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
        CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await EnsureClientLockedAsync(cancellationToken).ConfigureAwait(false))
            {
                return AutomationOutcome.Error(
                    _fatal ?? _lastError ?? "Lỗi: chưa kết nối được Chrome.");
            }

            var client = _client!;
            var config = _config.Current;
            var selectors = config.Selectors;

            try
            {
                var status = await WaitForReadyStatusAsync(config, selectors, cancellationToken)
                    .ConfigureAwait(false);
                if (status is null)
                {
                    return AutomationOutcome.Error(
                        "Lỗi: Không tìm thấy ô nhập Gemini. Có thể chưa đăng nhập " +
                        "hoặc giao diện đã đổi (kiểm tra selectors.input).");
                }
                if (status.SignedOut)
                {
                    return AutomationOutcome.Error(
                        "Lỗi: Chưa đăng nhập Gemini. Hãy đăng nhập trong tab Chrome.");
                }
                if (!status.Input)
                {
                    return AutomationOutcome.Error(
                        "Lỗi: Không tìm thấy ô nhập Gemini. Có thể chưa đăng nhập " +
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
                    return AutomationOutcome.Error("Lỗi: Không tìm thấy ô nhập của Gemini.");
                }

                if (isImage)
                {
                    await InsertTextAsync(client, selectors.Input, config.PromptImage, cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    await PasteImageAsync(client, selectors, config, imagePng, cancellationToken)
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
                    "Kiểm tra selectors.response hoặc trạng thái Gemini.");
            }
            catch (OperationCanceledException)
            {
                await CancelAsync().ConfigureAwait(false);
                return AutomationOutcome.Cancelled;
            }
            catch (Exception exception)
            {
                AppLog.Error(exception, "Lỗi CDP khi tương tác");
                await ResetClientAsync().ConfigureAwait(false);
                return AutomationOutcome.Error($"Lỗi khi tương tác: {exception.Message}");
            }
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await EnsureClientLockedAsync(cancellationToken).ConfigureAwait(false))
            {
                return _fatal ?? _lastError ?? "Không kết nối được Chrome.";
            }

            var config = _config.Current;
            var status = await WaitForReadyStatusAsync(config, config.Selectors, cancellationToken)
                .ConfigureAwait(false);
            if (status is null)
            {
                return "Không tìm thấy ô nhập Gemini. Kiểm tra đăng nhập hoặc selectors.";
            }
            if (status.SignedOut)
            {
                return "Chưa đăng nhập Gemini. Hãy đăng nhập trong Chrome.";
            }
            if (!status.Input)
            {
                return "Không tìm thấy ô nhập Gemini. Kiểm tra đăng nhập hoặc selectors.";
            }
            return "Kết nối Gemini OK.";
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
        var client = _client;
        if (client is null || client.IsClosed)
        {
            return;
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

    private async Task<bool> EnsureClientLockedAsync(CancellationToken cancellationToken)
    {
        if (_fatal is not null)
        {
            return false;
        }

        if (_client is not null)
        {
            if (!_client.IsClosed && await _client.EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
            await ResetClientAsync().ConfigureAwait(false);
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
            _client = await CdpClient.ConnectAsync(
                    config.DebugPort,
                    "gemini.google.com",
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

    private async Task ResetClientAsync()
    {
        var client = _client;
        _client = null;
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<PageStatus?> WaitForReadyStatusAsync(
        AppConfig config,
        Selectors selectors,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(config.PageWait);
        while (DateTime.UtcNow < deadline)
        {
            var client = _client;
            if (client is null || client.IsClosed)
            {
                return null;
            }

            try
            {
                var result = await client.EvaluateAsync(
                        JavaScriptLibrary.Status(selectors.Input, selectors.Send),
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
                AppLog.Debug($"Trang Gemini chưa sẵn sàng: {exception.Message}");
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
        CancellationToken cancellationToken)
    {
        var imageWait = Math.Max(1, config.ImageWait);
        var baseline = await client.EvaluateAsync(
                JavaScriptLibrary.PasteBaseline(selectors.ImagePreview),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var initialPreview = baseline is { ValueKind: JsonValueKind.Object } ? GetInt(baseline.Value, "preview") : 0;
        var initialNodes = baseline is { ValueKind: JsonValueKind.Object } ? GetInt(baseline.Value, "nodes") : 0;

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

        if (imagePng is null)
        {
            return;
        }

        try
        {
            AppLog.Info("Ctrl+V không thấy ảnh, thử synthetic paste.");
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
