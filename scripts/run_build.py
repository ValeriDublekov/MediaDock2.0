#!/usr/bin/env python3
"""Build or lint MediaDock locally with compact summaries and retained logs."""

from __future__ import annotations

import argparse
import subprocess
import sys
import time
from pathlib import Path

if __package__:
    from .run_tests import ROOT, SuiteResult, create_run_artifact_dir, find_log_diagnostics, print_results
else:
    from run_tests import ROOT, SuiteResult, create_run_artifact_dir, find_log_diagnostics, print_results


TARGETS = ("server", "web-lint", "web")


def build_command(target: str, restore: bool) -> tuple[list[str], Path]:
    if target == "server":
        command = ["dotnet", "build", "server/MediaDock.sln", "--nologo", "--verbosity", "minimal"]
        if not restore:
            command.append("--no-restore")
        return command, ROOT
    if target in {"web", "web-lint"}:
        npm_command = "npm.cmd" if sys.platform == "win32" else "npm"
        return [npm_command, "run", "lint" if target == "web-lint" else "build"], ROOT / "web"
    raise ValueError(f"Unknown build target: {target}")


def run_target(target: str, report_dir: Path, restore: bool) -> SuiteResult:
    command, cwd = build_command(target, restore)
    log_path = report_dir / f"{target}.log"
    started = time.monotonic()
    try:
        with log_path.open("w", encoding="utf-8", errors="replace") as output:
            completed = subprocess.run(
                command,
                cwd=cwd,
                stdout=output,
                stderr=subprocess.STDOUT,
                check=False,
            )
        return_code = completed.returncode
    except OSError as error:
        log_path.write_text(f"Could not start {command[0]}: {error}\n", encoding="utf-8")
        return_code = 127

    failures = []
    if return_code != 0:
        failures = find_log_diagnostics(log_path)
        if not failures:
            failures = [f"Exit status {return_code}; full log: {log_path}"]
    return SuiteResult(
        name=target,
        passed=return_code == 0,
        duration=time.monotonic() - started,
        summary="completed" if return_code == 0 else f"exit status {return_code}",
        failures=failures,
        log_path=log_path,
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=("all", *TARGETS), help="Local build/lint target; all runs each target")
    parser.add_argument("--restore", action="store_true", help="Allow the server build to restore NuGet packages")
    parser.add_argument(
        "--artifacts-dir",
        type=Path,
        help="Base directory for unique run logs (default: .test-tmp/ under the repository root)",
    )
    args = parser.parse_args()
    if args.restore and args.target not in {"all", "server"}:
        parser.error("--restore applies only to server or all")
    return args


def main() -> int:
    args = parse_args()
    targets = TARGETS if args.target == "all" else (args.target,)
    report_dir = create_run_artifact_dir(args.artifacts_dir, prefix="mediadock-build-")
    results = [run_target(target, report_dir, args.restore) for target in targets]
    return print_results(results, report_dir, title="Build results:")


if __name__ == "__main__":
    raise SystemExit(main())