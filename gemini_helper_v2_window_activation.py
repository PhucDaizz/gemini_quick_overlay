import base64
import ctypes
import io
import json
import logging
import os
import queue
import sys
import threading
import time
from concurrent.futures import Future, TimeoutError as FutureTimeout
from logging.handlers import RotatingFileHandler
from urllib.parse import quote, urlsplit, urlunsplit

import keyboard
import pyperclip
import requests
import websocket
from PIL import Image, ImageGrab
import tkinter as tk
from threading import Thread

# --- CẤU HÌNH ---
GEMINI_URL = "https://gemini.google.com/app"
CONFIG_FILENAME = "config.json"
LOG_FILENAME = "gemini_helper.log"
MUTEX_NAME = "Local\\GeminiQuickOverlayAssistant"
CANCELLED = "__CANCELLED__"
HIDE = "__HIDE__"

if getattr(sys, 'frozen', False):
    application_path = os.path.dirname(sys.executable)
else:
    application_path = os.path.dirname(os.path.abspath(__file__))

CONFIG_FILE_PATH = os.path.join(application_path, CONFIG_FILENAME)

log = logging.getLogger("gemini_assistant")


def setup_logging():
    log.setLevel(logging.INFO)
    fmt = logging.Formatter("%(asctime)s [%(levelname)s] %(message)s")
    handlers = []
    try:
        file_handler = RotatingFileHandler(
            os.path.join(application_path, LOG_FILENAME),
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
    "response_timeout": 60,
    "display_seconds": 15,
    "image_wait": 10,
    "image_max_size": 1600,
    "poll_interval": 0.35,
    "settle_ms": 1500,
    "debug_port": 9222,
    "selectors": DEFAULT_SELECTORS,
}


def read_config(path):
    cfg = DEFAULT_CONFIG.copy()
    selectors = dict(DEFAULT_SELECTORS)
    mtime = None
    if not os.path.exists(path):
        cfg["selectors"] = selectors
        log.warning("Không tìm thấy config tại %s, dùng mặc định.", path)
        return cfg, mtime
    try:
        mtime = os.path.getmtime(path)
        with open(path, "r", encoding="utf-8") as f:
            user_config = json.load(f)
        if isinstance(user_config, dict):
            cfg.update(user_config)
            user_selectors = user_config.get("selectors")
            if isinstance(user_selectors, dict):
                for key, value in user_selectors.items():
                    if isinstance(value, str) and value.strip():
                        selectors[key] = value
        cfg["selectors"] = selectors
        log.info("Đã load config từ: %s", path)
    except Exception as e:
        cfg["selectors"] = selectors
        log.error("Lỗi đọc config: %s", e)
    return cfg, mtime


setup_logging()
config, _config_mtime = read_config(CONFIG_FILE_PATH)
_config_lock = threading.Lock()


def get_config():
    global config, _config_mtime
    try:
        mtime = os.path.getmtime(CONFIG_FILE_PATH)
    except OSError:
        return config
    if mtime == _config_mtime:
        return config
    with _config_lock:
        if mtime != _config_mtime:
            new_config, _config_mtime = read_config(CONFIG_FILE_PATH)
            config = new_config
    return config


def enable_dpi_awareness():
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except Exception:
        try:
            ctypes.windll.user32.SetProcessDPIAware()
        except Exception:
            pass


def acquire_single_instance():
    try:
        kernel32 = ctypes.windll.kernel32
        kernel32.CreateMutexW.restype = ctypes.c_void_p
        kernel32.CreateMutexW.argtypes = (ctypes.c_void_p, ctypes.c_bool, ctypes.c_wchar_p)
        kernel32.CloseHandle.argtypes = (ctypes.c_void_p,)
        handle = kernel32.CreateMutexW(None, False, MUTEX_NAME)
        if not handle:
            return True
        if kernel32.GetLastError() == 183:
            kernel32.CloseHandle(handle)
            return None
        return handle
    except Exception:
        return True


