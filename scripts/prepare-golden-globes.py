#!/usr/bin/env python3
"""Create a compact Golden Globes CSV suitable for the MediaDock importer."""

from __future__ import annotations

import argparse
import csv
from pathlib import Path


OUTPUT_COLUMNS = ("id", "nominee_type", "year", "winner", "award", "title")
TITLE_TYPES = {"film", "movie", "series", "tv-show"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("-o", "--output", type=Path)
    parser.add_argument("--year-after", type=int, default=1980)
    parser.add_argument("--force", action="store_true")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    source = args.input.resolve()
    output = (args.output or source.with_name(f"golden_globes_after_{args.year_after}_compact.csv")).resolve()
    if not source.is_file():
        raise SystemExit(f"Input file not found: {source}")
    if source == output:
        raise SystemExit("Output path must not point to the input file.")
    if output.exists() and not args.force:
        raise SystemExit(f"Output file already exists: {output}")

    output.parent.mkdir(parents=True, exist_ok=True)
    source_rows = year_rows = exported = skipped_type = skipped_invalid = skipped_duplicate = 0
    seen_keys: set[tuple[int, bool, str, str]] = set()

    with source.open("r", encoding="utf-8-sig", newline="") as input_file:
        sample = input_file.read(8192)
        input_file.seek(0)
        try:
            dialect = csv.Sniffer().sniff(sample, delimiters=",\t")
        except csv.Error:
            dialect = csv.excel
        reader = csv.DictReader(input_file, dialect=dialect)
        if reader.fieldnames is None:
            raise SystemExit("Input file has no header row.")
        reader.fieldnames = [
            "id" if index == 0 and not name.strip() else name.strip().lstrip("\ufeff")
            for index, name in enumerate(reader.fieldnames)
        ]
        missing = [column for column in OUTPUT_COLUMNS if column not in reader.fieldnames]
        if missing:
            raise SystemExit(f"Input file is missing required columns: {', '.join(missing)}")

        with output.open("w", encoding="utf-8", newline="") as output_file:
            writer = csv.DictWriter(output_file, fieldnames=OUTPUT_COLUMNS)
            writer.writeheader()
            for row in reader:
                source_rows += 1
                try:
                    year = int((row.get("year") or "").strip())
                except ValueError:
                    skipped_invalid += 1
                    continue
                if year <= args.year_after:
                    continue
                year_rows += 1
                nominee_type = (row.get("nominee_type") or "").strip().lower()
                if nominee_type not in TITLE_TYPES:
                    skipped_type += 1
                    continue
                title = (row.get("title") or "").strip()
                award = (row.get("award") or "").strip()
                if not title or not award:
                    skipped_invalid += 1
                    continue
                winner = (row.get("winner") or "").strip()
                key = (year, winner.lower() in {"true", "yes", "1"}, award, title)
                if key in seen_keys:
                    skipped_duplicate += 1
                    continue
                seen_keys.add(key)
                writer.writerow({
                    "id": (row.get("id") or "").strip(),
                    "nominee_type": nominee_type,
                    "year": year,
                    "winner": winner,
                    "award": award,
                    "title": title,
                })
                exported += 1

    print(f"Output: {output}")
    print(f"Source rows: {source_rows}")
    print(f"Rows with Year > {args.year_after}: {year_rows}")
    print(f"Rows excluded by nominee type: {skipped_type}")
    print(f"Rows excluded as invalid: {skipped_invalid}")
    print(f"Rows excluded as duplicates: {skipped_duplicate}")
    print(f"Exported nomination rows: {exported}")


if __name__ == "__main__":
    main()
