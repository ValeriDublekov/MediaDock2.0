import contextlib
import io
import tempfile
import unittest
from pathlib import Path

from scripts.check_markdown_links import check_markdown_links, main


class MarkdownLinkCheckerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)

    def write_markdown(self, relative_path: str, content: str) -> Path:
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def test_validates_local_pages_anchors_directories_and_images(self) -> None:
        self.write_markdown(
            "source.md",
            "\n".join((
                "# Source",
                "[heading](guide.md#heading-one)",
                "[folder](assets/)",
                "[external](https://example.com/guide)",
                "![photo](assets/photo.png)",
                "[html anchor](guide.md#custom-anchor)",
                "[duplicate heading](guide.md#repeated-1)",
                "Inline example: `[ignored](missing-from-code-span.md)`",
                "```markdown",
                "[ignored](missing-from-code.md)",
                "```",
            )),
        )
        self.write_markdown(
            "guide.md",
            "\n".join((
                "# Heading One",
                '<a id="custom-anchor"></a>',
                "# Repeated",
                "# Repeated",
            )),
        )
        (self.root / "assets").mkdir()
        (self.root / "assets" / "photo.png").write_bytes(b"image")

        checked_links, errors = check_markdown_links(self.root)

        self.assertEqual(checked_links, 5)
        self.assertEqual(errors, [])

    def test_reports_missing_targets_and_anchors_but_ignores_fenced_code(self) -> None:
        self.write_markdown(
            "source.md",
            "\n".join((
                "[missing target](missing.md)",
                "[missing anchor](target.md#not-here)",
                "```markdown",
                "[ignored target](also-missing.md)",
                "```",
            )),
        )
        self.write_markdown("target.md", "# Existing\n")

        checked_links, errors = check_markdown_links(self.root)

        self.assertEqual(checked_links, 2)
        self.assertEqual(len(errors), 2)
        self.assertIn("missing target missing.md", errors[0])
        self.assertIn("missing anchor target.md#not-here", errors[1])

    def test_cli_reports_repository_scan_summary(self) -> None:
        self.write_markdown("source.md", "# Source\n")
        output = io.StringIO()

        with contextlib.redirect_stdout(output):
            result = main(["--root", str(self.root)])

        self.assertEqual(result, 0)
        self.assertIn("Checked 0 local Markdown links across 1 Markdown files.", output.getvalue())
        self.assertIn("All local targets and Markdown anchors resolve.", output.getvalue())


if __name__ == "__main__":
    unittest.main()