msg_queue = queue.Queue()


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


class CDPAutomation:
    def __init__(self, port=None, target_match="gemini.google.com", config_override=None):
        self.config_override = config_override
        self._port = port or int(get_config().get("debug_port", 9222))
        self._target_match = target_match
        self.client = None
        self._cancel_lock = threading.Lock()
        self._ensure_client()

    def _ensure_client(self):
        client = self.client
        if client is not None and not client.closed:
            return
        client = CDPClient(self._port)
        client.connect(self._target_match)
        self.client = client

    def _cfg(self):
        return self.config_override or get_config()

    def close(self):
        client = self.client
        if client is not None:
            client.close()

    def cancel(self):
        with self._cancel_lock:
            client = self.client
        if client is not None and not client.closed:
            client.try_eval("window.__geminiCancel = true;")

    @staticmethod
    def _probe_js(selector):
        return (
            "(function(){const n=document.querySelectorAll(%s);"
            "if(!n.length)return{count:0,text:''};"
            "const e=n[n.length-1];"
            "return{count:n.length,text:(e.innerText||'').trim()};})()"
        ) % json.dumps(selector)

    @staticmethod
    def _focus_js(selector):
        return (
            "(function(){const e=document.querySelector(%s);if(!e)return false;"
            "e.focus();const s=window.getSelection();const r=document.createRange();"
            "r.selectNodeContents(e);s.removeAllRanges();s.addRange(r);return true;})()"
        ) % json.dumps(selector)

    @staticmethod
    def _input_text_js(selector):
        return (
            "(function(){const e=document.querySelector(%s);"
            "return e?(e.innerText||''):'';})()"
        ) % json.dumps(selector)

    @staticmethod
    def _insert_fallback_js(selector, text):
        return (
            "(function(){const e=document.querySelector(%s);if(!e)return false;"
            "e.focus();document.execCommand('insertText',false,%s);return true;})()"
        ) % (json.dumps(selector), json.dumps(text))

    @staticmethod
    def _send_js(selector):
        return (
            "(function(){const b=document.querySelector(%s);"
            "if(b&&!b.disabled){b.click();return true;}return false;})()"
        ) % json.dumps(selector)

    @staticmethod
    def _send_ready_js(selector):
        return (
            "(function(){const b=document.querySelector(%s);"
            "return !!(b&&!b.disabled);})()"
        ) % json.dumps(selector)

    @staticmethod
    def _preview_count_js(selector):
        return "document.querySelectorAll(%s).length" % json.dumps(selector)

    @staticmethod
    def _wait_preview_js(selector, initial_count, timeout_ms):
        return (
            "new Promise((resolve)=>{const end=Date.now()+%d;"
            "const id=setInterval(()=>{"
            "if(document.querySelectorAll(%s).length>%d){clearInterval(id);resolve(true);}"
            "else if(Date.now()>end){clearInterval(id);resolve(false);}},200);})"
        ) % (int(timeout_ms), json.dumps(selector), int(initial_count))

    @staticmethod
    def _synthetic_paste_js(selector, b64):
        return (
            "(function(){const e=document.querySelector(%s);if(!e)return false;"
            "const raw=atob(%s);const bytes=new Uint8Array(raw.length);"
            "for(let i=0;i<raw.length;i++)bytes[i]=raw.charCodeAt(i);"
            "const f=new File([bytes],'clipboard.png',{type:'image/png'});"
            "const dt=new DataTransfer();dt.items.add(f);"
            "e.focus();"
            "e.dispatchEvent(new ClipboardEvent('paste',{bubbles:true,cancelable:true,clipboardData:dt}));"
            "return true;})()"
        ) % (json.dumps(selector), json.dumps(b64))

    @staticmethod
    def _observer_js(response_selector, initial_count, initial_text, settle_ms, timeout_ms):
        cfg = json.dumps({
            "response": response_selector,
            "initialCount": int(initial_count),
            "initialText": initial_text,
            "settleMs": int(settle_ms),
            "timeoutMs": int(timeout_ms),
            "tickMs": 150,
        })
        return """
        new Promise((resolve)=>{
          if(window.__geminiObserver){try{window.__geminiObserver.disconnect();}catch(e){}}
          if(window.__geminiTimer){clearInterval(window.__geminiTimer);}
          const cfg=%s;
          const started=Date.now();
          let lastText='';
          let stableSince=0;
          let done=false;
          let observer=null;
          function current(){
            const nodes=document.querySelectorAll(cfg.response);
            if(!nodes.length)return{count:0,text:''};
            const el=nodes[nodes.length-1];
            return{count:nodes.length,text:(el.innerText||'').trim()};
          }
          function finish(payload){
            if(done)return;
            done=true;
            try{if(observer)observer.disconnect();}catch(e){}
            if(window.__geminiTimer){clearInterval(window.__geminiTimer);window.__geminiTimer=null;}
            window.__geminiObserver=null;
            resolve(payload);
          }
          function tick(){
            if(done)return;
            if(window.__geminiCancel)return finish({status:'cancelled',count:0,text:''});
            const cur=current();
            const isNew=cur.count>cfg.initialCount||(cur.text&&cur.text!==cfg.initialText);
            if(isNew&&cur.text){
              if(cur.text!==lastText){
                lastText=cur.text;
                stableSince=Date.now();
              }else if(Date.now()-stableSince>=cfg.settleMs){
                return finish({status:'ok',count:cur.count,text:cur.text});
              }
            }
            if(Date.now()-started>cfg.timeoutMs){
              return finish({status:'timeout',count:cur.count,text:cur.text});
            }
          }
          let scheduled=false;
          function schedule(){
            if(scheduled)return;
            scheduled=true;
            setTimeout(()=>{scheduled=false;tick();},100);
          }
          observer=new MutationObserver(schedule);
          observer.observe(document.body,{subtree:true,childList:true,characterData:true});
          window.__geminiObserver=observer;
          window.__geminiTimer=setInterval(tick,250);
          tick();
        })
        """ % cfg

    def _probe(self, selector):
        result = self.client.eval(self._probe_js(selector), timeout=5)
        if isinstance(result, dict):
            return int(result.get("count") or 0), (result.get("text") or "").strip()
        return 0, ""

    def _insert_text(self, selector, text):
        self.client.send("Input.insertText", {"text": text}, timeout=10)
        time.sleep(0.05)
        current = self.client.eval(self._input_text_js(selector), timeout=5)
        if (current or "").strip():
            return
        self.client.eval(self._insert_fallback_js(selector, text), timeout=10)

    def _wait_send_ready(self, selector, timeout):
        end = time.time() + timeout
        while time.time() < end:
            if self.client.eval(self._send_ready_js(selector), timeout=3):
                return True
            time.sleep(0.1)
        return False

    def _send_ctrl_v(self):
        base = {
            "key": "v",
            "code": "KeyV",
            "windowsVirtualKeyCode": 86,
            "nativeVirtualKeyCode": 86,
            "modifiers": 2,
        }
        self.client.send("Input.dispatchKeyEvent", dict(base, type="rawKeyDown"), timeout=5)
        self.client.send("Input.dispatchKeyEvent", dict(base, type="keyUp"), timeout=5)

    def _wait_preview(self, selector, initial_count, timeout_sec):
        return bool(
            self.client.eval(
                self._wait_preview_js(selector, initial_count, timeout_sec * 1000),
                await_promise=True,
                timeout=timeout_sec + 10,
            )
        )

    def _encode_image(self, image, max_side):
        if image.mode not in ("RGB", "RGBA"):
            image = image.convert("RGB")
        width, height = image.size
        largest = max(width, height)
        if largest > max_side:
            scale = max_side / float(largest)
            image = image.resize(
                (max(1, int(width * scale)), max(1, int(height * scale))),
                Image.LANCZOS,
            )
        buffer = io.BytesIO()
        image.save(buffer, format="PNG", optimize=True)
        return base64.b64encode(buffer.getvalue()).decode("ascii")

    def _paste_image(self, image, selectors, cfg):
        image_wait = float(cfg.get("image_wait", 10))
        initial = int(self.client.eval(self._preview_count_js(selectors["image_preview"]), timeout=5) or 0)
        self._send_ctrl_v()
        if self._wait_preview(selectors["image_preview"], initial, min(4.0, image_wait)):
            return True

        if image is not None:
            log.info("Ctrl+V không thấy ảnh, thử synthetic paste.")
            try:
                b64 = self._encode_image(image, int(cfg.get("image_max_size", 1600)))
                self.client.eval(
                    self._synthetic_paste_js(selectors["input"], b64),
                    timeout=30,
                )
                if self._wait_preview(selectors["image_preview"], initial, min(5.0, image_wait)):
                    return True
            except Exception as e:
                log.warning("Synthetic paste lỗi: %s", e)

        log.warning("Không xác nhận được thumbnail ảnh, vẫn thử gửi.")
        return False

    def send_and_get_response(self, text, is_image=False, cancel_event=None, image=None):
        cfg = self._cfg()
        selectors = cfg["selectors"]
        try:
            self._ensure_client()
            self.client.eval("window.__geminiCancel=false;true", timeout=5)
            initial_count, initial_text = self._probe(selectors["response"])
            if initial_count and not initial_text:
                for _ in range(10):
                    time.sleep(0.2)
                    initial_count, initial_text = self._probe(selectors["response"])
                    if initial_text:
                        break

            if not self.client.eval(self._focus_js(selectors["input"]), timeout=5):
                return "Lỗi: Không tìm thấy ô nhập của Gemini."

            if is_image:
                self._insert_text(selectors["input"], cfg["prompt_image"])
                time.sleep(0.1)
                self._paste_image(image, selectors, cfg)
                time.sleep(0.4)
            else:
                self._insert_text(selectors["input"], text)

            self._wait_send_ready(selectors["send"], 3)
            if not self.client.eval(self._send_js(selectors["send"]), timeout=5):
                self.client.send(
                    "Input.dispatchKeyEvent",
                    {"type": "rawKeyDown", "key": "Enter", "code": "Enter",
                     "windowsVirtualKeyCode": 13, "nativeVirtualKeyCode": 13},
                    timeout=5,
                )
                self.client.send(
                    "Input.dispatchKeyEvent",
                    {"type": "keyUp", "key": "Enter", "code": "Enter",
                     "windowsVirtualKeyCode": 13, "nativeVirtualKeyCode": 13},
                    timeout=5,
                )

            log.info("Đang chờ phản hồi mới (CDP)...")
            result = self.client.eval(
                self._observer_js(
                    selectors["response"],
                    initial_count,
                    initial_text,
                    float(cfg.get("settle_ms", 1200)),
                    float(cfg.get("response_timeout", 60)) * 1000,
                ),
                await_promise=True,
                timeout=float(cfg.get("response_timeout", 60)) + 10,
            )
            if not isinstance(result, dict):
                return "Không lấy được phản hồi mới (kết quả rỗng)."
            status = result.get("status")
            if status == "ok":
                return str(result.get("text") or "").strip()
            if status == "cancelled":
                return CANCELLED
            return "Không lấy được phản hồi mới (Hết thời gian chờ)."
        except Exception as e:
            log.exception("Lỗi CDP khi tương tác")
            self.close()
            return f"Lỗi khi tương tác: {e}"


