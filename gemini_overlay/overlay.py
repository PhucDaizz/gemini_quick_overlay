import queue
import tkinter as tk

from .config import get_config
from .constants import HIDE
from .events import msg_queue


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
