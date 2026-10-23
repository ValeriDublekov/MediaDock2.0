#!/usr/bin/env python3
"""Filter IMDb title ratings by average rating and vote count."""

from __future__ import annotations

import argparse
import csv
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = PROJECT_ROOT / "title.ratings.tsv"
DEFAULT_OUTPUT = PROJECT_ROOT / "title.ratings.filtered.tsv"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", nargs="?", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("-o", "--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--force", action="store_true")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    source = args.input.resolve()
    output = args.output.resolve()
    if not source.is_file():
        raise SystemExit(f"Input file not found: {source}")
    if source == output:
        raise SystemExit("Output path must not point to the input file.")
    if output.exists() and not args.force:
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
                if average_rating > 7 and num_votes > 60_000:
                    writer.writerow(row)
                    exported += 1

    print(f"Output: {output}")
    print(f"Source rows: {source_rows}")
    print(f"Rows excluded as invalid: {skipped_invalid}")
    print(f"Exported rows: {exported}")


if __name__ == "__main__":
    main()