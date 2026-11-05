namespace GeminiQuickOverlay.Core.Cdp;

public sealed class CdpUnavailableException : Exception
{
    public CdpUnavailableException(string message, bool fatal = false) : base(message)
    {
        Fatal = fatal;
    }

    public bool Fatal { get; }
}

public sealed class CdpException : Exception
{
    public CdpException(string message) : base(message)
    {
    }
}
