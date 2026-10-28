import os
import sys

from .constants import CONFIG_FILENAME, ICON_FILENAME, LOG_FILENAME, UPDATE_STATE_FILENAME

if getattr(sys, "frozen", False):
    APP_DIR = os.path.dirname(sys.executable)
else:
    APP_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def resource_path(filename):
    if getattr(sys, "frozen", False):
        bundled = os.path.join(getattr(sys, "_MEIPASS", APP_DIR), filename)
        if os.path.exists(bundled):
            return bundled
    return os.path.join(APP_DIR, filename)


CONFIG_PATH = os.path.join(APP_DIR, CONFIG_FILENAME)
LOG_PATH = os.path.join(APP_DIR, LOG_FILENAME)
UPDATE_STATE_PATH = os.path.join(APP_DIR, UPDATE_STATE_FILENAME)
ICON_PATH = resource_path(ICON_FILENAME)
