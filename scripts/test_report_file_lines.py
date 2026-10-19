import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from scripts.report_file_lines import collect_file_lines, count_file_lines, main


class FileLineInventoryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)

    def test_count_handles_empty_files_and_unterminated_final_lines(self) -> None:
        empty = self.root / "empty.txt"
        terminated = self.root / "terminated.txt"
        unterminated = self.root / "unterminated.txt"
        mixed_newlines = self.root / "mixed.txt"
        empty.write_bytes(b"")
        terminated.write_bytes(b"first\nsecond\n")
        unterminated.write_bytes(b"first\nsecond")
        mixed_newlines.write_bytes(b"first\r\nsecond\rthird")

        self.assertEqual(count_file_lines(empty), 0)
        self.assertEqual(count_file_lines(terminated), 2)
        self.assertEqual(count_file_lines(unterminated), 2)
        self.assertEqual(count_file_lines(mixed_newlines), 3)

    def test_binary_files_are_skipped(self) -> None:
        binary = self.root / "image.bin"
        binary.write_bytes(b"header\0payload\n")

        self.assertIsNone(count_file_lines(binary))
        self.assertEqual(collect_file_lines(self.root), [])

    def test_inventory_sorts_by_count_then_relative_path(self) -> None:
        (self.root / "z.txt").write_text("one\ntwo\n", encoding="utf-8")
        (self.root / "a.txt").write_text("one\ntwo\n", encoding="utf-8")
        (self.root / "larger.txt").write_text("one\ntwo\nthree", encoding="utf-8")

        self.assertEqual(
            collect_file_lines(self.root),
            [("larger.txt", 3), ("a.txt", 2), ("z.txt", 2)],
        )

    def test_minimum_threshold_includes_equal_counts(self) -> None:
        (self.root / "two.txt").write_text("one\ntwo", encoding="utf-8")
        (self.root / "three.txt").write_text("one\ntwo\nthree", encoding="utf-8")

        self.assertEqual(collect_file_lines(self.root, min_lines=3), [("three.txt", 3)])
        self.assertEqual(len(collect_file_lines(self.root, min_lines=2)), 2)

    def test_generated_dependency_cache_and_artifact_directories_are_excluded(self) -> None:
        (self.root / "kept.txt").write_text("kept", encoding="utf-8")
        for directory_name in ("bin", "obj", "node_modules", "__pycache__", ".test-tmp"):
            generated_file = self.root / "nested" / directory_name / "ignored.txt"
            generated_file.parent.mkdir(parents=True)
            generated_file.write_text("ignored", encoding="utf-8")

        self.assertEqual(collect_file_lines(self.root), [("kept.txt", 1)])

    def test_cli_prints_relative_paths_and_applies_threshold(self) -> None:
        (self.root / "small.txt").write_text("one", encoding="utf-8")
        (self.root / "large.txt").write_text("one\ntwo", encoding="utf-8")
        output = io.StringIO()
        with mock.patch("scripts.report_file_lines.ROOT", self.root):
            with contextlib.redirect_stdout(output):
                self.assertEqual(main(["--min-lines", "2"]), 0)

        self.assertEqual(output.getvalue(), "      2  large.txt\n")


if __name__ == "__main__":
    unittest.main()