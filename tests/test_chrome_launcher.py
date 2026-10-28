import socket
import unittest
from unittest import mock

from gemini_overlay import chrome_launcher


class ProfileDirTests(unittest.TestCase):
    def test_default_when_empty(self):
        self.assertEqual(chrome_launcher.profile_dir({}), chrome_launcher.DEFAULT_PROFILE_DIR)
        self.assertEqual(chrome_launcher.profile_dir({"profile_dir": "  "}), chrome_launcher.DEFAULT_PROFILE_DIR)

    def test_custom_value(self):
        self.assertEqual(chrome_launcher.profile_dir({"profile_dir": "D:\p"}), "D:\p")


class EnsureChromeTests(unittest.TestCase):
    def test_port_open_detected(self):
        with socket.socket() as s:
            s.bind(("127.0.0.1", 0))
            s.listen()
            self.assertTrue(chrome_launcher.port_open(s.getsockname()[1]))

    def test_skips_launch_when_port_open(self):
        with mock.patch.object(chrome_launcher, "port_open", return_value=True), \
                mock.patch.object(chrome_launcher.subprocess, "Popen") as popen:
            self.assertIsNone(chrome_launcher.ensure_chrome({"debug_port": 9222}))
        popen.assert_not_called()

    def test_disabled_auto_launch(self):
        with mock.patch.object(chrome_launcher, "port_open", return_value=False), \
                mock.patch.object(chrome_launcher.subprocess, "Popen") as popen:
            result = chrome_launcher.ensure_chrome({"debug_port": 9222, "auto_launch_chrome": False})
        self.assertIsNone(result)
        popen.assert_not_called()

    def test_missing_chrome_reports_error(self):
        with mock.patch.object(chrome_launcher, "port_open", return_value=False), \
                mock.patch.object(chrome_launcher, "find_chrome", return_value=None):
            self.assertIn("Chrome", chrome_launcher.ensure_chrome({"debug_port": 9222}))

    def test_launch_uses_port_and_profile(self):
        with mock.patch.object(chrome_launcher, "port_open", side_effect=[False, True]), \
                mock.patch.object(chrome_launcher, "find_chrome", return_value="chrome.exe"), \
                mock.patch.object(chrome_launcher.subprocess, "Popen") as popen:
            result = chrome_launcher.ensure_chrome({"debug_port": 9333, "profile_dir": "D:\p"})
        self.assertIsNone(result)
        args = popen.call_args[0][0]
        self.assertIn("--remote-debugging-port=9333", args)
        self.assertIn("--user-data-dir=D:\p", args)


if __name__ == "__main__":
    unittest.main()
