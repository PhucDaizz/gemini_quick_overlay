import json
import threading
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer

from gemini_overlay.constants import CANCELLED
from gemini_overlay.server import build_prompt, handle, make_handler


class BuildPromptTests(unittest.TestCase):
    def test_joins_system_and_user_on_one_line(self):
        prompt = build_prompt([{"role": "system", "content": "Bạn là cô giáo.\nNói ngắn."}, {"role": "user", "content": "cô ơi\nkhoẻ không"}])
        self.assertEqual(prompt, "Bạn là cô giáo. Nói ngắn. ||| cô ơi khoẻ không")
        self.assertNotIn("\n", prompt)

    def test_ignores_bad_messages(self):
        self.assertEqual(build_prompt(None), "")
        self.assertEqual(build_prompt([{"content": 5}, "x", {"content": "  "}]), "")


class HandleTests(unittest.TestCase):
    def test_ok_response_has_openai_shape(self):
        status, payload = handle(json.dumps({"messages": [{"role": "user", "content": "chào"}]}).encode(), lambda p: " Chào em! ")
        self.assertEqual(status, 200)
        self.assertEqual(payload["choices"][0]["message"]["content"], "Chào em!")

    def test_bad_body_and_missing_messages(self):
        self.assertEqual(handle(b"{hong", lambda p: "x")[0], 400)
        self.assertEqual(handle(b"{}", lambda p: "x")[0], 400)

    def test_backend_errors_become_502(self):
        body = json.dumps({"messages": [{"role": "user", "content": "chào"}]}).encode()
        for bad in ["Lỗi: Chưa đăng nhập Gemini.", "Không lấy được phản hồi mới (hết thời gian).", CANCELLED, "", None]:
            self.assertEqual(handle(body, lambda p, bad=bad: bad)[0], 502)


class HttpTests(unittest.TestCase):
    def setUp(self):
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(lambda p: "trả lời: " + p))
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.base = f"http://127.0.0.1:{self.server.server_address[1]}"

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()

    def post(self, path, headers=None):
        req = urllib.request.Request(self.base + path, data=json.dumps({"messages": [{"role": "user", "content": "chào"}]}).encode(), headers={"Content-Type": "application/json", **(headers or {})})
        try:
            with urllib.request.urlopen(req, timeout=5) as res:
                return res.status, json.loads(res.read())
        except urllib.error.HTTPError as e:
            return e.code, None

    def test_chat_completions_roundtrip(self):
        status, payload = self.post("/v1/chat/completions")
        self.assertEqual(status, 200)
        self.assertEqual(payload["choices"][0]["message"]["content"], "trả lời: chào")

    def test_rejects_other_paths_and_foreign_callers(self):
        self.assertEqual(self.post("/v1/models")[0], 404)
        self.assertEqual(self.post("/v1/chat/completions", {"Host": "evil.com"})[0], 403)
        self.assertEqual(self.post("/v1/chat/completions", {"Origin": "https://evil.com"})[0], 403)


if __name__ == "__main__":
    unittest.main()
