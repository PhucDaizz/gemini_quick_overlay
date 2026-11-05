namespace GeminiQuickOverlay.Core.Cdp;

public sealed record CdpTarget(
    string Id,
    string Type,
    string Url,
    string? WebSocketDebuggerUrl);
