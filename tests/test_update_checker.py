import unittest
from unittest import mock

from gemini_overlay import update_checker as uc


class VersionTests(unittest.TestCase):
    def test_parse(self):
        self.assertEqual(uc.parse_version("v1.2.3"), (1, 2, 3))
        self.assertIsNone(uc.parse_version("latest"))
        self.assertIsNone(uc.parse_version(None))

    def test_is_newer(self):
        self.assertTrue(uc.is_newer("v1.10.0", "1.9.0"))
        self.assertFalse(uc.is_newer("v1.2.0", "1.2.0"))
        self.assertFalse(uc.is_newer("v1.1.0", "1.2.0"))
        self.assertFalse(uc.is_newer("garbage", "1.2.0"))


class CheckTests(unittest.TestCase):
    def setUp(self):
        self.state = {}
        patches = (
            mock.patch.object(uc, "_load_state", lambda: dict(self.state)),
            mock.patch.object(uc, "_save_state", lambda s: self.state.update(s)),
            mock.patch.object(uc, "__version__", "1.0.0"),
        )
        for p in patches:
            p.start()
            self.addCleanup(p.stop)

    def test_newer_release_found_and_cached(self):
        with mock.patch.object(uc, "fetch_latest", return_value=("v1.5.0", "u")) as fetch:
            self.assertEqual(uc.check_for_update(now=1000), ("v1.5.0", "u"))
            self.assertEqual(uc.check_for_update(now=1000 + 60), ("v1.5.0", "u"))
        fetch.assert_called_once()

    def test_rechecks_after_a_day(self):
        with mock.patch.object(uc, "fetch_latest", return_value=("v1.0.0", "u")) as fetch:
            self.assertIsNone(uc.check_for_update(now=0))
            self.assertIsNone(uc.check_for_update(now=uc.CHECK_INTERVAL + 1))
        self.assertEqual(fetch.call_count, 2)

    def test_network_error_is_silent_unless_forced(self):
        with mock.patch.object(uc, "fetch_latest", side_effect=OSError("down")):
            self.assertIsNone(uc.check_for_update(now=0))
            with self.assertRaises(OSError):
                uc.check_for_update(force=True, now=0)


if __name__ == "__main__":
    unittest.main()
