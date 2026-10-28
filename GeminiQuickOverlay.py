from gemini_overlay.app import main
from gemini_overlay.automation import Automation
from gemini_overlay.backends import (
    CDPAutomation,
    CDPClient,
    CDPError,
    CDPUnavailable,
    SeleniumAutomation,
)
from gemini_overlay.config import get_config, read_config
from gemini_overlay.constants import (
    CANCELLED,
    DEFAULT_CONFIG,
    DEFAULT_SELECTORS,
    GEMINI_URL,
    HIDE,
)
from gemini_overlay.events import msg_queue
from gemini_overlay.logging_setup import log, setup_logging
from gemini_overlay.overlay import OverlayWindow, run_overlay
from gemini_overlay.paths import APP_DIR, CONFIG_PATH, LOG_PATH
from gemini_overlay.win_utils import acquire_single_instance, enable_dpi_awareness

application_path = APP_DIR
CONFIG_FILE_PATH = CONFIG_PATH

__all__ = [
    "APP_DIR",
    "Automation",
    "CANCELLED",
    "CDPAutomation",
    "CDPClient",
    "CDPError",
    "CDPUnavailable",
    "CONFIG_PATH",
    "DEFAULT_CONFIG",
    "DEFAULT_SELECTORS",
    "GEMINI_URL",
    "HIDE",
    "LOG_PATH",
    "OverlayWindow",
    "SeleniumAutomation",
    "acquire_single_instance",
    "application_path",
    "CONFIG_FILE_PATH",
    "enable_dpi_awareness",
    "get_config",
    "log",
    "main",
    "msg_queue",
    "read_config",
    "run_overlay",
    "setup_logging",
]

if __name__ == "__main__":
    main()
