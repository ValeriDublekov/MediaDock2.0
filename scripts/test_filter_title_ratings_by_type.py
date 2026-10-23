from __future__ import annotations

import csv
import tempfile
import unittest
from pathlib import Path

from scripts.filter_title_ratings_by_type import DEFAULT_TITLE_TYPES, process_files


class FilterTitleRatingsByTypeTests(unittest.TestCase):
    def test_merge_join_exports_selected_types_and_reports_all_categories(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            ratings_path = root / "ratings.tsv"
            basics_path = root / "basics.tsv"
            output_path = root / "filtered.tsv"
            report_path = root / "categories.tsv"
            unmatched_path = root / "unmatched.tsv"

            self._write_tsv(
                ratings_path,
                ("tconst", "averageRating", "numVotes"),
                (
                    ("tt0000000", "8.0", "70000"),
                    ("tt0000001", "8.1", "71000"),
                    ("tt0000002", "8.2", "72000"),
                    ("tt0000003", "8.3", "73000"),
                    ("tt0000004", "8.4", "74000"),
                    ("tt0000006", "8.5", "75000"),
                ),
            )
            self._write_tsv(
                basics_path,
                (
                    "tconst",
                    "titleType",
                    "primaryTitle",
                    "originalTitle",
                    "isAdult",
                    "startYear",
                    "endYear",
                    "runtimeMinutes",
                    "genres",
                ),
                (
                    ("tt0000001", "short", "Short", "Short", "0", "2000", "\\N", "5", "Drama"),
                    ("tt0000002", "movie", "Film", "Film", "0", "2001", "\\N", "90", "Drama,Comedy"),
                    ("tt0000003", "tvEpisode", "Episode", "Episode", "0", "2002", "\\N", "30", "Comedy"),
                    ("tt0000004", "tvMiniSeries", "Mini", "Mini", "0", "2003", "2003", "240", "Drama"),
                    ("tt0000005", "tvMovie", "TV Film", "TV Film", "0", "2004", "\\N", "80", "Thriller"),
                    ("tt0000006", "tvMovie", "Broken", "0", "2005", "\\N", "80", "Thriller"),
                ),
            )

            stats = process_files(
                ratings_path,
                basics_path,
                output_path,
                report_path,
                unmatched_path,
                set(DEFAULT_TITLE_TYPES),
            )

            self.assertEqual(stats.rating_rows, 6)
            self.assertEqual(stats.matched_rows, 4)
            self.assertEqual(stats.exported_rows, 2)
            self.assertEqual(stats.excluded_rows, 2)
            self.assertEqual(stats.unmatched_rows, 2)
            self.assertEqual(stats.invalid_basics_rows, 1)
            self.assertEqual(stats.invalid_ratings_rows, 0)
            self.assertEqual(
                stats.title_type_counts,
                {"movie": 1, "short": 1, "tvEpisode": 1, "tvMiniSeries": 1, "tvMovie": 2},
            )

            with output_path.open(encoding="utf-8", newline="") as output_file:
                output_rows = list(csv.DictReader(output_file, delimiter="\t"))
            self.assertEqual(
                list(output_rows[0]),
                [
                    "tconst",
                    "averageRating",
                    "numVotes",
                    "titleType",
                    "primaryTitle",
                    "startYear",
                    "genres",
                ],
            )
            self.assertEqual(
                [(row["tconst"], row["titleType"]) for row in output_rows],
                [("tt0000002", "movie"), ("tt0000004", "tvMiniSeries")],
            )
            self.assertEqual(output_rows[0]["primaryTitle"], "Film")
            self.assertEqual(output_rows[0]["startYear"], "2001")
            self.assertEqual(output_rows[0]["genres"], "Drama,Comedy")

            with report_path.open(encoding="utf-8", newline="") as report_file:
                report_rows = list(csv.DictReader(report_file, delimiter="\t"))
            self.assertEqual(
                [(row["titleType"], row["count"]) for row in report_rows],
                [
                    ("movie", "1"),
                    ("short", "1"),
                    ("tvEpisode", "1"),
                    ("tvMiniSeries", "1"),
                    ("tvMovie", "2"),
                ],
            )

            with unmatched_path.open(encoding="utf-8", newline="") as unmatched_file:
                unmatched_rows = list(csv.DictReader(unmatched_file, delimiter="\t"))
            self.assertEqual(
                [row["tconst"] for row in unmatched_rows],
                ["tt0000000", "tt0000006"],
            )
            self.assertEqual(unmatched_rows[0]["matchIssue"], "No tconst match at the current merge position")
            self.assertEqual(unmatched_rows[1]["basicsTitleType"], "tvMovie")
            self.assertTrue(unmatched_rows[1]["matchIssue"].startswith("Malformed basics row"))

    def test_malformed_basics_row_is_skipped_and_reported(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            ratings_path = root / "ratings.tsv"
            basics_path = root / "basics.tsv"
            output_path = root / "filtered.tsv"
            report_path = root / "categories.tsv"
            unmatched_path = root / "unmatched.tsv"

            self._write_tsv(
                ratings_path,
                ("tconst", "averageRating", "numVotes"),
                (("tt0000001", "8.1", "71000"),),
            )
            self._write_tsv(
                basics_path,
                (
                    "tconst",
                    "titleType",
                    "primaryTitle",
                    "originalTitle",
                    "isAdult",
                    "startYear",
                    "endYear",
                    "runtimeMinutes",
                    "genres",
                ),
                (
                    ("tt0000001", "movie", "Broken", "0", "2001", "\\N", "80", "Drama"),
                ),
            )

            stats = process_files(
                ratings_path,
                basics_path,
                output_path,
                report_path,
                unmatched_path,
                set(DEFAULT_TITLE_TYPES),
            )

            self.assertEqual(stats.invalid_basics_rows, 1)
            self.assertEqual(stats.unmatched_rows, 1)
            self.assertEqual(stats.exported_rows, 0)
            with output_path.open(encoding="utf-8", newline="") as output_file:
                self.assertEqual(list(csv.DictReader(output_file, delimiter="\t")), [])
            with unmatched_path.open(encoding="utf-8", newline="") as unmatched_file:
                unmatched_rows = list(csv.DictReader(unmatched_file, delimiter="\t"))
            self.assertEqual(unmatched_rows[0]["tconst"], "tt0000001")
            self.assertEqual(unmatched_rows[0]["basicsTitleType"], "movie")

    @staticmethod
    def _write_tsv(path: Path, header: tuple[str, ...], rows: tuple[tuple[str, ...], ...]) -> None:
        with path.open("w", encoding="utf-8", newline="") as output_file:
            writer = csv.writer(output_file, delimiter="\t", lineterminator="\n")
            writer.writerow(header)
            writer.writerows(rows)


if __name__ == "__main__":
    unittest.main()