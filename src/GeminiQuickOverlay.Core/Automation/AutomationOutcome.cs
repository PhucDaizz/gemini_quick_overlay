namespace GeminiQuickOverlay.Core.Automation;

public enum AutomationStatus
{
    Success,
    Error,
    Cancelled,
}

public sealed record AutomationOutcome(AutomationStatus Status, string Text)
{
    public static AutomationOutcome Success(string text) => new(AutomationStatus.Success, text);

    public static AutomationOutcome Error(string text) => new(AutomationStatus.Error, text);

    public static AutomationOutcome Cancelled { get; } = new(AutomationStatus.Cancelled, "");
}
