import io
import json
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from scripts import run_build
from scripts.run_tests import (
    EF_COMMAND_LOG_LEVEL,
    MAX_DIAGNOSTIC_LENGTH,
    MAX_FAILURES,
    build_command,
    create_run_artifact_dir,
    find_log_diagnostics,
    parse_trx,
    parse_vitest_report,
    print_results,
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

    def test_runner_suite_includes_markdown_link_checker_tests(self) -> None:
        command, _, _, _ = build_command("runner", None, self.directory, restore=False)

        self.assertIn("scripts.test_check_markdown_links", command)

    def test_runner_suite_includes_complexity_analysis_tests(self) -> None:
        command, _, _, _ = build_command("runner", None, self.directory, restore=False)

        self.assertIn("scripts.test_analyze_complexity", command)

    def test_runner_suite_includes_diff_inspector_tests(self) -> None:
        command, _, _, _ = build_command("runner", None, self.directory, restore=False)

        self.assertIn("scripts.test_inspect_diff", command)

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


class BuildRunnerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory(dir=run_build.ROOT / ".test-tmp")
        self.addCleanup(self.temp_dir.cleanup)
        self.directory = Path(self.temp_dir.name)

    def test_server_command_skips_restore_by_default(self) -> None:
        command, cwd = run_build.build_command("server", restore=False)

        self.assertEqual(command[:3], ["dotnet", "build", "server/MediaDock.sln"])
        self.assertIn("--no-restore", command)
        self.assertEqual(cwd, run_build.ROOT)

    def test_server_restore_is_explicit(self) -> None:
        command, _ = run_build.build_command("server", restore=True)

        self.assertNotIn("--no-restore", command)

    def test_web_commands_use_platform_npm_and_web_directory(self) -> None:
        for target, script in (("web", "build"), ("web-lint", "lint")):
            with self.subTest(target=target):
                command, cwd = run_build.build_command(target, restore=False)

                self.assertEqual(command, ["npm.cmd" if sys.platform == "win32" else "npm", "run", script])
                self.assertEqual(cwd, run_build.ROOT / "web")

    def test_unknown_target_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            run_build.build_command("deploy", restore=False)

    def test_build_artifacts_use_unique_workspace_directories(self) -> None:
        with mock.patch("scripts.run_tests.ROOT", self.directory):
            first = create_run_artifact_dir(prefix="mediadock-build-")
            second = create_run_artifact_dir(prefix="mediadock-build-")

        self.assertEqual(first.parent, self.directory / ".test-tmp")
        self.assertTrue(first.name.startswith("mediadock-build-"))
        self.assertNotEqual(first, second)

    def test_success_captures_output_without_printing_full_log(self) -> None:
        def write_output(*args, **kwargs):
            kwargs["stdout"].write("Verbose build output\n")
            return SimpleNamespace(returncode=0)

        with mock.patch("scripts.run_build.subprocess.run", side_effect=write_output) as run_process:
            result = run_build.run_target("web", self.directory, restore=False)

        self.assertTrue(result.passed)
        self.assertEqual(result.failures, [])
        self.assertEqual(result.log_path.read_text(encoding="utf-8"), "Verbose build output\n")
        self.assertEqual(run_process.call_args.kwargs["cwd"], run_build.ROOT / "web")
        self.assertEqual(run_process.call_args.kwargs["stderr"], run_build.subprocess.STDOUT)
        self.assertFalse(run_process.call_args.kwargs["check"])

    def test_typescript_failure_uses_bounded_shared_diagnostics(self) -> None:
        def write_failure(*args, **kwargs):
            kwargs["stdout"].write(
                "Ordinary build output\n"
                + "".join(f"src/App.tsx(1,1): error TS2322: mismatch {index}\n" for index in range(MAX_FAILURES + 2))
            )
            return SimpleNamespace(returncode=2)

        with mock.patch("scripts.run_build.subprocess.run", side_effect=write_failure):
            result = run_build.run_target("web", self.directory, restore=False)

        self.assertFalse(result.passed)
        self.assertEqual(len(result.failures), MAX_FAILURES)
        self.assertTrue(all("error TS2322" in failure for failure in result.failures))

    def test_unrecognized_failure_keeps_exit_status_and_log_location(self) -> None:
        with mock.patch("scripts.run_build.subprocess.run", return_value=SimpleNamespace(returncode=42)):
            result = run_build.run_target("web-lint", self.directory, restore=False)

        self.assertFalse(result.passed)
        self.assertEqual(result.failures, [f"Exit status 42; full log: {result.log_path}"])

    def test_missing_executable_is_a_failed_result(self) -> None:
        with mock.patch("scripts.run_build.subprocess.run", side_effect=FileNotFoundError("missing dotnet")):
            result = run_build.run_target("server", self.directory, restore=False)

        self.assertFalse(result.passed)
        self.assertEqual(result.summary, "exit status 127")
        self.assertIn("Could not start dotnet", result.log_path.read_text(encoding="utf-8"))

    def test_restore_is_rejected_for_web_only(self) -> None:
        with mock.patch.object(sys, "argv", ["run_build.py", "web", "--restore"]):
            with mock.patch("sys.stderr", new_callable=io.StringIO):
                with self.assertRaises(SystemExit) as error:
                    run_build.parse_args()

        self.assertEqual(error.exception.code, 2)

    def test_all_builds_run_and_any_failure_returns_nonzero(self) -> None:
        args = SimpleNamespace(target="all", artifacts_dir=None, restore=False)
        results = [SimpleNamespace(
            name=target,
            passed=target != "server",
            duration=0.1,
            summary="completed" if target != "server" else "exit status 1",
            failures=[],
        ) for target in run_build.TARGETS]
        with mock.patch("scripts.run_build.parse_args", return_value=args):
            with mock.patch("scripts.run_build.create_run_artifact_dir", return_value=self.directory):
                with mock.patch("scripts.run_build.run_target", side_effect=results) as run_target:
                    with redirect_stdout(io.StringIO()) as output:
                        exit_code = run_build.main()

        self.assertEqual(exit_code, 1)
        self.assertEqual([call.args[0] for call in run_target.call_args_list], list(run_build.TARGETS))
        self.assertIn("Build results:", output.getvalue())
        self.assertIn("FAIL server", output.getvalue())
        self.assertIn("PASS web", output.getvalue())

    def test_shared_summary_returns_success_without_dumping_logs(self) -> None:
        result = SimpleNamespace(name="server", passed=True, duration=0.1, summary="completed", failures=[])
        with redirect_stdout(io.StringIO()) as output:
            exit_code = print_results([result], self.directory)

        self.assertEqual(exit_code, 0)
        self.assertIn("Test results:", output.getvalue())
        self.assertIn("Full logs and reports:", output.getvalue())


if __name__ == "__main__":
    unittest.main()