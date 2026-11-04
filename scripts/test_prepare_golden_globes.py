import csv
import io
import runpy
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch


class PrepareGoldenGlobesTests(unittest.TestCase):
    def test_preserves_optional_imdb_id_column(self):
        script = Path(__file__).with_name("prepare-golden-globes.py")
        main = runpy.run_path(str(script))["main"]

        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source.csv"
            output = Path(directory) / "compact.csv"
            source.write_text(
                "id,nominee_type,year,winner,award,title,imdb_id\n"
                "1,film,2025,true,Best Motion Picture,Example Film,tt12345678\n",
                encoding="utf-8",
            )

            with patch.object(sys, "argv", [str(script), str(source), "--output", str(output)]):
                with redirect_stdout(io.StringIO()):
                    main()

            with output.open(encoding="utf-8", newline="") as file:
                row = next(csv.DictReader(file))

        self.assertEqual("tt12345678", row["imdb_id"])


if __name__ == "__main__":
    unittest.main()