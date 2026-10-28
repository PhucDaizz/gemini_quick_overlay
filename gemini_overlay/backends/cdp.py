import json
import threading
import time
from concurrent.futures import Future, TimeoutError as FutureTimeout
from threading import Thread
from urllib.parse import quote, urlsplit, urlunsplit

import requests
import websocket

from ..constants import GEMINI_URL
from ..logging_setup import log


class CDPUnavailable(Exception):
    def __init__(self, message, fatal=False):
        super().__init__(message)
        self.fatal = fatal


class CDPError(Exception):
    pass


class CDPClient:
    def __init__(self, port):
        self.port = port
        self.ws = None
        self.target = None
        self._netloc = None
        self._id = 0
        self._pending = {}
        self._lock = threading.Lock()
        self._closed = threading.Event()
        self._reader = None

    def _candidates(self):
        return (
            f"127.0.0.1:{self.port}",
            f"[::1]:{self.port}",
            f"localhost:{self.port}",
        )

    def _base_netloc(self):
        return self._netloc or f"127.0.0.1:{self.port}"

    def _localize_ws(self, url):
        try:
            parts = urlsplit(url)
            if (parts.hostname or "").lower() in ("localhost", "127.0.0.1", "::1") and parts.port == self.port:
                return urlunsplit((parts.scheme, self._netloc, parts.path, parts.query, parts.fragment))
        except Exception:
            pass
        return url

    def connect(self, target_match):
        errors = []
        electron = False
        for netloc in self._candidates():
            try:
                version = requests.get(f"http://{netloc}/json/version", timeout=1.5).json()
            except Exception as e:
                errors.append(f"{netloc}: {e}")
                continue

            identity = "{} {}".format(version.get("Browser", ""), version.get("User-Agent", ""))
            if "Electron" in identity or "Code/" in identity:
                electron = True
                errors.append(f"{netloc}: Electron")
                continue

            self._netloc = netloc
            log.debug("Dùng CDP tại %s (%s)", netloc, version.get("Browser", "?"))
            break

        if self._netloc is None:
            if electron:
                raise CDPUnavailable(
                    f"Cổng {self.port} đang bị ứng dụng Electron (VS Code...) chiếm. "
                    "Hãy đổi 'debug_port' trong config.json hoặc đóng ứng dụng đó.",
                    fatal=True,
                )
            detail = "; ".join(errors[-2:]) or "không có phản hồi"
            raise CDPUnavailable(f"Không kết nối được cổng debug {self.port}: {detail}")

        target = self._find_target(target_match)
        if target is None:
            target = self._open_target(target_match)
        if target is None:
            raise CDPUnavailable(f"Không tìm thấy tab chứa '{target_match}'.")

        self.target = target
        try:
            ws_url = self._localize_ws(target["webSocketDebuggerUrl"])
            self.ws = websocket.create_connection(
                ws_url, timeout=10, suppress_origin=True
            )
            self.ws.settimeout(None)
        except Exception as e:
            raise CDPUnavailable(f"Không mở được WebSocket CDP: {e}")

        self._reader = Thread(target=self._read_loop, daemon=True)
        self._reader.start()
        log.info("Đã kết nối CDP tới: %s", target.get("url", "?"))

    def _find_target(self, target_match):
        try:
            targets = requests.get(f"http://{self._base_netloc()}/json", timeout=2).json()
        except Exception:
            return None
        pages = [t for t in targets if t.get("type") == "page"]
        return next((t for t in pages if target_match in (t.get("url") or "")), None)

    def _open_target(self, target_match):
        created = False
        urls = (
            f"http://{self._base_netloc()}/json/new?{GEMINI_URL}",
            f"http://{self._base_netloc()}/json/new?{quote(GEMINI_URL, safe='')}",
        )
        for method in (requests.put, requests.get):
            for url in urls:
                try:
                    method(url, timeout=5)
                    created = True
                    break
                except Exception:
                    continue
            if created:
                break
        if not created:
            return None
        end = time.time() + 8
        while time.time() < end:
            target = self._find_target(target_match)
            if target is not None:
                return target
            time.sleep(0.3)
        return None

    def _read_loop(self):
        while not self._closed.is_set():
            try:
                data = json.loads(self.ws.recv())
            except Exception as e:
                if not self._closed.is_set():
                    log.debug("CDP reader dừng: %s", e)
                break
            msg_id = data.get("id")
            if msg_id is None:
                continue
            with self._lock:
                future = self._pending.pop(msg_id, None)
            if future and not future.done():
                future.set_result(data)
        self._closed.set()
        try:
            if self.ws is not None:
                self.ws.close()
        except Exception:
            pass
        with self._lock:
            pending = list(self._pending.values())
            self._pending.clear()
        for future in pending:
            if not future.done():
                future.set_exception(CDPError("Kết nối CDP đã đóng."))

    def send(self, method, params=None, timeout=30):
        if self._closed.is_set() or self.ws is None:
            raise CDPError("CDP chưa kết nối.")
        with self._lock:
            self._id += 1
            msg_id = self._id
            future = Future()
            self._pending[msg_id] = future
            try:
                self.ws.send(json.dumps({"id": msg_id, "method": method, "params": params or {}}))
            except Exception:
                self._pending.pop(msg_id, None)
                raise
        try:
            data = future.result(timeout=timeout)
        except FutureTimeout:
            with self._lock:
                self._pending.pop(msg_id, None)
            raise CDPError(f"{method} quá thời gian chờ.")
        if "error" in data:
            raise CDPError(data["error"].get("message", "CDP error"))
        return data.get("result", {})

    def eval(self, expression, await_promise=False, timeout=30):
        result = self.send(
            "Runtime.evaluate",
            {
                "expression": expression,
                "returnByValue": True,
                "awaitPromise": await_promise,
            },
            timeout=timeout,
        )
        if result.get("exceptionDetails"):
            details = result["exceptionDetails"]
            message = (
                details.get("exception", {}).get("description")
                or details.get("text")
                or "Lỗi JavaScript."
            )
            raise CDPError(message)
        return result.get("result", {}).get("value")

    @property
    def closed(self):
        return self._closed.is_set()

    def try_eval(self, expression, timeout=1.5):
        try:
            return self.eval(expression, timeout=timeout)
        except Exception:
            return None

    def close(self):
        if self._closed.is_set():
            return
        self._closed.set()
        try:
            if self.ws is not None:
                self.ws.close()
        except Exception:
            pass
        with self._lock:
            pending = list(self._pending.values())
            self._pending.clear()
        for future in pending:
            if not future.done():
                future.set_exception(CDPError("CDP đã đóng."))

