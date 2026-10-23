#!/usr/bin/env python3
"""Filter IMDb ratings by title type and add title metadata using a merge join."""

from __future__ import annotations

import argparse
import csv
import os
import tempfile
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterator


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RATINGS = PROJECT_ROOT / "title.ratings.filtered.tsv"
DEFAULT_BASICS = PROJECT_ROOT / "title.basics.tsv"
DEFAULT_OUTPUT = PROJECT_ROOT / "title.ratings.filtered.movies-series.tsv"
DEFAULT_CATEGORIES_REPORT = PROJECT_ROOT / "title.basics.title-types.tsv"
DEFAULT_UNMATCHED_REPORT = PROJECT_ROOT / "title.ratings.filtered.movies-series.unmatched.tsv"
DEFAULT_TITLE_TYPES = ("movie", "tvSeries", "tvMiniSeries", "tvMovie")
TITLE_METADATA_FIELDS = ("titleType", "primaryTitle", "startYear", "genres")


@dataclass(frozen=True)
class JoinStats:
    rating_rows: int
    matched_rows: int
    exported_rows: int
    excluded_rows: int
    unmatched_rows: int
    invalid_ratings_rows: int
    invalid_basics_rows: int
    title_type_counts: dict[str, int]


@dataclass(frozen=True)
class BasicsRecord:
    tconst: str
    title_type: str
    row: list[str]
    valid: bool
    line_number: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ratings", type=Path, default=DEFAULT_RATINGS)
    parser.add_argument("--basics", type=Path, default=DEFAULT_BASICS)
    parser.add_argument("-o", "--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--categories-report",
        type=Path,
        default=DEFAULT_CATEGORIES_REPORT,
        help="TSV report of every titleType found in the basics file",
    )
    parser.add_argument(
        "--unmatched-report",
        type=Path,
        default=DEFAULT_UNMATCHED_REPORT,
        help="TSV report of rating rows without a valid basics match",
    )
    parser.add_argument(
        "--title-types",
        nargs="+",
        metavar="TYPE",
        default=DEFAULT_TITLE_TYPES,
        help="titleType values to include (default: movie tvSeries tvMiniSeries tvMovie)",
    )
    parser.add_argument("--force", action="store_true", help="overwrite existing output files")
    return parser.parse_args()


def _read_header(
    reader: Iterator[list[str]], source: Path, required_fields: set[str]
) -> tuple[list[str], dict[str, int]]:
    raw_header = next(reader, None)
    if raw_header is None:
        raise ValueError(f"Input file has no header row: {source}")

    header = [field.strip() for field in raw_header]
    if len(set(header)) != len(header):
        raise ValueError(f"Input file has duplicate column names: {source}")

    missing = sorted(required_fields.difference(header))
    if missing:
        raise ValueError(f"Input file is missing required columns: {', '.join(missing)} ({source})")
    return header, {field: index for index, field in enumerate(header)}


def _temporary_sibling(destination: Path) -> Path:
    destination.parent.mkdir(parents=True, exist_ok=True)
    file_descriptor, name = tempfile.mkstemp(
        prefix=f".{destination.name}.", suffix=".tmp", dir=destination.parent
    )
    os.close(file_descriptor)
    return Path(name)