class SeleniumAutomation:
    def __init__(self, port):
        from selenium import webdriver
        from selenium.webdriver.chrome.options import Options
        from selenium.webdriver.chrome.service import Service
        from selenium.webdriver.support.ui import WebDriverWait

        chrome_options = Options()
        chrome_options.add_experimental_option("debuggerAddress", f"127.0.0.1:{port}")
        try:
            self.driver = webdriver.Chrome(options=chrome_options)
        except Exception:
            from webdriver_manager.chrome import ChromeDriverManager
            service = Service(ChromeDriverManager().install())
            self.driver = webdriver.Chrome(service=service, options=chrome_options)
        self.wait = WebDriverWait(self.driver, 30)
        self._gemini_handle = None
        self._cancel_lock = threading.Lock()
        log.info("Đã kết nối Selenium tới Chrome.")

    def close(self):
        self.driver = None

    def cancel(self):
        pass

    def _ensure_gemini_tab(self):
        if self._gemini_handle:
            try:
                self.driver.switch_to.window(self._gemini_handle)
                if "gemini.google.com" in self.driver.current_url:
                    return
            except Exception:
                self._gemini_handle = None

        for handle in self.driver.window_handles:
            try:
                self.driver.switch_to.window(handle)
                if "gemini.google.com" in self.driver.current_url:
                    self._gemini_handle = handle
                    return
            except Exception:
                continue

        self.driver.get(GEMINI_URL)
        self.wait.until(lambda d: "gemini.google.com" in d.current_url)
        self._gemini_handle = self.driver.current_window_handle

    def _probe(self, selector):
        from selenium.webdriver.common.by import By
        responses = self.driver.find_elements(By.CSS_SELECTOR, selector)
        if not responses:
            return 0, ""
        return len(responses), responses[-1].text.strip()

    def _insert_text(self, input_box, text, preserve_clipboard=False):
        from selenium.webdriver.common.keys import Keys

        try:
            self.driver.execute_cdp_cmd("Input.insertText", {"text": text})
            time.sleep(0.05)
            if (input_box.get_attribute("innerText") or "").strip():
                return
        except Exception:
            pass
        if preserve_clipboard:
            input_box.send_keys(text)
            return
        pyperclip.copy(text)
        input_box.send_keys(Keys.CONTROL, "v")

    def send_and_get_response(self, text, is_image=False, cancel_event=None, image=None):
        from selenium.webdriver.common.by import By
        from selenium.webdriver.common.keys import Keys
        from selenium.webdriver.support import expected_conditions as EC
        from selenium.webdriver.support.ui import WebDriverWait

        cfg = get_config()
        selectors = cfg["selectors"]
        try:
            self._ensure_gemini_tab()
            initial_count, old_last_text = self._probe(selectors["response"])
            if initial_count and not old_last_text:
                for _ in range(10):
                    time.sleep(0.2)
                    initial_count, old_last_text = self._probe(selectors["response"])
                    if old_last_text:
                        break

            input_box = self.wait.until(
                EC.presence_of_element_located((By.CSS_SELECTOR, selectors["input"]))
            )
            input_box.click()
            input_box.send_keys(Keys.CONTROL, "a")
            input_box.send_keys(Keys.DELETE)

            if is_image:
                self._insert_text(input_box, cfg["prompt_image"], preserve_clipboard=True)
                time.sleep(0.15)
                input_box.send_keys(Keys.CONTROL, 'v')
                try:
                    WebDriverWait(self.driver, cfg.get("image_wait", 10), poll_frequency=0.2).until(
                        lambda d: self.driver.find_elements(By.CSS_SELECTOR, selectors["image_preview"])
                    )
                    time.sleep(0.5)
                except Exception:
                    time.sleep(2)
            else:
                self._insert_text(input_box, text)
                time.sleep(0.05)

            send_js = (
                "(function(){const b=document.querySelector(%s);"
                "if(b&&!b.disabled){b.click();return true;}return false;})()"
            ) % json.dumps(selectors["send"])
            if not self.driver.execute_script(send_js):
                input_box.send_keys(Keys.ENTER)

            log.info("Đang chờ phản hồi mới (Selenium)...")
            return self._wait_for_response(
                initial_count, old_last_text, cancel_event, selectors["response"]
            )
        except Exception as e:
            log.exception("Lỗi Selenium khi tương tác")
            return f"Lỗi khi tương tác: {e}"

    def _wait_for_response(self, initial_count, old_last_text, cancel_event, selector):
        from selenium.webdriver.common.by import By

        cfg = get_config()
        deadline = time.time() + float(cfg.get("response_timeout", 60))
        poll = float(cfg.get("poll_interval", 0.35))
        last_text = ""
        stable = 0

        while time.time() < deadline:
            if cancel_event is not None and cancel_event.is_set():
                return CANCELLED
            try:
                responses = self.driver.find_elements(By.CSS_SELECTOR, selector)
                current_count = len(responses)
                current_text = responses[-1].text.strip() if responses else ""
            except Exception:
                time.sleep(poll)
                continue

            is_new_response = current_count > initial_count or (
                bool(current_text) and current_text != old_last_text
            )
            if is_new_response and current_text:
                if current_text != last_text:
                    last_text = current_text
                    stable = 0
                else:
                    stable += 1
                    if stable >= 3:
                        return current_text
            time.sleep(poll)

        return "Không lấy được phản hồi mới (Hết thời gian chờ)."


