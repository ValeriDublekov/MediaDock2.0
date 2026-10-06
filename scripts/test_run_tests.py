import json
import sys
import tempfile
import unittest
from pathlib import Path

from scripts.run_tests import build_command, parse_trx, parse_vitest_report, summarize_unittest


class TestResultParsingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.directory = Path(self.temp_dir.name)

    def test_parse_trx_summary_and_failure_detail(self) -> None:
        report = self.directory / "tests.trx"
        report.write_text(
            '<TestRun xmlns="urn:test"><ResultSummary><Counters total="2" passed="1" failed="1" error="0" />'
            '</ResultSummary><Results><UnitTestResult testName="Example.Fails" outcome="Failed">'
            '<Output><ErrorInfo><Message>expected true</Message><StackTrace>at Example.Test()</StackTrace>'
            '</ErrorInfo></Output></UnitTestResult></Results></TestRun>',
            encoding="utf-8",
        )

        self.assertEqual(parse_trx(report), ("1/2 passed, 1 failed", ["Example.Fails\nexpected true\nat Example.Test()"] ))

    def test_parse_vitest_json_summary_and_failure_detail(self) -> None:
        report = self.directory / "vitest.json"
        report.write_text(json.dumps({
            "numTotalTests": 2,
            "numPassedTests": 1,
            "numFailedTests": 1,
            "testResults": [{"assertionResults": [{
                "title": "renders empty state",
                "status": "failed",
                "failureMessages": ["Expected empty state"],
            }]}],
        }), encoding="utf-8")

        self.assertEqual(parse_vitest_report(report), ("1/2 passed, 1 failed", ["renders empty state\nExpected empty state"]))

    def test_summarize_unittest_failure_names(self) -> None:
        log = self.directory / "unittest.log"
        log.write_text(
            "FAIL: test_example (module.TestCase)\nAssertionError: mismatch\nRan 4 tests in 0.1s\n\nFAILED (failures=1)\n",
            encoding="utf-8",
        )

        summary, failures = summarize_unittest(log)

        self.assertEqual(summary, "4 tests, failures=1")
        self.assertEqual(failures[0], "FAIL: test_example (module.TestCase)")
        self.assertIn("AssertionError: mismatch", failures[1])

    def test_web_command_uses_platform_npm_executable(self) -> None:
        command, _, _, _ = build_command("web", None, self.directory, restore=False)

        self.assertEqual(command[0], "npm.cmd" if sys.platform == "win32" else "npm")


if __name__ == "__main__":
    unittest.main()