def process_files(
    ratings_source: Path,
    basics_source: Path,
    output_path: Path,
    categories_report: Path,
    unmatched_report: Path,
    title_types: set[str],
    force: bool = False,
) -> JoinStats:
    ratings_source = ratings_source.resolve()
    basics_source = basics_source.resolve()
    output_path = output_path.resolve()
    categories_report = categories_report.resolve()
    unmatched_report = unmatched_report.resolve()

    for source in (ratings_source, basics_source):
        if not source.is_file():
            raise FileNotFoundError(f"Input file not found: {source}")

    source_paths = {ratings_source, basics_source}
    destination_paths = (output_path, categories_report, unmatched_report)
    if any(destination in source_paths for destination in destination_paths):
        raise ValueError("Output paths must not point to an input file.")
    if len(set(destination_paths)) != len(destination_paths):
        raise ValueError("Output and report paths must be different.")

    for destination in destination_paths:
        if destination.exists() and not force:
            raise FileExistsError(f"Output file already exists: {destination}")

    selected_types = {title_type.strip() for title_type in title_types if title_type.strip()}
    if not selected_types:
        raise ValueError("At least one non-empty title type must be selected.")

    temporary_paths: list[Path] = []
    try:
        with (
            ratings_source.open("r", encoding="utf-8-sig", newline="") as ratings_file,
            basics_source.open("r", encoding="utf-8-sig", newline="") as basics_file,
        ):
            ratings_reader = csv.reader(ratings_file, delimiter="\t")
            basics_reader = csv.reader(basics_file, delimiter="\t")
            ratings_header, ratings_columns = _read_header(
                ratings_reader, ratings_source, {"tconst"}
            )
            basics_header, basics_columns = _read_header(
                basics_reader,
                basics_source,
                {"tconst", *TITLE_METADATA_FIELDS},
            )

            output_header = list(ratings_header)
            output_header.extend(
                field for field in TITLE_METADATA_FIELDS if field not in ratings_columns
            )
            unmatched_header = list(ratings_header)
            unmatched_header.extend(
                field for field in ("basicsTitleType", "matchIssue") if field not in ratings_columns
            )
            basics_tconst_index = basics_columns["tconst"]
            basics_type_index = basics_columns["titleType"]

            temporary_output = _temporary_sibling(output_path)
            temporary_paths.append(temporary_output)
            temporary_report = _temporary_sibling(categories_report)
            temporary_paths.append(temporary_report)
            temporary_unmatched = _temporary_sibling(unmatched_report)
            temporary_paths.append(temporary_unmatched)

            category_counts: Counter[str] = Counter()
            invalid_basics_rows = 0
            rating_rows = matched_rows = exported_rows = excluded_rows = unmatched_rows = 0
            invalid_ratings_rows = 0

            def iter_basics_rows() -> Iterator[BasicsRecord]:
                nonlocal invalid_basics_rows
                for line_number, row in enumerate(basics_reader, start=2):
                    title_type = (
                        row[basics_type_index].strip()
                        if len(row) > basics_type_index
                        else ""
                    )
                    category_counts[title_type or "(empty)"] += 1
                    tconst = (
                        row[basics_tconst_index].strip()
                        if len(row) > basics_tconst_index
                        else ""
                    )
                    valid = (
                        len(row) == len(basics_header)
                        and bool(tconst)
                        and bool(title_type)
                    )
                    if not valid:
                        invalid_basics_rows += 1
                    if tconst:
                        yield BasicsRecord(tconst, title_type, row, valid, line_number)

            def unmatched_row(
                ratings_row: list[str], match_issue: str, basics_title_type: str = ""
            ) -> list[str]:
                values = {
                    field: ratings_row[index] if index < len(ratings_row) else ""
                    for field, index in ratings_columns.items()
                }
                values["basicsTitleType"] = basics_title_type
                values["matchIssue"] = match_issue
                return [values.get(field, "") for field in unmatched_header]

            with (
                temporary_output.open("w", encoding="utf-8", newline="") as output_file,
                temporary_report.open("w", encoding="utf-8", newline="") as report_file,
                temporary_unmatched.open("w", encoding="utf-8", newline="") as unmatched_file,
            ):
                output_writer = csv.writer(
                    output_file, delimiter="\t", lineterminator="\n"
                )
                report_writer = csv.writer(
                    report_file, delimiter="\t", lineterminator="\n"
                )
                unmatched_writer = csv.writer(
                    unmatched_file, delimiter="\t", lineterminator="\n"
                )
                output_writer.writerow(output_header)
                unmatched_writer.writerow(unmatched_header)

                basics_rows = iter_basics_rows()

                def advance_basics() -> BasicsRecord | None:
                    return next(basics_rows, None)

                basics_row = advance_basics()
                for ratings_row in ratings_reader:
                    rating_rows += 1
                    ratings_tconst = (
                        ratings_row[ratings_columns["tconst"]].strip()
                        if len(ratings_row) > ratings_columns["tconst"]
                        else ""
                    )
                    if len(ratings_row) != len(ratings_header) or not ratings_tconst:
                        invalid_ratings_rows += 1
                        if ratings_tconst:
                            unmatched_rows += 1
                            unmatched_writer.writerow(
                                unmatched_row(
                                    ratings_row,
                                    f"Malformed ratings row ({len(ratings_row)} fields)",
                                )
                            )
                        continue
                    ratings_row[ratings_columns["tconst"]] = ratings_tconst

                    while (
                        basics_row is not None
                        and basics_row.tconst < ratings_tconst
                    ):
                        basics_row = advance_basics()

                    if basics_row is None or basics_row.tconst != ratings_tconst:
                        unmatched_rows += 1
                        unmatched_writer.writerow(
                            unmatched_row(ratings_row, "No tconst match at the current merge position")
                        )
                        continue

                    if not basics_row.valid:
                        unmatched_rows += 1
                        unmatched_writer.writerow(
                            unmatched_row(
                                ratings_row,
                                f"Malformed basics row at record {basics_row.line_number} ({len(basics_row.row)} fields)",
                                basics_row.title_type,
                            )
                        )
                        basics_row = advance_basics()
                        continue

                    matched_rows += 1
                    if basics_row.title_type not in selected_types:
                        excluded_rows += 1
                        basics_row = advance_basics()
                        continue

                    output_row = list(ratings_row)
                    for field in TITLE_METADATA_FIELDS:
                        value = basics_row.row[basics_columns[field]]
                        if field in ratings_columns:
                            output_row[ratings_columns[field]] = value
                        else:
                            output_row.append(value)
                    output_writer.writerow(output_row)
                    exported_rows += 1
                    basics_row = advance_basics()

                while basics_row is not None:
                    basics_row = advance_basics()

                report_writer.writerow(("titleType", "count"))
                for title_type, count in sorted(category_counts.items()):
                    report_writer.writerow((title_type, count))

            temporary_output.replace(output_path)
            temporary_report.replace(categories_report)
            temporary_unmatched.replace(unmatched_report)

            return JoinStats(
                rating_rows=rating_rows,
                matched_rows=matched_rows,
                exported_rows=exported_rows,
                excluded_rows=excluded_rows,
                unmatched_rows=unmatched_rows,
                invalid_ratings_rows=invalid_ratings_rows,
                invalid_basics_rows=invalid_basics_rows,
                title_type_counts=dict(sorted(category_counts.items())),
            )
    finally:
        for temporary_path in temporary_paths:
            temporary_path.unlink(missing_ok=True)


