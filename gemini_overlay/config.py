import json
import os
import threading

from .constants import DEFAULT_CONFIG, DEFAULT_SELECTORS
from .logging_setup import log
from .paths import CONFIG_PATH

_config = None
_config_mtime = None
_config_lock = threading.Lock()


def read_config(path=CONFIG_PATH):
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


def get_config():
    global _config, _config_mtime
    if _config is None:
        with _config_lock:
            if _config is None:
                _config, _config_mtime = read_config(CONFIG_PATH)
        return _config
    try:
        mtime = os.path.getmtime(CONFIG_PATH)
    except OSError:
        return _config
    if mtime == _config_mtime:
        return _config
    with _config_lock:
        if mtime != _config_mtime:
            new_config, _config_mtime = read_config(CONFIG_PATH)
            _config = new_config
    return _config
