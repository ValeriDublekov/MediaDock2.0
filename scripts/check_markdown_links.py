#!/usr/bin/env python3
"""Check local inline Markdown links and heading anchors in the repository."""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit


ROOT = Path(__file__).resolve().parents[1]
SKIP_DIRECTORIES = frozenset({
    ".git",
    ".test-tmp",
    ".venv",
    "__pycache__",
    "bin",
    "build",
    "dist",
    "node_modules",
    "obj",
})
LINK_PATTERN = re.compile(r"!?\[[^\]]*\]\(\s*(?:<([^>]+)>|([^\s)]+))(?:\s+[^)]*)?\)")
FENCE_START_PATTERN = re.compile(r"^ {0,3}(`{3,}|~{3,})")
FENCE_END_PATTERN = re.compile(r"^ {0,3}(`{3,}|~{3,})\s*$")
HEADING_PATTERN = re.compile(r"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$")
HTML_ANCHOR_PATTERN = re.compile(r"\b(?:id|name)\s*=\s*(?:\"([^\"]+)\"|'([^']+)')", re.IGNORECASE)
INLINE_CODE_PATTERN = re.compile(r"(?<!`)(`+)(?!`)(.*?)(?<!`)\1(?!`)", re.DOTALL)


def iter_markdown_files(root: Path) -> list[Path]:
    """Return Markdown files beneath root, excluding generated directories."""
    files: list[Path] = []
    for directory, child_directories, filenames in os.walk(root, topdown=True):
        child_directories[:] = sorted(
            name for name in child_directories if name not in SKIP_DIRECTORIES
        )
        files.extend(
            Path(directory) / filename
            for filename in sorted(filenames)
            if filename.lower().endswith((".md", ".markdown"))
        )
    return sorted(files)


def mask_fenced_code(content: str) -> str:
    """Mask fenced and inline code while preserving line endings."""
    masked_lines: list[str] = []
    active_fence: tuple[str, int] | None = None

    for line in content.splitlines(keepends=True):
        marker = FENCE_START_PATTERN.match(line)
        if active_fence is not None:
            if marker:
                fence = marker.group(1)
                if fence[0] == active_fence[0] and len(fence) >= active_fence[1]:
                    if FENCE_END_PATTERN.match(line):
                        active_fence = None
            masked_lines.append("".join("\n" if char == "\n" else "\r" if char == "\r" else " " for char in line))
            continue

        if marker:
            fence = marker.group(1)
            active_fence = (fence[0], len(fence))
            masked_lines.append("".join("\n" if char == "\n" else "\r" if char == "\r" else " " for char in line))
        else:
            masked_lines.append(line)

    masked_content = "".join(masked_lines)
    return INLINE_CODE_PATTERN.sub(
        lambda match: re.sub(r"[^\r\n]", " ", match.group(0)),
        masked_content,
    )


def heading_slug(text: str) -> str:
    """Create the GitHub-style fragment for a Markdown heading."""
    text = re.sub(r"`([^`]*)`", r"\1", text)
    text = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text).lower().strip()
    text = re.sub(r"[^\w\- ]", "", text)
    return re.sub(r"\s+", "-", text)


def collect_anchors(path: Path) -> set[str]:
    """Collect generated heading fragments and explicit HTML anchors."""
    content = mask_fenced_code(path.read_text(encoding="utf-8"))
    anchors: set[str] = set()
    duplicate_counts: dict[str, int] = {}

    for line in content.splitlines():
        heading = HEADING_PATTERN.match(line)
        if heading:
            base = heading_slug(heading.group(1))
            duplicate_count = duplicate_counts.get(base, 0)
            anchors.add(base if duplicate_count == 0 else f"{base}-{duplicate_count}")
            duplicate_counts[base] = duplicate_count + 1

        for match in HTML_ANCHOR_PATTERN.finditer(line):
            anchors.add(match.group(1) or match.group(2))

    return anchors


def display_path(path: Path, root: Path) -> str:
    try:
        return path.relative_to(root).as_posix()
    except ValueError:
        return path.as_posix()


def check_markdown_links(root: Path) -> tuple[int, list[str]]:
    """Return the number of local links checked and any validation errors."""
    root = root.resolve()
    markdown_files = iter_markdown_files(root)
    anchor_cache: dict[Path, set[str]] = {}
    errors: list[str] = []
    checked_links = 0

    for source in markdown_files:
        content = source.read_text(encoding="utf-8")
        markdown = mask_fenced_code(content)
        for match in LINK_PATTERN.finditer(markdown):
            destination = match.group(1) or match.group(2)
            parsed = urlsplit(destination)
            if parsed.scheme or parsed.netloc:
                continue

            target_path = unquote(parsed.path)
            if target_path.startswith("/"):
                target = (root / target_path.lstrip("/")).resolve()
            else:
                target = (source.parent / target_path).resolve() if target_path else source
            checked_links += 1
            line_number = content.count("\n", 0, match.start()) + 1
            source_name = display_path(source, root)

            if not target.exists():
                errors.append(f"{source_name}:{line_number}: missing target {destination}")
                continue

            fragment = unquote(parsed.fragment)
            if not fragment or not target.is_file() or target.suffix.lower() not in {".md", ".markdown"}:
                continue

            if target not in anchor_cache:
                anchor_cache[target] = collect_anchors(target)
            if fragment not in anchor_cache[target]:
                errors.append(f"{source_name}:{line_number}: missing anchor {destination}")

    return checked_links, errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=ROOT,
        help="repository root to scan (default: the repository containing this script)",
    )
    args = parser.parse_args(argv)
    if not args.root.is_dir():
        parser.error(f"not a directory: {args.root}")

    checked_links, errors = check_markdown_links(args.root)
    markdown_files = len(iter_markdown_files(args.root.resolve()))
    print(f"Checked {checked_links} local Markdown links across {markdown_files} Markdown files.")
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print("All local targets and Markdown anchors resolve.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