class Automation:
    def __init__(self):
        self._backend = None
        self._kind = None
        self._fatal = None
        self._last_error = None
        self._lock = threading.Lock()

    def _ensure(self):
        with self._lock:
            if self._backend is not None:
                return True
            if self._fatal:
                return False
            port = int(get_config().get("debug_port", 9222))
            try:
                self._backend = CDPAutomation(port)
                self._kind = "cdp"
                return True
            except CDPUnavailable as e:
                if e.fatal:
                    self._fatal = str(e)
                    log.error("%s", e)
                    return False
                log.warning("CDP không khả dụng (%s), chuyển sang Selenium.", e)
            try:
                self._backend = SeleniumAutomation(port)
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


class OverlayWindow:
    def __init__(self):
        cfg = get_config()
        self.root = tk.Tk()
        self.root.overrideredirect(True)
        self.root.attributes("-topmost", True)

        transparent_color = cfg["background_color"]
        self.root.configure(bg=transparent_color)
        self.root.wm_attributes("-transparentcolor", transparent_color)
        self.root.attributes("-alpha", cfg["opacity"])

        self.screen_w = self.root.winfo_screenwidth()
        self.screen_h = self.root.winfo_screenheight()

        self.label = tk.Label(
            self.root,
            text="",
            fg=cfg["text_color"],
            bg=transparent_color,
            font=("Arial", cfg["font_size"], "bold"),
            wraplength=max(200, min(cfg.get("wrap_length", 800), self.screen_w - 40)),
            justify="left"
        )
        self.label.pack()

        self.root.geometry(f"+{cfg['position_left_offset']}+{self.screen_h - 100}")
        self.root.withdraw()

        self.hide_timer = None

        self.root.after(100, self.check_queue)
        self.root.mainloop()

    def check_queue(self):
        while True:
            try:
                item = msg_queue.get_nowait()
            except queue.Empty:
                break

            if isinstance(item, tuple):
                text, duration = item
                if text == HIDE:
                    self.hide_now()
                else:
                    self.show_message(text, duration)
            else:
                self.show_message(item, get_config().get("display_seconds", 15))

        self.root.after(100, self.check_queue)

    def show_message(self, text, duration_sec):
        if self.hide_timer:
            self.root.after_cancel(self.hide_timer)
            self.hide_timer = None

        cfg = get_config()
        self.label.config(
            text=text,
            fg=cfg["text_color"],
            font=("Arial", cfg["font_size"], "bold"),
            wraplength=max(200, min(cfg.get("wrap_length", 800), self.screen_w - 40)),
        )
        self.root.attributes("-alpha", cfg["opacity"])

        self.root.update_idletasks()
        width = self.label.winfo_reqwidth()
        height = self.label.winfo_reqheight()

        x_pos = max(0, min(cfg["position_left_offset"], self.screen_w - width))
        y_pos = max(0, self.screen_h - height - cfg["position_bottom_offset"])

        self.root.geometry(f"{width}x{height}+{x_pos}+{y_pos}")
        self.root.deiconify()
        self.root.lift()

        if duration_sec > 0:
            self.hide_timer = self.root.after(int(duration_sec * 1000), self._auto_hide)

    def _auto_hide(self):
        self.hide_timer = None
        self.root.withdraw()

    def hide_now(self):
        if self.hide_timer:
            self.root.after_cancel(self.hide_timer)
            self.hide_timer = None
        self.root.withdraw()


