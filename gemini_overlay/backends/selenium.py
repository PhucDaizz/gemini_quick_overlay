import json
import threading
import time

import pyperclip

from ..config import get_config
from ..constants import CANCELLED, GEMINI_URL
from ..logging_setup import log


class SeleniumAutomation:
    def __init__(self, port, config_provider=None):
        self._config_provider = config_provider
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

    def _cfg(self):
        provider = self._config_provider or get_config
        return provider()

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

        cfg = self._cfg()
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

        cfg = self._cfg()
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

