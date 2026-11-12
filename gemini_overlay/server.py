"""Cổng nội bộ kiểu OpenAI cho app khác dùng Gemini trong Chrome (không cần API key).

Chạy:  python -m gemini_overlay.server [cổng]      (mặc định 8765, chỉ nghe trên 127.0.0.1)
Gọi:   POST http://127.0.0.1:8765/v1/chat/completions  {"messages": [{"role": "system"|"user", "content": "..."}]}

Mỗi yêu cầu được gõ vào trang Gemini rồi chờ câu trả lời, nên chậm (vài giây) và chỉ chạy từng câu một.
"""

import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from .automation import Automation
from .constants import CANCELLED
from .logging_setup import log, setup_logging

DEFAULT_PORT = 8765
MAX_BODY = 64 * 1024
LOCAL_HOSTS = ("127.0.0.1", "localhost")
# Các câu báo lỗi của backend (không phải câu trả lời của Gemini)
ERROR_PREFIXES = ("Lỗi", "Không lấy được", "Không tìm thấy Chrome", "Không khởi động được", "Không kết nối được")


def build_prompt(messages):
    """Ghép system + user thành MỘT dòng: xuống dòng trong ô nhập Gemini có thể bị hiểu là bấm gửi."""
    parts = []
    for m in messages if isinstance(messages, list) else []:
        content = m.get("content") if isinstance(m, dict) else None
        if isinstance(content, str) and content.strip():
            parts.append(" ".join(content.split()))
    return " ||| ".join(parts)


def handle(body, ask):
    """Xử lý một yêu cầu chat. Trả (mã HTTP, dict). `ask(prompt) -> str` là hàm hỏi Gemini."""
    try:
        data = json.loads(body)
    except (ValueError, UnicodeDecodeError):
        return 400, {"error": {"message": "Body phải là JSON"}}
    prompt = build_prompt(data.get("messages") if isinstance(data, dict) else None)
    if not prompt:
        return 400, {"error": {"message": "Thiếu messages"}}
    answer = ask(prompt)
    if not isinstance(answer, str) or not answer.strip() or answer == CANCELLED or answer.startswith(ERROR_PREFIXES):
        return 502, {"error": {"message": str(answer or "Gemini không trả lời")}}
    return 200, {
        "object": "chat.completion",
        "model": "gemini-web",
        "choices": [{"index": 0, "finish_reason": "stop", "message": {"role": "assistant", "content": answer.strip()}}],
    }


def make_handler(ask):
    lock = threading.Lock()  # trang Gemini chỉ gõ được một câu một lúc

    class Handler(BaseHTTPRequestHandler):
        def _send(self, status, payload):
            raw = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

        def do_POST(self):
            # chỉ nhận yêu cầu gửi tới chính máy này (chặn trang web lạ gọi vào qua DNS rebinding)
            if (self.headers.get("Host") or "").rsplit(":", 1)[0] not in LOCAL_HOSTS or self.headers.get("Origin"):
                return self._send(403, {"error": {"message": "Forbidden"}})
            if self.path.rstrip("/") != "/v1/chat/completions":
                return self._send(404, {"error": {"message": "Not Found"}})
            length = int(self.headers.get("Content-Length") or 0)
            if length <= 0 or length > MAX_BODY:
                return self._send(413, {"error": {"message": "Body rỗng hoặc quá lớn"}})
            body = self.rfile.read(length)
            with lock:
                status, payload = handle(body, ask)
            try:
                self._send(status, payload)
            except OSError:
                pass  # bên gọi đã hết kiên nhẫn và ngắt kết nối

        def log_message(self, fmt, *args):
            log.info("server: " + fmt, *args)

    return Handler


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    port = int(argv[0]) if argv else DEFAULT_PORT
    setup_logging()
    automation = Automation()
    server = ThreadingHTTPServer(("127.0.0.1", port), make_handler(automation.send_and_get_response))
    print(f"Gemini (Chrome) đang nghe tại http://127.0.0.1:{port}/v1  ·  Ctrl+C để tắt")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        automation.close()


if __name__ == "__main__":
    main()
