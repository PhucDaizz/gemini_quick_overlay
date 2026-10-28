APP_NAME = "AIHelper"
GEMINI_URL = "https://gemini.google.com/app"
CONFIG_FILENAME = "config.json"
LOG_FILENAME = "gemini_helper.log"
ICON_FILENAME = "AIHelper.ico"
MUTEX_NAME = "Local\\GeminiQuickOverlayAssistant"
CANCELLED = "__CANCELLED__"
HIDE = "__HIDE__"


DEFAULT_SELECTORS = {
    "response": ".model-response-text",
    "input": "div[contenteditable='true']",
    "send": (
        "button:has(mat-icon[data-mat-icon-name='send']), "
        "button[aria-label*='Send'], button[aria-label*='Gửi']"
    ),
    "image_preview": (
        "div[contenteditable='true'] img, div[contenteditable='true'] .image-preview"
    ),
}

DEFAULT_CONFIG = {
    "text_color": "black",
    "background_color": "#abcdef",
    "opacity": 0.5,
    "font_size": 11,
    "position_bottom_offset": 50,
    "position_left_offset": 20,
    "prompt_text": "chỉ trả lời đáp án không giải thích: ",
    "prompt_image": "hãy giải bài trong ảnh, chỉ trả lời đáp án ngắn gọn: ",
    "wrap_length": 800,
    "page_wait": 5.0,
    "response_timeout": 60,
    "display_seconds": 15,
    "image_wait": 10,
    "image_max_size": 1600,
    "poll_interval": 0.35,
    "settle_ms": 1500,
    "debug_port": 9222,
    "hotkey_send": "f2",
    "hotkey_replay": "f4",
    "hotkey_copy": "f3",
    "hotkey_hide": "esc",
    "show_pending": True,
    "selectors": DEFAULT_SELECTORS,
}
