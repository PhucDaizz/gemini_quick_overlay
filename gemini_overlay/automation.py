import threading

from .backends import CDPAutomation, CDPUnavailable, SeleniumAutomation
from .config import get_config
from .logging_setup import log


class Automation:
    def __init__(self, config_provider=None):
        self._config_provider = config_provider or get_config
        self._backend = None
        self._kind = None
        self._fatal = None
        self._last_error = None
        self._lock = threading.Lock()

    def _cfg(self):
        return self._config_provider()

    def _ensure(self):
        with self._lock:
            if self._backend is not None:
                return True
            if self._fatal:
                return False
            port = int(self._cfg().get("debug_port", 9222))
            try:
                self._backend = CDPAutomation(port, config_provider=self._config_provider)
                self._kind = "cdp"
                return True
            except CDPUnavailable as e:
                if e.fatal:
                    self._fatal = str(e)
                    log.error("%s", e)
                    return False
                log.warning("CDP không khả dụng (%s), chuyển sang Selenium.", e)
            try:
                self._backend = SeleniumAutomation(port, config_provider=self._config_provider)
                self._kind = "selenium"
                return True
            except Exception as e:
                self._last_error = f"Không kết nối được Chrome: {e}"
                log.error("%s", self._last_error)
                return False

    def send_and_get_response(self, text, is_image=False, cancel_event=None, image=None):
        if not self._ensure():
            return self._fatal or self._last_error or "Lỗi: chưa kết nối được Chrome."
        backend = self._backend
        try:
            return backend.send_and_get_response(
                text, is_image=is_image, cancel_event=cancel_event, image=image
            )
        except Exception as e:
            log.exception("Backend lỗi")
            self._reset()
            return f"Lỗi: {e}"

    def cancel(self):
        backend = self._backend
        if backend is not None:
            try:
                backend.cancel()
            except Exception:
                pass

    def _reset(self):
        with self._lock:
            if self._backend is not None:
                try:
                    self._backend.close()
                except Exception:
                    pass
            self._backend = None
            self._kind = None