def main() -> None:
    args = parse_args()
    selected_types = {title_type.strip() for title_type in args.title_types if title_type.strip()}
    try:
        stats = process_files(
            args.ratings,
            args.basics,
            args.output,
            args.categories_report,
            args.unmatched_report,
            selected_types,
            force=args.force,
        )
    except (OSError, ValueError) as error:
        raise SystemExit(str(error)) from error

    print(f"Output: {args.output.resolve()}")
    print(f"Categories report: {args.categories_report.resolve()}")
    print(f"Included title types: {', '.join(sorted(selected_types))}")
    print(f"Filtered ratings rows: {stats.rating_rows}")
    print(f"Rows matched to basics: {stats.matched_rows}")
    print(f"Rows excluded by title type: {stats.excluded_rows}")
    print(f"Unmatched rating rows: {stats.unmatched_rows}")
    print(f"Malformed ratings rows skipped: {stats.invalid_ratings_rows}")
    print(f"Malformed basics rows skipped: {stats.invalid_basics_rows}")
    print(f"Rows exported: {stats.exported_rows}")
    if stats.unmatched_rows:
        print(f"Unmatched titles report: {args.unmatched_report.resolve()}")
    else:
        print("No unmatched title IDs.")
    print("Categories found in title.basics.tsv:")
    for title_type, count in stats.title_type_counts.items():
        print(f"  {title_type or '(empty)'}\t{count}")


if __name__ == "__main__":
    main()