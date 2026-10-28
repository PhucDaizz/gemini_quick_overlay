import ctypes
import queue
import threading
import time
from threading import Thread

import keyboard
import pyperclip
from PIL import Image, ImageGrab

from .automation import Automation
from .config import get_config
from .constants import CANCELLED, HIDE
from .events import msg_queue
from .logging_setup import log, setup_logging
from .overlay import run_overlay
from .win_utils import acquire_single_instance, enable_dpi_awareness


def automation_task():
    cfg = get_config()
    automation = Automation()
    commands = queue.Queue()
    cancel_event = threading.Event()
    busy = threading.Event()
    last_response = {"text": ""}
    last_press = {"send": 0.0}

    log.info("Script đang chạy...")
    log.info("Config: text=%s, opacity=%s", cfg["text_color"], cfg["opacity"])

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
    setup_logging()
    get_config()
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
