#!/usr/bin/env python3
"""Run MediaDock test suites and print compact failure-focused summaries."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
UNIT_PROJECT = "server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj"
INTEGRATION_PROJECT = "server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj"
ALL_SUITES = ("server-unit", "server-integration", "web", "deploy", "runner")
MAX_FAILURES = 12
MAX_DETAIL_LINES = 16


@dataclass
class SuiteResult:
    name: str
    passed: bool
    duration: float
    summary: str
    failures: list[str]
    log_path: Path


def parse_trx(report_path: Path) -> tuple[str, list[str]] | None:
    if not report_path.is_file():
        return None
    try:
        root = ET.parse(report_path).getroot()
    except ET.ParseError:
        return None

    counters = next((node for node in root.iter() if node.tag.endswith("}Counters") or node.tag == "Counters"), None)
    results = [node for node in root.iter() if node.tag.endswith("}UnitTestResult") or node.tag == "UnitTestResult"]
    if counters is not None:
        total = int(counters.get("total", "0"))
        passed = int(counters.get("passed", "0"))
        failed_count = int(counters.get("failed", "0")) + int(counters.get("error", "0"))
    else:
        passed = sum(node.get("outcome", "").lower() == "passed" for node in results)
        failed_count = sum(node.get("outcome", "").lower() in {"failed", "error"} for node in results)
        total = len(results)

    failures: list[str] = []
    for result in results:
        if result.get("outcome", "").lower() not in {"failed", "error"}:
            continue
        name = result.get("testName", "Unknown test")
        error_info = next((node for node in result.iter() if node.tag.endswith("}ErrorInfo") or node.tag == "ErrorInfo"), None)
        detail = ""
        if error_info is not None:
            parts = [node.text.strip() for node in error_info.iter() if node.text and node.text.strip()]
            detail = "\n".join(parts)
        failures.append(f"{name}\n{detail}".rstrip())

    return f"{passed}/{total} passed, {failed_count} failed", failures


def parse_vitest_report(report_path: Path) -> tuple[str, list[str]] | None:
    if not report_path.is_file():
        return None
    try:
        report = json.loads(report_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    if not isinstance(report, dict):
        return None

    test_results = report.get("testResults", [])
    assertions = [
        assertion
        for suite in test_results if isinstance(test_results, list) and isinstance(suite, dict)
        for assertion in suite.get("assertionResults", [])
        if isinstance(suite.get("assertionResults", []), list) and isinstance(assertion, dict)
    ]
    passed = report.get("numPassedTests", sum(item.get("status") == "passed" for item in assertions))
    failed = report.get("numFailedTests", sum(item.get("status") in {"failed", "failure"} for item in assertions))
    total = report.get("numTotalTests", len(assertions))

    failures = []
    for assertion in assertions:
        if assertion.get("status") not in {"failed", "failure"}:
            continue
        name = assertion.get("fullName") or assertion.get("title") or "Unknown test"
        messages = assertion.get("failureMessages", [])
        detail = "\n".join(str(message) for message in messages) if isinstance(messages, list) else str(messages)
        failures.append(f"{name}\n{detail}".rstrip())

    return f"{passed}/{total} passed, {failed} failed", failures


def summarize_unittest(log_path: Path) -> tuple[str, list[str]]:
    text = log_path.read_text(encoding="utf-8", errors="replace")
    ran = re.search(r"Ran (\d+) tests?", text)
    failed = re.search(r"FAILED \(([^)]*)\)", text)
    summary = f"{ran.group(1)} tests" if ran else "Python unittest completed"
    if failed:
        summary += f", {failed.group(1)}"

    failure_names = re.findall(r"^(?:FAIL|ERROR): .+$", text, re.MULTILINE)
    if failed:
        failure_names.append("Failure details:\n" + "\n".join(text.splitlines()[-MAX_DETAIL_LINES:]))
    return summary, failure_names


def build_command(name: str, filter_value: str | None, report_dir: Path, restore: bool) -> tuple[list[str], Path, Path | None, str]:
    log_path = report_dir / f"{name}.log"
    report_path: Path | None = None
    cwd = ROOT

    if name in {"server-unit", "server-integration"}:
        project = UNIT_PROJECT if name == "server-unit" else INTEGRATION_PROJECT
        report_path = report_dir / f"{name}.trx"
        command = ["dotnet", "test", project]
        if not restore:
            command.append("--no-restore")
        command.extend(["--logger", f"trx;LogFileName={report_path.name}", "--results-directory", str(report_dir)])
        if filter_value:
            command.extend(["--filter", filter_value])
    elif name == "web":
        cwd = ROOT / "web"
        report_path = report_dir / "web.json"
        npm_command = "npm.cmd" if sys.platform == "win32" else "npm"
        command = [npm_command, "run", "test", "--", "--reporter=json", f"--outputFile={report_path}"]
        if filter_value:
            command.append(filter_value)
    else:
        command = [sys.executable, "-B", "-m", "unittest"]
        if filter_value:
            command.extend(["-k", filter_value])
        module = "deploy.test_deploy_control" if name == "deploy" else "scripts.test_run_tests"
        command.append(module)

    return command, log_path, report_path, str(cwd)


def tail_lines(path: Path, limit: int = MAX_DETAIL_LINES) -> list[str]:
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return []
    return lines[-limit:]


def run_suite(name: str, filter_value: str | None, report_dir: Path, restore: bool) -> SuiteResult:
    command, log_path, report_path, cwd = build_command(name, filter_value, report_dir, restore)
    started = time.monotonic()
    try:
        with log_path.open("w", encoding="utf-8", errors="replace") as output:
            completed = subprocess.run(command, cwd=cwd, stdout=output, stderr=subprocess.STDOUT, check=False)
        return_code = completed.returncode
    except OSError as error:
        log_path.write_text(f"Could not start {command[0]}: {error}\n", encoding="utf-8")
        return_code = 127

    duration = time.monotonic() - started
    parsed = parse_trx(report_path) if name.startswith("server-") and report_path else None
    if name == "web" and report_path:
        parsed = parse_vitest_report(report_path)

    if name in {"deploy", "runner"}:
        summary, failures = summarize_unittest(log_path)
    elif parsed is not None:
        summary, failures = parsed
    else:
        summary, failures = "No structured test report was produced", []

    if return_code != 0 and not failures:
        failures = tail_lines(log_path)
    return SuiteResult(name, return_code == 0, duration, summary, failures, log_path)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("suite", choices=("all", *ALL_SUITES), help="Test suite to run")
    parser.add_argument("--filter", help="Suite-specific test filter; cannot be used with 'all'")
    parser.add_argument("--restore", action="store_true", help="Allow dotnet test to restore packages")
    args = parser.parse_args()
    if args.filter and args.suite == "all":
        parser.error("--filter requires one specific suite")
    if args.restore and args.suite not in {"all", "server-unit", "server-integration"}:
        parser.error("--restore applies only to .NET suites")
    return args


def main() -> int:
    args = parse_args()
    suites = ALL_SUITES if args.suite == "all" else (args.suite,)
    report_dir = Path(tempfile.mkdtemp(prefix="mediadock-tests-"))
    results = [run_suite(name, args.filter, report_dir, args.restore) for name in suites]

    print("Test results:")
    for result in results:
        status = "PASS" if result.passed else "FAIL"
        print(f"{status:4} {result.name}: {result.summary} ({result.duration:.1f}s)")
        if not result.passed and result.failures:
            for failure in result.failures[:MAX_FAILURES]:
                print("  " + "\n  ".join(failure.splitlines()[:MAX_DETAIL_LINES]))
            remaining = len(result.failures) - MAX_FAILURES
            if remaining > 0:
                print(f"  ... {remaining} more failures; see full log")
    print(f"Full logs and reports: {report_dir}")
    return 0 if all(result.passed for result in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())