namespace GeminiQuickOverlay.Core.Models;

/// <summary>
/// Everything the automation needs to drive one web chat: where it lives, how to find its input and
/// answer, and how it signs a user out. Selector values can be overridden per provider in config.json.
/// </summary>
public sealed class ProviderProfile
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Opened in a new tab when Chrome has none for this provider.</summary>
    public required string Url { get; init; }

    /// <summary>Substring of the tab URL that identifies this provider's tab.</summary>
    public required string TargetMatch { get; init; }

    /// <summary>Regex tested against the tab URL to detect the sign-in page.</summary>
    public required string SignedOutPattern { get; init; }

    /// <summary>Value reported in the API's "model" field.</summary>
    public required string ApiModelName { get; init; }

    /// <summary>"model" values (prefix match, case-insensitive) that select this provider.</summary>
    public required string[] ModelPrefixes { get; init; }

    public required Selectors Selectors { get; init; }

    public bool SupportsImage { get; init; }
}

public static class ProviderCatalog
{
    public const string DefaultId = "gemini";

    public static readonly ProviderProfile Gemini = new()
    {
        Id = "gemini",
        DisplayName = "Gemini",
        Url = "https://gemini.google.com/app",
        TargetMatch = "gemini.google.com",
        SignedOutPattern = @"accounts\.google\.com|servicelogin",
        ApiModelName = "gemini-web",
        ModelPrefixes = new[] { "gemini" },
        Selectors = new Selectors(),
        SupportsImage = true,
    };

    // Selectors come from the live page: a single <textarea> for the prompt, and the final answer
    // lives in .ds-assistant-message-main-content (the optional "thinking" block is a sibling, not a
    // child). Send is left empty on purpose so the automation presses Enter: the round send button
    // only has generated class names, and the same element turns into "stop" while streaming.
    public static readonly ProviderProfile DeepSeek = new()
    {
        Id = "deepseek",
        DisplayName = "DeepSeek",
        Url = "https://chat.deepseek.com/",
        TargetMatch = "chat.deepseek.com",
        SignedOutPattern = @"/sign[_-]?in|/login",
        ApiModelName = "deepseek-web",
        ModelPrefixes = new[] { "deepseek" },
        Selectors = new Selectors
        {
            Response = ".ds-markdown.ds-assistant-message-main-content",
            Input = "textarea",
            Send = "",
            ImagePreview = "",
        },
        SupportsImage = false,
    };

    public static IReadOnlyList<ProviderProfile> All { get; } = new[] { Gemini, DeepSeek };

    public static ProviderProfile ById(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Gemini;

    /// <summary>Picks a provider from an API "model" value; null when it matches none.</summary>
    public static ProviderProfile? ByModel(string? model)
    {
        var value = model?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return All.FirstOrDefault(p => p.ModelPrefixes.Any(prefix =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Built-in selectors for the provider with any per-provider overrides from config applied.</summary>
    public static Selectors EffectiveSelectors(ProviderProfile provider, AppConfig config)
    {
        // The long-standing top-level "selectors" block keeps meaning "Gemini's selectors".
        var effective = provider.Id == Gemini.Id ? config.Selectors.Clone() : provider.Selectors.Clone();
        if (config.ProviderSelectors is not null &&
            config.ProviderSelectors.TryGetValue(provider.Id, out var overrides))
        {
            foreach (var (key, value) in overrides)
            {
                switch (key.ToLowerInvariant())
                {
                    case "response" when !string.IsNullOrWhiteSpace(value):
                        effective.Response = value;
                        break;
                    case "input" when !string.IsNullOrWhiteSpace(value):
                        effective.Input = value;
                        break;
                    case "send" when value is not null:
                        effective.Send = value; // empty is meaningful: press Enter
                        break;
                    case "image_preview" when value is not null:
                        effective.ImagePreview = value;
                        break;
                }
            }
        }

        return effective;
    }
}
