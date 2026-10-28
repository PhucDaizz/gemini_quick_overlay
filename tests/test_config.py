import json
import os
import tempfile
import unittest

from gemini_overlay.config import read_config


class ReadConfigTests(unittest.TestCase):
    def test_missing_file_uses_defaults(self):
        with tempfile.TemporaryDirectory() as directory:
            cfg, mtime = read_config(os.path.join(directory, "missing.json"))
        self.assertEqual(cfg["debug_port"], 9222)
        self.assertIsNone(mtime)

    def test_user_values_override_and_selectors_merge(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "config.json")
            with open(path, "w", encoding="utf-8") as handle:
                json.dump(
                    {
                        "debug_port": 9333,
                        "selectors": {"response": ".answer"},
                    },
                    handle,
                )
            cfg, mtime = read_config(path)
        self.assertEqual(cfg["debug_port"], 9333)
        self.assertEqual(cfg["selectors"]["response"], ".answer")
        self.assertEqual(cfg["selectors"]["input"], "div[contenteditable='true']")
        self.assertIsNotNone(mtime)

    def test_invalid_json_falls_back_to_defaults(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "config.json")
            with open(path, "w", encoding="utf-8") as handle:
                handle.write("{bad")
            cfg, mtime = read_config(path)
        self.assertEqual(cfg["debug_port"], 9222)
        self.assertIsNotNone(mtime)


if __name__ == "__main__":
    unittest.main()
