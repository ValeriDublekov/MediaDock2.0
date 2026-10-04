#!/usr/bin/env python3
"""Expose a narrow Unix-socket API for deployment status and manual runs."""

import json
import os
import stat
import subprocess
from threading import Lock
from http.server import BaseHTTPRequestHandler
from pathlib import Path


SOCKET_PATH = Path(os.environ.get(
    "MEDIADOCK_DEPLOY_CONTROL_SOCKET",
    "/var/lib/mediadock-deploy-control/control.sock",
))
STATE_DIR = Path(os.environ.get("MEDIADOCK_DEPLOY_STATE_DIR", "/var/lib/mediadock-deploy"))
DEPLOY_UNIT = "mediadock-next-deploy.service"
ACTION_LOCK = Lock()


def read_key_values(path: Path) -> dict[str, str]:
    try:
        file_stat = path.lstat()
        if not stat.S_ISREG(file_stat.st_mode):
            return {}
        values: dict[str, str] = {}
        for line in path.read_text(encoding="utf-8").splitlines():
            key, separator, value = line.partition("=")
            if separator:
                values[key] = value
        return values
    except OSError:
        return {}


def read_gate_failure(path: Path) -> str | None:
    try:
        if not stat.S_ISREG(path.lstat().st_mode):
            return None
        value = path.read_text(encoding="ascii").strip()
        return value if len(value) == 40 and all(char in "0123456789abcdefABCDEF" for char in value) else None
    except (OSError, UnicodeError):
        return None


def run_command(arguments: list[str], timeout: int = 5) -> subprocess.CompletedProcess[str]:
    return subprocess.run(arguments, check=False, capture_output=True, text=True, timeout=timeout)


def deployment_status() -> dict[str, object]:
    properties = [
        "ActiveState",
        "SubState",
        "Result",
        "ExecMainStatus",
        "ExecMainStartTimestamp",
        "ExecMainExitTimestamp",
    ]
    try:
        result = run_command(["systemctl", "show", DEPLOY_UNIT, "--property=" + ",".join(properties)])
        unit_state = dict(
            line.split("=", 1) for line in result.stdout.splitlines() if "=" in line
        )
        if result.returncode != 0:
            unit_state = {"ActiveState": "unknown", "SubState": "unknown", "Result": "unknown"}
    except (OSError, subprocess.TimeoutExpired):
        unit_state = {"ActiveState": "unknown", "SubState": "unknown", "Result": "unknown"}

    try:
        journal = run_command(["journalctl", "-u", DEPLOY_UNIT, "-n", "40", "--no-pager", "-o", "cat"])
        recent_output = journal.stdout.splitlines()[-40:] if journal.returncode == 0 else []
    except (OSError, subprocess.TimeoutExpired):
        recent_output = []

    failure_path = STATE_DIR / "deploy-failed"
    failure = read_key_values(failure_path)
    failure_target = failure.get("target_sha")
    if failure_target is not None and (
        len(failure_target) != 40 or any(char not in "0123456789abcdefABCDEF" for char in failure_target)
    ):
        failure_target = None
    return {
        "isRunning": unit_state.get("ActiveState") in {"activating", "active"},
        "activeState": unit_state.get("ActiveState", "unknown"),
        "subState": unit_state.get("SubState", "unknown"),
        "result": unit_state.get("Result", "unknown"),
        "exitCode": unit_state.get("ExecMainStatus", ""),
        "startedAt": unit_state.get("ExecMainStartTimestamp", ""),
        "finishedAt": unit_state.get("ExecMainExitTimestamp", ""),
        "deployedSha": read_key_values(STATE_DIR / "deploy-state").get("deployed_sha"),
        "gateFailedSha": read_gate_failure(STATE_DIR / "gate-failed"),
        "recoveryRequired": failure_path.exists() or failure_path.is_symlink(),
        "failureTargetSha": failure_target,
        "recentOutput": recent_output,
    }


def _start_deployment(action: str) -> tuple[int, dict[str, object]]:
    if action not in {"check", "retry_failed_gate"}:
        return 400, {"message": "Unsupported deployment action."}

    failure_path = STATE_DIR / "deploy-failed"
    if failure_path.exists() or failure_path.is_symlink():
        return 409, {"message": "Deployment recovery is required before another run."}

    active = run_command(["systemctl", "is-active", "--quiet", DEPLOY_UNIT])
    if active.returncode == 0:
        return 409, {"message": "A deployment is already running."}

    gate_path = STATE_DIR / "gate-failed"
    if action == "retry_failed_gate":
        failed_sha = read_gate_failure(gate_path)
        if failed_sha is None:
            return 409, {"message": "There is no valid failed staging gate to retry."}
        gate_path.unlink()
    try:
        result = run_command(["systemctl", "start", "--no-block", DEPLOY_UNIT])
    except (OSError, subprocess.TimeoutExpired):
        return 503, {"message": "Could not start the deployment service."}
    if result.returncode != 0:
        return 503, {"message": "Could not start the deployment service."}
    return 202, {"message": "Deployment check queued."}


def start_deployment(action: str) -> tuple[int, dict[str, object]]:
    with ACTION_LOCK:
        return _start_deployment(action)


class DeploymentControlHandler(BaseHTTPRequestHandler):
    server_version = "MediaDockDeployControl/1.0"

    def log_message(self, format: str, *args: object) -> None:
        return

    def send_json(self, status: int, body: dict[str, object]) -> None:
        payload = json.dumps(body, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self) -> None:
        if self.path != "/status":
            self.send_json(404, {"message": "Not found."})
            return
        self.send_json(200, deployment_status())

    def do_POST(self) -> None:
        if self.path != "/run":
            self.send_json(404, {"message": "Not found."})
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if length < 1 or length > 128:
                raise ValueError("Invalid request size.")
            request = json.loads(self.rfile.read(length))
            action = request.get("action") if isinstance(request, dict) else None
            if not isinstance(action, str):
                raise ValueError("Invalid deployment action.")
        except (ValueError, json.JSONDecodeError):
            self.send_json(400, {"message": "Invalid deployment request."})
            return
        status, body = start_deployment(action)
        self.send_json(status, body)


def main() -> None:
    from socketserver import ThreadingMixIn, UnixStreamServer

    class ThreadingUnixServer(ThreadingMixIn, UnixStreamServer):
        daemon_threads = True

    SOCKET_PATH.parent.mkdir(mode=0o755, parents=True, exist_ok=True)
    try:
        socket_stat = SOCKET_PATH.lstat()
        if not stat.S_ISSOCK(socket_stat.st_mode):
            raise RuntimeError(f"Refusing to replace non-socket path: {SOCKET_PATH}")
        SOCKET_PATH.unlink()
    except FileNotFoundError:
        pass

    with ThreadingUnixServer(str(SOCKET_PATH), DeploymentControlHandler) as server:
        os.chmod(SOCKET_PATH, 0o666)
        server.serve_forever()


if __name__ == "__main__":
    main()