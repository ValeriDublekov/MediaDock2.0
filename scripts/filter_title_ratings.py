#!/usr/bin/env python3
"""Filter IMDb title ratings by average rating and vote count."""

from __future__ import annotations

import argparse
import csv
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = PROJECT_ROOT / "title.ratings.tsv"
DEFAULT_OUTPUT = PROJECT_ROOT / "title.ratings.filtered.tsv"
DEFAULT_MIN_RATING = 7.0
DEFAULT_MIN_VOTES = 60_000


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", nargs="?", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("-o", "--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--min-rating",
        type=float,
        default=DEFAULT_MIN_RATING,
        help="keep rows with averageRating greater than this value (default: 7)",
    )
    parser.add_argument(
        "--min-votes",
        type=int,
        default=DEFAULT_MIN_VOTES,
        help="keep rows with numVotes greater than this value (default: 60000)",
    )
    parser.add_argument("--force", action="store_true")
    return parser.parse_args()


def filter_file(
    source: Path,
    output: Path,
    min_rating: float = DEFAULT_MIN_RATING,
    min_votes: int = DEFAULT_MIN_VOTES,
    force: bool = False,
) -> tuple[int, int, int]:
    source = source.resolve()
    output = output.resolve()
    if not source.is_file():
        raise SystemExit(f"Input file not found: {source}")
    if source == output:
        raise SystemExit("Output path must not point to the input file.")
    if output.exists() and not force:
        raise SystemExit(f"Output file already exists: {output}")

    source_rows = exported = skipped_invalid = 0
    with source.open("r", encoding="utf-8-sig", newline="") as input_file:
        reader = csv.DictReader(input_file, delimiter="\t")
        if reader.fieldnames is None:
            raise SystemExit("Input file has no header row.")
        reader.fieldnames = [name.strip() for name in reader.fieldnames]
        required = {"averageRating", "numVotes"}
        missing = sorted(required.difference(reader.fieldnames))
        if missing:
            raise SystemExit(f"Input file is missing required columns: {', '.join(missing)}")

        with output.open("w", encoding="utf-8", newline="") as output_file:
            writer = csv.DictWriter(
                output_file,
                fieldnames=reader.fieldnames,
                delimiter="\t",
                lineterminator="\n",
            )
            writer.writeheader()
            for row in reader:
                source_rows += 1
                try:
                    average_rating = float(row.get("averageRating") or "")
                    num_votes = int(row.get("numVotes") or "")
                except ValueError:
                    skipped_invalid += 1
                    continue
                if average_rating > min_rating and num_votes > min_votes:
                    writer.writerow(row)
                    exported += 1

    return source_rows, skipped_invalid, exported


def main() -> None:
    args = parse_args()
    source = args.input.resolve()
    output = args.output.resolve()
    source_rows, skipped_invalid, exported = filter_file(
        source,
        output,
        min_rating=args.min_rating,
        min_votes=args.min_votes,
        force=args.force,
    )
    print(f"Output: {output}")
    print(f"Source rows: {source_rows}")
    print(f"Rows excluded as invalid: {skipped_invalid}")
    print(f"Minimum average rating (exclusive): {args.min_rating}")
    print(f"Minimum vote count (exclusive): {args.min_votes}")
    print(f"Exported rows: {exported}")


if __name__ == "__main__":
    main()