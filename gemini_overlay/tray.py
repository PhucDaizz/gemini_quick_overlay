import threading

from PIL import Image, ImageDraw

from .constants import APP_NAME
from .logging_setup import log
from .paths import ICON_PATH

try:
    import pystray
except Exception:
    pystray = None


def _icon_image():
    try:
        with Image.open(ICON_PATH) as image:
            image = image.convert("RGBA")
            image.thumbnail((64, 64), Image.LANCZOS)
            return image.copy()
    except Exception as e:
        log.warning("Không load được icon %s: %s", ICON_PATH, e)

    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rectangle((4, 4, 60, 60), fill=(32, 33, 36, 255))
    draw.ellipse((18, 18, 46, 46), fill=(138, 180, 248, 255))
    return image


class TrayController:
    def __init__(self, on_show, on_copy, on_quit, on_restart):
        self._on_show = on_show
        self._on_copy = on_copy
        self._on_quit = on_quit
        self._on_restart = on_restart
        self._icon = None

    def start(self):
        if pystray is None:
            log.warning("Không có pystray, bỏ qua tray icon.")
            return False
        try:
            menu = pystray.Menu(
                pystray.MenuItem(
                    "Hiện đáp án",
                    lambda icon, item: self._on_show(),
                    default=True,
                ),
                pystray.MenuItem(
                    "Copy đáp án",
                    lambda icon, item: self._on_copy(),
                ),
                pystray.Menu.SEPARATOR,
                pystray.MenuItem(
                    "Khởi động lại",
                    lambda icon, item: self._on_restart(),
                ),
                pystray.MenuItem(
                    "Thoát",
                    lambda icon, item: self._on_quit(),
                ),
            )
            self._icon = pystray.Icon(
                "aihelper",
                _icon_image(),
                APP_NAME,
                menu,
            )
            threading.Thread(target=self._icon.run, daemon=True).start()
            log.info("Đã bật tray icon.")
            return True
        except Exception as e:
            log.warning("Không bật được tray icon: %s", e)
            self._icon = None
            return False

    def stop(self):
        icon = self._icon
        self._icon = None
        if icon is not None:
            try:
                icon.stop()
            except Exception:
                pass
