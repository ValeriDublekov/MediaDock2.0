#!/usr/bin/env python3
"""Report line counts for text files in the MediaDock project."""

from __future__ import annotations

import argparse
import os
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CHUNK_SIZE = 1024 * 1024
SKIP_DIRECTORIES = frozenset({
    ".git",
    ".next",
    ".nox",
    ".pytest_cache",
    ".ruff_cache",
    ".test-tmp",
    ".tox",
    ".venv",
    ".vite",
    "__pycache__",
    "bin",
    "build",
    "coverage",
    "dist",
    "node_modules",
    "obj",
    "playwright-report",
    "test-results",
    "venv",
})


def count_file_lines(path: Path) -> int | None:
    """Return a file's logical line count, or None for binary/unreadable files."""
    line_breaks = 0
    last_byte: int | None = None

    try:
        with path.open("rb") as file:
            while chunk := file.read(CHUNK_SIZE):
                if b"\0" in chunk:
                    return None
                line_breaks += chunk.count(b"\n") + chunk.count(b"\r") - chunk.count(b"\r\n")
                if last_byte == 13 and chunk[0] == 10:
                    line_breaks -= 1
                last_byte = chunk[-1]
    except OSError:
        return None

    if last_byte is None:
        return 0
    return line_breaks + int(last_byte not in (10, 13))


def collect_file_lines(root: Path = ROOT, min_lines: int = 0) -> list[tuple[str, int]]:
    """Collect relative paths and line counts in deterministic descending order."""
    inventory: list[tuple[str, int]] = []
    for directory, child_directories, filenames in os.walk(root, topdown=True):
        child_directories[:] = sorted(
            name for name in child_directories if name not in SKIP_DIRECTORIES
        )
        for filename in sorted(filenames):
            path = Path(directory) / filename
            if path.is_symlink():
                continue
            line_count = count_file_lines(path)
            if line_count is not None and line_count >= min_lines:
                inventory.append((path.relative_to(root).as_posix(), line_count))

    inventory.sort(key=lambda item: (-item[1], item[0]))
    return inventory


def non_negative_int(value: str) -> int:
    try:
        parsed = int(value)
    except ValueError as error:
        raise argparse.ArgumentTypeError("must be an integer") from error
    if parsed < 0:
        raise argparse.ArgumentTypeError("must be zero or greater")
    return parsed


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--min-lines",
        type=non_negative_int,
        default=0,
        metavar="N",
        help="show only files with at least N lines",
    )
    args = parser.parse_args(argv)

    for relative_path, line_count in collect_file_lines(ROOT, args.min_lines):
        print(f"{line_count:>7}  {relative_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())