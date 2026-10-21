#!/usr/bin/env python3
"""Inspect Git diffs without flooding command output for large changes."""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_MAX_LINES = 300
DEFAULT_MAX_FILE_LINES = 120
DEFAULT_PAGE_LINES = 120


@dataclass(frozen=True)
class DiffEntry:
    path: str
    additions: int | None
    deletions: int | None

    @property
    def changed_lines(self) -> int | None:
        if self.additions is None or self.deletions is None:
            return None
        return self.additions + self.deletions


def positive_int(value: str) -> int:
    number = int(value)
    if number < 1:
        raise argparse.ArgumentTypeError("must be a positive integer")
    return number


def parse_numstat(output: bytes) -> list[DiffEntry]:
    entries = []
    for record in output.split(b"\0"):
        if not record:
            continue
        additions, deletions, path = record.split(b"\t", 2)
        entries.append(DiffEntry(
            os.fsdecode(path),
            None if additions == b"-" else int(additions),
            None if deletions == b"-" else int(deletions),
        ))
    return entries


def git_diff_command(paths: list[str], *, numstat: bool) -> list[str]:
    command = ["git", "diff"]
    if numstat:
        command.extend(["--numstat", "-z"])
    else:
        command.extend(["--no-color", "--unified=3"])
    command.extend(["--no-ext-diff", "--no-renames", "HEAD", "--", *paths])
    return command


def run_numstat(paths: list[str]) -> list[DiffEntry]:
    completed = subprocess.run(
        git_diff_command(paths, numstat=True),
        cwd=ROOT,
        capture_output=True,
        check=False,
    )
    if completed.returncode != 0:
        message = completed.stderr.decode("utf-8", errors="replace").strip()
        raise RuntimeError(message or f"git diff exited with status {completed.returncode}")
    return parse_numstat(completed.stdout)


def write_diff_snapshot(paths: list[str]) -> Path:
    artifact_dir = ROOT / ".test-tmp"
    artifact_dir.mkdir(parents=True, exist_ok=True)
    descriptor, file_name = tempfile.mkstemp(prefix="mediadock-diff-", suffix=".patch", dir=artifact_dir)
    snapshot = Path(file_name)
    try:
        with os.fdopen(descriptor, "wb") as output:
            completed = subprocess.run(
                git_diff_command(paths, numstat=False),
                cwd=ROOT,
                stdout=output,
                stderr=subprocess.PIPE,
                check=False,
            )
        if completed.returncode != 0:
            message = completed.stderr.decode("utf-8", errors="replace").strip()
            raise RuntimeError(message or f"git diff exited with status {completed.returncode}")
    except Exception:
        snapshot.unlink(missing_ok=True)
        raise
    return snapshot


def count_lines(path: Path) -> int:
    with path.open(encoding="utf-8", errors="replace") as source:
        return sum(1 for _ in source)


def display_diff(path: Path, start_line: int = 1, line_count: int | None = None) -> int:
    displayed = 0
    with path.open(encoding="utf-8", errors="replace") as source:
        for number, line in enumerate(source, start=1):
            if number < start_line:
                continue
            if line_count is not None and displayed >= line_count:
                break
            sys.stdout.write(line)
            displayed += 1
    return displayed


def print_summary(entries: list[DiffEntry], total_lines: int | None = None, snapshot: Path | None = None) -> None:
    additions = sum(entry.additions or 0 for entry in entries)
    deletions = sum(entry.deletions or 0 for entry in entries)
    print(f"Diff summary against HEAD: {len(entries)} file(s), +{additions}/-{deletions} changed lines")
    if total_lines is not None:
        print(f"Rendered diff: {total_lines} lines")

    print("  Add  Del  Changed  File")
    ordered = sorted(
        entries,
        key=lambda entry: (entry.changed_lines if entry.changed_lines is not None else -1, entry.path),
        reverse=True,
    )
    for entry in ordered:
        if entry.changed_lines is None:
            print(f"  {'-':>3}  {'-':>3}  {'binary':>7}  {entry.path}")
        else:
            print(f"  {entry.additions:>3}  {entry.deletions:>3}  {entry.changed_lines:>7}  {entry.path}")
    if snapshot is not None:
        print(f"Full diff snapshot: {snapshot.relative_to(ROOT).as_posix()}")
    print("Inspect selected files with --file PATH; repeat --file to select several files.")
    print("For a long file, add --start-line N --lines N to print a page.")


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--file", action="append", default=[], help="Limit output to a changed file; may be repeated")
    parser.add_argument("--list", action="store_true", help="Print per-file change sizes without the diff")
    parser.add_argument("--max-lines", type=positive_int, default=DEFAULT_MAX_LINES,
                        help=f"Summarize instead of printing above this many diff lines (default: {DEFAULT_MAX_LINES})")
    parser.add_argument("--max-file-lines", type=positive_int, default=DEFAULT_MAX_FILE_LINES,
                        help=f"Summarize if one file changes more than this many lines (default: {DEFAULT_MAX_FILE_LINES})")
    parser.add_argument("--start-line", type=positive_int, help="First diff line to print; requires exactly one --file")
    parser.add_argument("--lines", type=positive_int, default=DEFAULT_PAGE_LINES,
                        help=f"Maximum diff lines to print with --start-line (default: {DEFAULT_PAGE_LINES})")
    args = parser.parse_args(argv)
    if args.start_line is not None and len(args.file) != 1:
        parser.error("--start-line requires exactly one --file")
    if args.list and args.start_line is not None:
        parser.error("--list cannot be combined with --start-line")
    args.file = [path.replace("\\", "/") for path in args.file]
    return args


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        entries = run_numstat(args.file)
        if not entries:
            label = "selected file(s)" if args.file else "tracked files"
            print(f"No diff against HEAD for {label}.")
            return 0
        if args.list:
            print_summary(entries)
            return 0

        snapshot = write_diff_snapshot(args.file)
        total_lines = count_lines(snapshot)
        if total_lines == 0:
            snapshot.unlink(missing_ok=True)
            print("No diff content was produced.")
            return 0

        if args.start_line is not None:
            page_count = display_diff(snapshot, args.start_line, args.lines)
            last_line = args.start_line + page_count - 1
            print(f"\nDiff lines {args.start_line}-{last_line} of {total_lines}.")
            print(f"Snapshot: {snapshot.relative_to(ROOT).as_posix()}")
            return 0

        oversized_files = [entry for entry in entries
                           if entry.changed_lines is not None and entry.changed_lines > args.max_file_lines]
        if total_lines > args.max_lines or oversized_files:
            print_summary(entries, total_lines, snapshot)
        else:
            display_diff(snapshot)
            snapshot.unlink(missing_ok=True)
        return 0
    except (OSError, RuntimeError, ValueError) as error:
        print(f"Could not inspect diff: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())