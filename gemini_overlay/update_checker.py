import json
import re
import time

import requests

from . import __version__
from .constants import RELEASES_API_URL
from .logging_setup import log
from .paths import UPDATE_STATE_PATH

CHECK_INTERVAL = 24 * 3600


def parse_version(tag):
    match = re.search(r"(\d+(?:\.\d+)*)", str(tag or ""))
    if not match:
        return None
    return tuple(int(part) for part in match.group(1).split("."))


def is_newer(latest_tag, current=None):
    latest = parse_version(latest_tag)
    now = parse_version(current or __version__)
    return bool(latest and now and latest > now)


def _load_state():
    try:
        with open(UPDATE_STATE_PATH, "r", encoding="utf-8") as f:
            state = json.load(f)
        return state if isinstance(state, dict) else {}
    except (OSError, ValueError):
        return {}


def _save_state(state):
    try:
        with open(UPDATE_STATE_PATH, "w", encoding="utf-8") as f:
            json.dump(state, f)
    except OSError:
        pass


def fetch_latest():
    response = requests.get(
        RELEASES_API_URL,
        headers={"Accept": "application/vnd.github+json"},
        timeout=6,
    )
    response.raise_for_status()
    data = response.json()
    return data.get("tag_name"), data.get("html_url")


def check_for_update(force=False, now=None):
    """Return (tag, url) when a newer release exists, otherwise None.

    Unless `force` is set, hits the network at most once per day and re-uses
    the cached result in between. A forced check raises on network errors so
    the caller can tell "up to date" apart from "could not check".
    """
    now = time.time() if now is None else now
    state = _load_state()
    fresh = "checked_at" in state and now - float(state["checked_at"]) < CHECK_INTERVAL
    if not force and fresh:
        tag, url = state.get("tag"), state.get("url")
    else:
        try:
            tag, url = fetch_latest()
        except Exception as e:
            log.info("Không kiểm tra được cập nhật: %s", e)
            if force:
                raise
            return None
        _save_state({"checked_at": now, "tag": tag, "url": url})
    if tag and is_newer(tag):
        return tag, url
    return None
