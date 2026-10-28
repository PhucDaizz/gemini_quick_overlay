import os
import socket
import subprocess
import time

from .logging_setup import log
from .paths import APP_DIR

DEFAULT_PROFILE_DIR = r"C:\selenium\ChromeProfile"
CHROME_ARGS = (
    "--disable-background-timer-throttling",
    "--disable-backgrounding-occluded-windows",
    "--disable-renderer-backgrounding",
)


def find_chrome():
    override = os.path.join(APP_DIR, "chrome_path.txt")
    if os.path.isfile(override):
        try:
            with open(override, "r", encoding="utf-8") as f:
                path = f.readline().strip()
            if os.path.isfile(path):
                return path
        except OSError:
            pass
    roots = (
        os.environ.get("ProgramFiles"),
        os.environ.get("ProgramFiles(x86)"),
        os.environ.get("LOCALAPPDATA"),
    )
    for root in roots:
        if root:
            path = os.path.join(root, "Google", "Chrome", "Application", "chrome.exe")
            if os.path.isfile(path):
                return path
    return None


def profile_dir(cfg):
    return str(cfg.get("profile_dir") or "").strip() or DEFAULT_PROFILE_DIR


def port_open(port, timeout=0.5):
    for host, family in (("127.0.0.1", socket.AF_INET), ("::1", socket.AF_INET6)):
        try:
            with socket.socket(family, socket.SOCK_STREAM) as s:
                s.settimeout(timeout)
                if s.connect_ex((host, port)) == 0:
                    return True
        except OSError:
            continue
    return False


def ensure_chrome(cfg, wait=10.0):
    """Start Chrome with remote debugging if nothing listens on the port.

    Returns an error message, or None when the port is (now) reachable.
    """
    port = int(cfg.get("debug_port", 9222))
    if port_open(port):
        return None
    if not cfg.get("auto_launch_chrome", True):
        return None
    chrome = find_chrome()
    if not chrome:
        return "Không tìm thấy Chrome. Tạo chrome_path.txt chứa đường dẫn chrome.exe."
    args = [
        chrome,
        f"--remote-debugging-port={port}",
        f"--user-data-dir={profile_dir(cfg)}",
        *CHROME_ARGS,
    ]
    startupinfo = subprocess.STARTUPINFO()
    startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startupinfo.wShowWindow = 0
    try:
        subprocess.Popen(args, startupinfo=startupinfo, cwd=APP_DIR)
    except OSError as e:
        return f"Không khởi động được Chrome: {e}"
    log.info("Đã khởi động Chrome (port %s).", port)
    deadline = time.time() + wait
    while time.time() < deadline:
        if port_open(port):
            return None
        time.sleep(0.3)
    return "Chrome khởi động nhưng chưa mở cổng debug."
