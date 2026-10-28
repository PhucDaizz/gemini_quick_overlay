import base64
import io
import json
import threading
import time

from PIL import Image

from ..config import get_config
from ..constants import CANCELLED
from ..logging_setup import log
from .cdp import CDPClient


class CDPAutomation:
    def __init__(
        self,
        port=None,
        target_match="gemini.google.com",
        config_override=None,
        config_provider=None,
    ):
        self.config_override = config_override
        self._config_provider = config_provider
        self._port = port or int(self._cfg().get("debug_port", 9222))
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
        if self.config_override is not None:
            return self.config_override
        provider = self._config_provider or get_config
        return provider()

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

