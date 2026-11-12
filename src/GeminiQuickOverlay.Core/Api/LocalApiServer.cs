using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeminiQuickOverlay.Core.Automation;
using GeminiQuickOverlay.Core.Logging;

namespace GeminiQuickOverlay.Core.Api;

/// <summary>
/// Cổng nội bộ kiểu OpenAI (POST /v1/chat/completions) để app khác dùng Gemini trong Chrome, không cần API key.
/// Chỉ nghe trên 127.0.0.1, từ chối yêu cầu mang Origin (trang web lạ) hoặc Host lạ. Mỗi yêu cầu được gõ vào trang Gemini.
/// </summary>
public sealed class LocalApiServer : IDisposable
{
    public const int MaxBodyBytes = 64 * 1024;

    // Các câu báo lỗi của backend (không phải câu trả lời của Gemini) đã được GeminiAutomation đánh dấu bằng AutomationStatus.Error.
    private readonly Func<string, CancellationToken, Task<AutomationOutcome>> _ask;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public LocalApiServer(int port, Func<string, CancellationToken, Task<AutomationOutcome>> ask)
    {
        Port = port;
        _ask = ask;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public int Port { get; }

    /// <summary>Mở cổng. Ném lỗi nếu cổng đang bị chiếm.</summary>
    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(LoopAsync);
        AppLog.Info($"Cổng API đang nghe tại http://127.0.0.1:{Port}/v1");
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                return; // listener đã đóng
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var (status, payload) = await ProcessAsync(context.Request, _stop.Token).ConfigureAwait(false);
            var raw = Encoding.UTF8.GetBytes(payload.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = raw.Length;
            await context.Response.OutputStream.WriteAsync(raw).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Cổng API: {exception.Message}"); // bên gọi đã ngắt kết nối
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    /// <summary>Xử lý một yêu cầu. Tách riêng khỏi HttpListener để kiểm tra được.</summary>
    internal async Task<(int Status, JsonObject Body)> ProcessAsync(HttpListenerRequest request, CancellationToken token)
    {
        var host = request.Headers["Host"] ?? "";
        var hostName = host.Contains(':') ? host[..host.LastIndexOf(':')] : host;
        if ((hostName != "127.0.0.1" && hostName != "localhost") || request.Headers["Origin"] is not null)
        {
            return Error(403, "Forbidden");
        }
        if (request.HttpMethod != "POST" || request.Url?.AbsolutePath.TrimEnd('/') != "/v1/chat/completions")
        {
            return Error(404, "Not Found");
        }
        if (request.ContentLength64 <= 0 || request.ContentLength64 > MaxBodyBytes)
        {
            return Error(413, "Body rỗng hoặc quá lớn");
        }

        string body;
        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(token).ConfigureAwait(false);
        }
        return await AnswerAsync(body, token).ConfigureAwait(false);
    }

    internal async Task<(int Status, JsonObject Body)> AnswerAsync(string body, CancellationToken token)
    {
        var prompt = BuildPrompt(body);
        if (prompt is null)
        {
            return Error(400, "Body phải là JSON có messages");
        }

        var outcome = await _ask(prompt, token).ConfigureAwait(false);
        if (outcome.Status != AutomationStatus.Success || string.IsNullOrWhiteSpace(outcome.Text))
        {
            return Error(502, string.IsNullOrWhiteSpace(outcome.Text) ? "Gemini không trả lời" : outcome.Text);
        }
        return (200, new JsonObject
        {
            ["object"] = "chat.completion",
            ["model"] = "gemini-web",
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["finish_reason"] = "stop",
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = outcome.Text.Trim() },
            }),
        });
    }

    /// <summary>Ghép system + user thành MỘT dòng (xuống dòng trong ô nhập Gemini có thể bị hiểu là bấm gửi). Null nếu body sai.</summary>
    internal static string? BuildPrompt(string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
        if (root?["messages"] is not JsonArray messages)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var message in messages)
        {
            if (message?["content"] is JsonValue value && value.TryGetValue<string>(out var content) && !string.IsNullOrWhiteSpace(content))
            {
                parts.Add(string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
            }
        }
        return parts.Count == 0 ? null : string.Join(" ||| ", parts);
    }

    private static (int, JsonObject) Error(int status, string message) =>
        (status, new JsonObject { ["error"] = new JsonObject { ["message"] = message } });

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener.Close();
        }
        catch
        {
        }
        try
        {
            _loop?.Wait(1000);
        }
        catch
        {
        }
        _stop.Dispose();
    }
}
