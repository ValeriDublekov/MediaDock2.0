import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from scripts.run_tests import (
    EF_COMMAND_LOG_LEVEL,
    MAX_DIAGNOSTIC_LENGTH,
    MAX_FAILURES,
    build_command,
    create_run_artifact_dir,
    find_log_diagnostics,
    parse_trx,
    parse_vitest_report,
    run_suite,
    summarize_unittest,
)


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


class TestRunnerArtifactAndFallbackTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.directory = Path(self.temp_dir.name)

    def test_artifact_directory_defaults_to_unique_workspace_run_directories(self) -> None:
        with mock.patch("scripts.run_tests.ROOT", self.directory):
            first_run = create_run_artifact_dir()
            second_run = create_run_artifact_dir()

        self.assertEqual(first_run.parent, self.directory / ".test-tmp")
        self.assertEqual(second_run.parent, self.directory / ".test-tmp")
        self.assertNotEqual(first_run, second_run)

    def test_artifact_directory_option_uses_custom_base_directory(self) -> None:
        with mock.patch("scripts.run_tests.ROOT", self.directory):
            run_dir = create_run_artifact_dir(Path("custom-artifacts"))

        self.assertEqual(run_dir.parent, self.directory / "custom-artifacts")
        self.assertTrue(run_dir.is_dir())

    def test_integration_log_filter_is_scoped_to_child_process(self) -> None:
        original_parent_level = os.environ.get(EF_COMMAND_LOG_LEVEL)
        original_level = "Debug"
        environment = {
            EF_COMMAND_LOG_LEVEL: original_level,
            "MEDIADOCK_RUNNER_TEST": "preserved",
        }
        with mock.patch.dict(os.environ, environment):
            with mock.patch(
                "scripts.run_tests.subprocess.run",
                return_value=SimpleNamespace(returncode=0),
            ) as run_process:
                run_suite("server-integration", None, self.directory, restore=False)
                integration_environment = run_process.call_args.kwargs["env"]
                run_suite("server-unit", None, self.directory, restore=False)
                unit_environment = run_process.call_args.kwargs["env"]

        self.assertEqual(integration_environment[EF_COMMAND_LOG_LEVEL], "Warning")
        self.assertEqual(integration_environment["MEDIADOCK_RUNNER_TEST"], "preserved")
        self.assertIsNone(unit_environment)
        self.assertEqual(os.environ.get(EF_COMMAND_LOG_LEVEL), original_parent_level)

    def test_fallback_diagnostics_are_selected_and_bounded(self) -> None:
        log = self.directory / "build.log"
        log.write_text(
            "EF SQL command noise\n"
            + f"error CS1002: {'x' * (MAX_DIAGNOSTIC_LENGTH + 100)}\n"
            + "".join(f"file.cs({index},1): error CS1002: expected ; {index}\n" for index in range(MAX_FAILURES + 3)),
            encoding="utf-8",
        )

        diagnostics = find_log_diagnostics(log)

        self.assertEqual(len(diagnostics), MAX_FAILURES)
        self.assertTrue(all("EF SQL command noise" not in diagnostic for diagnostic in diagnostics))
        self.assertTrue(all(len(diagnostic) <= MAX_DIAGNOSTIC_LENGTH + 3 for diagnostic in diagnostics))
        self.assertTrue(diagnostics[0].endswith("..."))

    def test_missing_report_fallback_uses_recognized_diagnostics(self) -> None:
        def write_failed_log(*args, **kwargs):
            kwargs["stdout"].write("EF SQL command noise\nfile.cs(1,1): error CS1002: expected ;\n")
            return SimpleNamespace(returncode=1)

        with mock.patch("scripts.run_tests.subprocess.run", side_effect=write_failed_log):
            result = run_suite("server-unit", None, self.directory, restore=False)

        self.assertEqual(result.failures, ["file.cs(1,1): error CS1002: expected ;"])

    def test_missing_report_without_diagnostics_shows_status_and_log_path(self) -> None:
        def write_noisy_log(*args, **kwargs):
            kwargs["stdout"].write("EF SQL command noise\nordinary output\n")
            return SimpleNamespace(returncode=42)

        with mock.patch("scripts.run_tests.subprocess.run", side_effect=write_noisy_log):
            result = run_suite("server-unit", None, self.directory, restore=False)

        self.assertEqual(result.failures, [f"Exit status 42; full log: {result.log_path}"])


if __name__ == "__main__":
    unittest.main()