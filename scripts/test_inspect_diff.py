import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from scripts.inspect_diff import DiffEntry, main, parse_numstat


class DiffNumstatTests(unittest.TestCase):
    def test_parse_numstat_handles_spaces_and_binary_files(self) -> None:
        entries = parse_numstat(b"4\t2\tweb/src/a file.ts\0-\t-\tassets/poster.png\0")

        self.assertEqual(entries, [
            DiffEntry("web/src/a file.ts", 4, 2),
            DiffEntry("assets/poster.png", None, None),
        ])


class DiffInspectorCliTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)

    def mock_git(self, diff: bytes):
        def run(command, *, stdout=None, **kwargs):
            if "--numstat" in command:
                return SimpleNamespace(returncode=0, stdout=b"4\t2\tsrc/example.ts\0", stderr=b"")
            stdout.write(diff)
            return SimpleNamespace(returncode=0, stderr=b"")

        return run

    def test_small_diff_is_printed_and_snapshot_is_removed(self) -> None:
        output = io.StringIO()
        with mock.patch("scripts.inspect_diff.ROOT", self.root), \
                mock.patch("scripts.inspect_diff.subprocess.run", side_effect=self.mock_git(b"one\ntwo\n")):
            with contextlib.redirect_stdout(output):
                self.assertEqual(main([]), 0)

        self.assertEqual(output.getvalue(), "one\ntwo\n")
        self.assertEqual(list((self.root / ".test-tmp").iterdir()), [])

    def test_large_diff_reports_sizes_and_keeps_snapshot(self) -> None:
        output = io.StringIO()
        diff = b"one\ntwo\nthree\nfour\n"
        with mock.patch("scripts.inspect_diff.ROOT", self.root), \
                mock.patch("scripts.inspect_diff.subprocess.run", side_effect=self.mock_git(diff)):
            with contextlib.redirect_stdout(output):
                self.assertEqual(main(["--max-lines", "2"]), 0)

        self.assertIn("Diff summary against HEAD: 1 file(s), +4/-2 changed lines", output.getvalue())
        self.assertIn("src/example.ts", output.getvalue())
        snapshots = list((self.root / ".test-tmp").glob("mediadock-diff-*.patch"))
        self.assertEqual(len(snapshots), 1)
        self.assertEqual(snapshots[0].read_bytes(), diff)

    def test_single_large_file_is_summarized_when_total_diff_is_small(self) -> None:
        output = io.StringIO()
        with mock.patch("scripts.inspect_diff.ROOT", self.root), \
                mock.patch("scripts.inspect_diff.subprocess.run", side_effect=self.mock_git(b"one\ntwo\n")):
            with contextlib.redirect_stdout(output):
                self.assertEqual(main(["--max-file-lines", "5"]), 0)

        self.assertIn("Diff summary against HEAD: 1 file(s), +4/-2 changed lines", output.getvalue())
        self.assertIn("Rendered diff: 2 lines", output.getvalue())

    def test_requested_page_prints_only_its_line_range(self) -> None:
        output = io.StringIO()
        diff = b"one\ntwo\nthree\nfour\n"
        with mock.patch("scripts.inspect_diff.ROOT", self.root), \
                mock.patch("scripts.inspect_diff.subprocess.run", side_effect=self.mock_git(diff)):
            with contextlib.redirect_stdout(output):
                self.assertEqual(main(["--file", "src/example.ts", "--start-line", "2", "--lines", "2"]), 0)

        self.assertTrue(output.getvalue().startswith("two\nthree\n"))
        self.assertIn("Diff lines 2-3 of 4.", output.getvalue())


if __name__ == "__main__":
    unittest.main()