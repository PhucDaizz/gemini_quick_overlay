import logging
import sys
from logging.handlers import RotatingFileHandler

from .paths import LOG_PATH

log = logging.getLogger("gemini_assistant")


def setup_logging():
    log.setLevel(logging.INFO)
    fmt = logging.Formatter("%(asctime)s [%(levelname)s] %(message)s")
    handlers = []
    try:
        file_handler = RotatingFileHandler(
            LOG_PATH,
            maxBytes=1_000_000,
            backupCount=3,
            encoding="utf-8",
            delay=True,
        )
        file_handler.setFormatter(fmt)
        handlers.append(file_handler)
    except Exception:
        pass
    if sys.stderr is not None:
        console = logging.StreamHandler()
        console.setFormatter(fmt)
        handlers.append(console)
    if handlers:
        log.handlers.clear()
        for handler in handlers:
            log.addHandler(handler)
        log.propagate = False
