#!/usr/bin/env python3
"""Print a compact, top-N Lizard complexity report."""

from __future__ import annotations

import argparse
import csv
import io
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PATHS = ("server/src", "web/src")
EXCLUDED_PATTERNS = (
    "*/obj/*",
    "*/bin/*",
    "*/Migrations/*.Designer.cs",
    "*/Migrations/MediaDockDbContextModelSnapshot.cs",
)
COGNITIVE_COMPLEXITY_COLUMN = 11


@dataclass(frozen=True)
class ComplexityResult:
    nloc: int
    cyclomatic_complexity: int
    parameter_count: int
    function_name: str
    file_path: str
    start_line: int
    cognitive_complexity: int


def positive_int(value: str) -> int:
    try:
        parsed = int(value)
    except ValueError as error:
        raise argparse.ArgumentTypeError("must be a positive integer") from error
    if parsed < 1:
        raise argparse.ArgumentTypeError("must be a positive integer")
    return parsed


def parse_lizard_csv(output: str) -> list[ComplexityResult]:
    results: list[ComplexityResult] = []
    for row in csv.reader(io.StringIO(output)):
        if not row:
            continue
        if len(row) <= COGNITIVE_COMPLEXITY_COLUMN:
            raise ValueError(f"Unexpected Lizard CSV row with {len(row)} columns")
        try:
            results.append(ComplexityResult(
                nloc=int(row[0]),
                cyclomatic_complexity=int(row[1]),
                parameter_count=int(row[3]),
                function_name=row[7],
                file_path=row[6],
                start_line=int(row[9]),
                cognitive_complexity=int(row[COGNITIVE_COMPLEXITY_COLUMN]),
            ))
        except ValueError as error:
            raise ValueError(f"Invalid numeric value in Lizard CSV row: {row[5]}") from error
    return results


def resolve_paths(paths: Sequence[str]) -> list[str]:
    requested_paths = paths or DEFAULT_PATHS
    resolved_paths: list[str] = []
    for value in requested_paths:
        path = Path(value).expanduser()
        if not path.is_absolute():
            path = ROOT / path
        path = path.resolve()
        if not path.exists():
            raise ValueError(f"Path does not exist: {value}")
        try:
            resolved_paths.append(path.relative_to(ROOT).as_posix())
        except ValueError:
            resolved_paths.append(str(path))
    return resolved_paths


def run_lizard(paths: Sequence[str]) -> list[ComplexityResult]:
    command = [sys.executable, "-m", "lizard", "-Ecognitive", "--csv", "-i", "-1"]
    for pattern in EXCLUDED_PATTERNS:
        command.extend(("-x", pattern))
    command.extend(paths)
    completed = subprocess.run(
        command,
        cwd=ROOT,
        capture_output=True,
        check=False,
        encoding="utf-8",
        errors="replace",
    )
    if completed.returncode != 0:
        detail = completed.stderr.strip() or completed.stdout.strip()
        raise RuntimeError(detail or f"Lizard exited with status {completed.returncode}")
    return parse_lizard_csv(completed.stdout)


def display_path(value: str) -> str:
    path = Path(value)
    if not path.is_absolute():
        return path.as_posix()
    try:
        return path.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return str(path)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "paths",
        nargs="*",
        metavar="PATH",
        help="file or directory to analyze (default: server/src and web/src)",
    )
    parser.add_argument(
        "--top",
        type=positive_int,
        default=10,
        metavar="N",
        help="number of highest-complexity functions to print (default: 10)",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        paths = resolve_paths(args.paths)
        results = run_lizard(paths)
    except (OSError, RuntimeError, ValueError) as error:
        print(f"Complexity analysis failed: {error}", file=sys.stderr)
        return 1

    results.sort(key=lambda item: (
        -item.cognitive_complexity,
        -item.cyclomatic_complexity,
        -item.nloc,
        item.file_path,
        item.start_line,
    ))
    selected = results[:args.top]
    print(f"Top {len(selected)} of {len(results)} functions by Cognitive Complexity")
    print("CogC  CCN  NLOC  PARAM  Function (file:line)")
    for result in selected:
        location = f"{display_path(result.file_path)}:{result.start_line}"
        print(
            f"{result.cognitive_complexity:>4}  "
            f"{result.cyclomatic_complexity:>3}  "
            f"{result.nloc:>4}  "
            f"{result.parameter_count:>5}  "
            f"{result.function_name} ({location})"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())