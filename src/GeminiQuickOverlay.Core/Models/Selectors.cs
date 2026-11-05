namespace GeminiQuickOverlay.Core.Models;

public sealed class Selectors
{
    public string Response { get; set; } = ".model-response-text";

    public string Input { get; set; } = "div[contenteditable='true']";

    public string Send { get; set; } =
        "button:has(mat-icon[data-mat-icon-name='send']), " +
        "button[aria-label*='Send'], button[aria-label*='Gửi']";

    public string ImagePreview { get; set; } =
        "div[contenteditable='true'] img, div[contenteditable='true'] .image-preview";

    public Selectors Clone() => new()
    {
        Response = Response,
        Input = Input,
        Send = Send,
        ImagePreview = ImagePreview,
    };
}
