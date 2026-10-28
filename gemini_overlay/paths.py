import os
import sys

from .constants import CONFIG_FILENAME, LOG_FILENAME

if getattr(sys, "frozen", False):
    APP_DIR = os.path.dirname(sys.executable)
else:
    APP_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

CONFIG_PATH = os.path.join(APP_DIR, CONFIG_FILENAME)
LOG_PATH = os.path.join(APP_DIR, LOG_FILENAME)
