import threading
import webbrowser

from PIL import Image, ImageDraw

from . import __version__
from .constants import APP_NAME, RELEASES_PAGE_URL
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
    def __init__(self, on_show, on_copy, on_quit, on_restart, on_check_update=None):
        self._on_show = on_show
        self._on_copy = on_copy
        self._on_quit = on_quit
        self._on_restart = on_restart
        self._on_check_update = on_check_update
        self._update = None
        self._icon = None

    def set_update(self, tag, url):
        self._update = (tag, url or RELEASES_PAGE_URL)
        if self._icon is not None:
            try:
                self._icon.update_menu()
            except Exception:
                pass

    def _update_label(self, item):
        return f"Có bản mới {self._update[0]} - Tải về"

    def _open_update(self, icon, item):
        if self._update:
            webbrowser.open(self._update[1])

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
                    self._update_label,
                    self._open_update,
                    visible=lambda item: self._update is not None,
                ),
                pystray.MenuItem(
                    f"Kiểm tra cập nhật (v{__version__})",
                    lambda icon, item: self._on_check_update(),
                    visible=lambda item: self._on_check_update is not None,
                ),
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
