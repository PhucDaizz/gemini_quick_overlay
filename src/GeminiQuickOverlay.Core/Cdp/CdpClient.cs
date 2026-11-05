using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GeminiQuickOverlay.Core.Logging;

namespace GeminiQuickOverlay.Core.Cdp;

public sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _reader;
    private int _nextId;
    private int _closed;

    private CdpClient(ClientWebSocket socket, string netloc, CdpTarget target)
    {
        _socket = socket;
        Netloc = netloc;
        Target = target;
        _reader = Task.Run(() => ReadLoopAsync(_lifetime.Token));
    }

    public string Netloc { get; }

    public CdpTarget Target { get; }

    public bool IsClosed
    {
        get
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                return true;
            }

            try
            {
                return _socket.State != WebSocketState.Open;
            }
            catch
            {
                return true;
            }
        }
    }

    public static async Task<CdpClient> ConnectAsync(
        int port,
        string targetMatch = "gemini.google.com",
        CancellationToken cancellationToken = default)
    {
        using var http = new CdpHttpClient();
        var errors = new List<string>();
        var electron = false;
        string? netloc = null;

        foreach (var candidate in new[]
                 {
                     $"127.0.0.1:{port}",
                     $"[::1]:{port}",
                     $"localhost:{port}",
                 })
        {
            try
            {
                var version = await http.GetVersionAsync(candidate, cancellationToken).ConfigureAwait(false);
                var browser = version.TryGetProperty("Browser", out var browserElement)
                    ? browserElement.GetString() ?? ""
                    : "";
                var userAgent = version.TryGetProperty("User-Agent", out var agentElement)
                    ? agentElement.GetString() ?? ""
                    : "";
                var identity = $"{browser} {userAgent}";
                if (identity.Contains("Electron", StringComparison.OrdinalIgnoreCase) ||
                    identity.Contains("Code/", StringComparison.OrdinalIgnoreCase))
                {
                    electron = true;
                    errors.Add($"{candidate}: Electron");
                    continue;
                }

                netloc = candidate;
                AppLog.Debug($"Dùng CDP tại {candidate} ({browser}).");
                break;
            }
            catch (Exception exception)
            {
                errors.Add($"{candidate}: {exception.Message}");
            }
        }

        if (netloc is null)
        {
            if (electron)
            {
                throw new CdpUnavailableException(
                    $"Cổng {port} đang bị ứng dụng Electron (VS Code...) chiếm. " +
                    "Hãy đổi debug_port trong Cài đặt hoặc đóng ứng dụng đó.",
                    fatal: true);
            }

            var detail = errors.Count == 0
                ? "không có phản hồi"
                : string.Join("; ", errors.TakeLast(2));
            throw new CdpUnavailableException($"Không kết nối được cổng debug {port}: {detail}");
        }

        var target = await http.FindTargetAsync(netloc, targetMatch, cancellationToken).ConfigureAwait(false);
        target ??= await http.CreateTargetAsync(netloc, targetMatch, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            throw new CdpUnavailableException($"Không tìm thấy tab chứa '{targetMatch}'.");
        }

        if (string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
        {
            throw new CdpUnavailableException("Tab Gemini không có WebSocket debugger URL.");
        }

        var socket = new ClientWebSocket();
        try
        {
            var websocketUrl = LocalizeWebSocket(target.WebSocketDebuggerUrl, netloc);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(new Uri(websocketUrl), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            socket.Dispose();
            throw new CdpUnavailableException($"Không mở được WebSocket CDP: {exception.Message}");
        }

        AppLog.Info($"Đã kết nối CDP tới: {target.Url}");
        return new CdpClient(socket, netloc, target);
    }

    public async Task<JsonElement?> EvaluateAsync(
        string expression,
        bool awaitPromise = false,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = awaitPromise,
        };
        var result = await SendCommandAsync(
                "Runtime.evaluate",
                parameters,
                timeout ?? TimeSpan.FromSeconds(30),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.TryGetProperty("exceptionDetails", out var exceptionDetails))
        {
            var description = exceptionDetails.TryGetProperty("exception", out var exception) &&
                              exception.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString()
                : exceptionDetails.TryGetProperty("text", out var textElement)
                    ? textElement.GetString()
                    : null;
            throw new CdpException(description ?? "Lỗi JavaScript.");
        }

        return result.TryGetProperty("result", out var inner) &&
               inner.TryGetProperty("value", out var value)
            ? value.Clone()
            : null;
    }

    public async Task<JsonElement?> TryEvaluateAsync(
        string expression,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        try
        {
            return await EvaluateAsync(expression, cancellationToken: cancellationToken, timeout: timeout)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!IsClosed)
        {
            var value = await TryEvaluateAsync(
                    "1",
                    cancellationToken,
                    TimeSpan.FromSeconds(1.5))
                .ConfigureAwait(false);
            if (value is not null)
            {
                return true;
            }
        }

        await CloseAsync().ConfigureAwait(false);
        return false;
    }

    public Task InsertTextAsync(string text, CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            "Input.insertText",
            new Dictionary<string, object?> { ["text"] = text },
            TimeSpan.FromSeconds(10),
            cancellationToken);

    public Task DispatchKeyEventAsync(
        string type,
        string key,
        string code,
        int virtualKeyCode,
        int modifiers = 0,
        CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            "Input.dispatchKeyEvent",
            new Dictionary<string, object?>
            {
                ["type"] = type,
                ["key"] = key,
                ["code"] = code,
                ["windowsVirtualKeyCode"] = virtualKeyCode,
                ["nativeVirtualKeyCode"] = virtualKeyCode,
                ["modifiers"] = modifiers,
            },
            TimeSpan.FromSeconds(5),
            cancellationToken);

    public async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", closeTimeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            _socket.Dispose();
            FailPending(new CdpException("CDP đã đóng."));
            try
            {
                await _reader.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _lifetime.Dispose();
    }

    private async Task<JsonElement> SendCommandAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (IsClosed)
        {
            throw new CdpException("CDP chưa kết nối.");
        }

        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var payload = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new Dictionary<string, object?>(),
        };

        try
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            JsonElement result;
            try
            {
                result = await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new CdpException($"{method} quá thời gian chờ.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }

            if (result.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new CdpException(message ?? "CDP error");
            }

            return result.TryGetProperty("result", out var resultElement)
                ? resultElement.Clone()
                : default;
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        AppLog.Debug($"CDP reader dừng: {exception.Message}");
                    }
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                HandleMessage(json);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _closed, 1);
            FailPending(new CdpException("Kết nối CDP đã đóng."));
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id))
            {
                return;
            }

            if (_pending.TryRemove(id, out var completion) && !completion.Task.IsCompleted)
            {
                completion.TrySetResult(root.Clone());
            }
        }
        catch (Exception exception)
        {
            AppLog.Debug($"Bỏ qua CDP message lỗi: {exception.Message}");
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var entry in _pending)
        {
            if (_pending.TryRemove(entry.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private static string LocalizeWebSocket(string websocketUrl, string netloc)
    {
        if (!Uri.TryCreate(websocketUrl, UriKind.Absolute, out var uri))
        {
            return websocketUrl;
        }

        if ((uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host == "127.0.0.1" ||
             uri.Host == "::1") &&
            uri.Port > 0)
        {
            return $"{uri.Scheme}://{netloc}{uri.PathAndQuery}";
        }

        return websocketUrl;
    }
}
