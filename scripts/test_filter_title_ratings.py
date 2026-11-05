from __future__ import annotations

import csv
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts.filter_title_ratings import (
    DEFAULT_MIN_RATING,
    DEFAULT_MIN_VOTES,
    filter_file,
    parse_args,
)


class FilterTitleRatingsTests(unittest.TestCase):
    def test_cli_accepts_custom_thresholds(self) -> None:
        with patch(
            "sys.argv",
            ["filter_title_ratings.py", "--min-rating", "7.5", "--min-votes", "100000"],
        ):
            args = parse_args()

        self.assertEqual(args.min_rating, 7.5)
        self.assertEqual(args.min_votes, 100_000)

    def test_default_thresholds_remain_strict(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "ratings.tsv"
            output = Path(directory) / "filtered.tsv"
            self._write_tsv(
                source,
                (
                    ("tt0000001", "7.0", "70001"),
                    ("tt0000002", "7.1", "60000"),
                    ("tt0000003", "7.1", "60001"),
                ),
            )

            stats = filter_file(source, output)

            self.assertEqual((DEFAULT_MIN_RATING, DEFAULT_MIN_VOTES), (7.0, 60_000))
            self.assertEqual(stats, (3, 0, 1))
            self.assertEqual(self._read_ids(output), ["tt0000003"])

    def test_custom_thresholds_are_applied(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "ratings.tsv"
            output = Path(directory) / "filtered.tsv"
            self._write_tsv(
                source,
                (
                    ("tt0000001", "7.5", "101"),
                    ("tt0000002", "7.6", "100"),
                    ("tt0000003", "7.6", "101"),
                ),
            )

            stats = filter_file(source, output, min_rating=7.5, min_votes=100)

            self.assertEqual(stats, (3, 0, 1))
            self.assertEqual(self._read_ids(output), ["tt0000003"])

    @staticmethod
    def _write_tsv(path: Path, rows: tuple[tuple[str, str, str], ...]) -> None:
        with path.open("w", encoding="utf-8", newline="") as output_file:
            writer = csv.writer(output_file, delimiter="\t", lineterminator="\n")
            writer.writerow(("tconst", "averageRating", "numVotes"))
            writer.writerows(rows)

    @staticmethod
    def _read_ids(path: Path) -> list[str]:
        with path.open(encoding="utf-8", newline="") as input_file:
            return [row["tconst"] for row in csv.DictReader(input_file, delimiter="\t")]


if __name__ == "__main__":
    unittest.main()