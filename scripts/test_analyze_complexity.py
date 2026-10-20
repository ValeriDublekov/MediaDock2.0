import csv
import io
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from scripts import analyze_complexity


def make_csv_row(nloc: int, ccn: int, parameters: int, name: str, path: str, line: int, cogc: int) -> str:
    row = [str(nloc), str(ccn), "100", str(parameters), str(nloc + 2), f"{name}@{line}@{path}", path,
           name, f"{name}()", str(line), str(line + nloc), str(cogc)]
    output = io.StringIO()
    csv.writer(output).writerow(row)
    return output.getvalue()


class ComplexityAnalysisTests(unittest.TestCase):
    def test_parse_lizard_csv_reads_cognitive_complexity(self) -> None:
        results = analyze_complexity.parse_lizard_csv(
            make_csv_row(34, 13, 2, "GetTitleAsync", "src/catalog.cs", 165, 66)
        )

        self.assertEqual(len(results), 1)
        self.assertEqual(results[0].cognitive_complexity, 66)
        self.assertEqual(results[0].cyclomatic_complexity, 13)
        self.assertEqual(results[0].start_line, 165)

    def test_run_lizard_passes_single_file_and_generated_file_exclusions(self) -> None:
        with mock.patch(
            "scripts.analyze_complexity.subprocess.run",
            return_value=SimpleNamespace(returncode=0, stdout="", stderr=""),
        ) as run_process:
            analyze_complexity.run_lizard(["src/FocusedFile.cs"])

        command = run_process.call_args.args[0]
        self.assertEqual(command[-1], "src/FocusedFile.cs")
        self.assertIn("-Ecognitive", command)
        self.assertIn("*/Migrations/*.Designer.cs", command)

    def test_main_prints_only_top_n_in_complexity_order(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source_file = root / "FocusedFile.cs"
            source_file.touch()
            results = analyze_complexity.parse_lizard_csv(
                make_csv_row(20, 9, 1, "LessComplex", "FocusedFile.cs", 4, 12)
                + make_csv_row(34, 13, 2, "MostComplex", "FocusedFile.cs", 30, 66)
            )
            output = io.StringIO()
            with (
                mock.patch.object(analyze_complexity, "ROOT", root),
                mock.patch("scripts.analyze_complexity.run_lizard", return_value=results),
                mock.patch("sys.stdout", output),
            ):
                exit_code = analyze_complexity.main(["--top", "1", "FocusedFile.cs"])

        self.assertEqual(exit_code, 0)
        self.assertIn("Top 1 of 2 functions", output.getvalue())
        self.assertIn("MostComplex", output.getvalue())
        self.assertNotIn("LessComplex", output.getvalue())


if __name__ == "__main__":
    unittest.main()