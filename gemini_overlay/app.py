import ctypes
import os
import queue
import subprocess
import sys
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
from .overlay import OverlayWindow
from .paths import APP_DIR
from .tray import TrayController
from .win_utils import (
    acquire_single_instance,
    enable_dpi_awareness,
    release_single_instance,
)


def automation_task(commands=None, cancel_event=None, last_response=None):
    cfg = get_config()
    automation = Automation()
    commands = commands if commands is not None else queue.Queue()
    cancel_event = cancel_event if cancel_event is not None else threading.Event()
    last_response = last_response if last_response is not None else {"text": ""}
    busy = threading.Event()
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

    def on_copy_pressed(e):
        enqueue("copy")

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

        if cfg.get("show_pending", False):
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
                        msg_queue.put(
                            (
                                last_response["text"],
                                get_config().get("display_seconds", 15),
                            )
                        )
                    else:
                        msg_queue.put(("Chưa có đáp án nào!", 1))
                elif command == "copy":
                    text = last_response["text"]
                    if text:
                        try:
                            pyperclip.copy(text)
                            log.info("Đã copy đáp án.")
                        except Exception as e:
                            log.error("Không copy được đáp án: %s", e)
                    else:
                        log.info("Chưa có đáp án để copy.")
            except Exception as e:
                log.exception("Lỗi worker")
                msg_queue.put((f"Lỗi: {e}", 5))
            finally:
                if command == "send":
                    busy.clear()

    Thread(target=worker, daemon=True).start()

    hotkeys = (
        ("hotkey_send", "f2", on_send_pressed),
        ("hotkey_replay", "f4", on_replay_pressed),
        ("hotkey_copy", "f3", on_copy_pressed),
        ("hotkey_hide", "esc", on_hide_pressed),
    )
    used = set()
    for config_key, default, callback in hotkeys:
        key = str(get_config().get(config_key) or default).strip() or default
        if key.lower() in used:
            log.warning("Hotkey '%s' bị trùng (%s), bỏ qua.", key, config_key)
            continue
        used.add(key.lower())
        try:
            keyboard.on_press_key(key, callback)
            log.info("Hotkey %s: %s", config_key, key)
        except Exception as e:
            log.error("Không đăng ký được hotkey %s=%s: %s", config_key, key, e)

    keyboard.wait()


def _launch_restart():
    if getattr(sys, "frozen", False):
        args = [sys.executable]
    else:
        entry = os.path.join(APP_DIR, "gemini_helper_v2_window_activation.py")
        args = [sys.executable, entry]
    try:
        subprocess.Popen(args, cwd=APP_DIR)
    except Exception:
        log.exception("Không khởi động lại được.")


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

    window = OverlayWindow()
    commands = queue.Queue()
    cancel_event = threading.Event()
    last_response = {"text": ""}
    Thread(
        target=automation_task,
        args=(commands, cancel_event, last_response),
        daemon=True,
    ).start()

    restart_requested = {"value": False}

    def request_restart():
        restart_requested["value"] = True
        window.request_quit()

    tray = TrayController(
        on_show=lambda: commands.put("replay"),
        on_copy=lambda: commands.put("copy"),
        on_quit=window.request_quit,
        on_restart=request_restart,
    )
    tray.start()
    try:
        window.run()
    finally:
        tray.stop()
        release_single_instance(mutex)

    if restart_requested["value"]:
        _launch_restart()