def run_overlay():
    OverlayWindow()


def automation_task():
    automation = Automation()
    commands = queue.Queue()
    cancel_event = threading.Event()
    busy = threading.Event()
    last_response = {"text": ""}
    last_press = {"send": 0.0}

    log.info("Script đang chạy...")
    log.info("Config: text=%s, opacity=%s", config["text_color"], config["opacity"])

    def enqueue(command):
        if command == "send":
            if busy.is_set():
                return
            busy.set()
        commands.put(command)

    def on_send_pressed(e):
        now = time.time()
        if now - last_press["send"] < 0.4:
            return
        last_press["send"] = now
        enqueue("send")

    def on_replay_pressed(e):
        enqueue("replay")

    def on_hide_pressed(e):
        cancel_event.set()
        automation.cancel()
        msg_queue.put((HIDE, 0))

    def handle_send():
        cfg = get_config()
        clip = None
        try:
            clip = ImageGrab.grabclipboard()
        except Exception:
            pass
        has_image = bool(clip)
        image = clip if isinstance(clip, Image.Image) else None

        try:
            text_content = pyperclip.paste().strip()
        except Exception:
            text_content = ""

        if has_image:
            job_text = ""
            is_image = True
            log.info("Phát hiện ảnh trong clipboard.")
        elif text_content:
            job_text = f"{cfg['prompt_text']}{text_content}"
            is_image = False
        else:
            log.info("Clipboard trống (không ảnh, không text).")
            msg_queue.put(("Clipboard trống!", 1))
            return

        msg_queue.put(("...", 2))
        cancel_event.clear()
        result = automation.send_and_get_response(
            job_text,
            is_image=is_image,
            cancel_event=cancel_event,
            image=image,
        )

        if result == CANCELLED:
            return

        last_response["text"] = result
        msg_queue.put((result, cfg.get("display_seconds", 15)))

    def worker():
        while True:
            command = commands.get()
            try:
                if command == "send":
                    handle_send()
                elif command == "replay":
                    if last_response["text"]:
                        msg_queue.put((last_response["text"], get_config().get("display_seconds", 15)))
                    else:
                        msg_queue.put(("Chưa có đáp án nào!", 1))
            except Exception as e:
                log.exception("Lỗi worker")
                msg_queue.put((f"Lỗi: {e}", 5))
            finally:
                if command == "send":
                    busy.clear()

    Thread(target=worker, daemon=True).start()

    keyboard.on_press_key("F2", on_send_pressed)
    keyboard.on_press_key("F4", on_replay_pressed)
    keyboard.on_press_key("esc", on_hide_pressed)

    keyboard.wait()


def main():
    enable_dpi_awareness()
    mutex = acquire_single_instance()
    if mutex is None:
        ctypes.windll.user32.MessageBoxW(
            None,
            "Gemini Quick Assistant đang chạy rồi!",
            "Gemini Quick Assistant",
            0x40,
        )
        return
    Thread(target=automation_task, daemon=True).start()
    run_overlay()


if __name__ == "__main__":
    main()
