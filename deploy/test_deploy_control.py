import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


MODULE_PATH = Path(__file__).with_name("deploy-control.py")
SPEC = importlib.util.spec_from_file_location("deploy_control", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
deploy_control = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(deploy_control)

VERIFY_SPEC = importlib.util.spec_from_file_location("verify_deployed_image", Path(__file__).with_name("verify_deployed_image.py"))
assert VERIFY_SPEC is not None and VERIFY_SPEC.loader is not None
verify_deployed_image = importlib.util.module_from_spec(VERIFY_SPEC)
VERIFY_SPEC.loader.exec_module(verify_deployed_image)


class DeployedImageVerificationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.container = {
            "State": {"Running": True},
            "Image": "sha256:expected",
            "Config": {"Image": "mediadock-next-api:commit", "Env": [
                "MediaDock__CommitSha=commit", "MediaDock__Version=version", "SECRET=not-for-logs"
            ]},
        }
        self.image = {"Id": "sha256:expected"}

    def verify(self) -> bool:
        results = [
            subprocess.CompletedProcess([], 0, json.dumps([self.container]), ""),
            subprocess.CompletedProcess([], 0, json.dumps([self.image]), ""),
        ]
        with patch.object(verify_deployed_image.subprocess, "run", side_effect=results):
            return verify_deployed_image.verify_deployed_image("container", "mediadock-next-api:commit", "commit", "version")

    def test_running_container_with_matching_build_is_accepted(self) -> None:
        self.assertTrue(self.verify())

    def test_mismatched_or_stopped_container_is_rejected(self) -> None:
        for field in ("image", "tag", "sha", "version", "duplicate", "stopped"):
            with self.subTest(field=field):
                self.setUp()
                if field == "image":
                    self.image["Id"] = "sha256:other"
                elif field == "tag":
                    self.container["Config"]["Image"] = "other"
                elif field == "sha":
                    self.container["Config"]["Env"][0] = "MediaDock__CommitSha=other"
                elif field == "version":
                    self.container["Config"]["Env"][1] = "MediaDock__Version=other"
                elif field == "duplicate":
                    self.container["Config"]["Env"].append("mediadock__version=other")
                else:
                    self.container["State"]["Running"] = False
                self.assertFalse(self.verify())

    def test_docker_failure_is_rejected(self) -> None:
        with patch.object(verify_deployed_image.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "docker")):
            self.assertFalse(verify_deployed_image.verify_deployed_image("container", "image", "commit", "version"))


class DeploymentControlTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.state_dir = Path(self.temp_dir.name)
        self.state_dir.mkdir(exist_ok=True)

    def test_retry_clears_only_gate_marker_and_starts_fixed_unit(self) -> None:
        gate_marker = self.state_dir / "gate-failed"
        gate_marker.write_text("a" * 40, encoding="ascii")
        command_results = [
            subprocess.CompletedProcess([], 3, "", ""),
            subprocess.CompletedProcess([], 0, "", ""),
        ]

        with patch.object(deploy_control, "STATE_DIR", self.state_dir), patch.object(
            deploy_control, "run_command", side_effect=command_results
        ) as run_command:
            status, response = deploy_control.start_deployment("retry_failed_gate")

        self.assertEqual(status, 202)
        self.assertFalse(gate_marker.exists())
        self.assertEqual(run_command.call_args_list[1].args[0], ["systemctl", "start", "--no-block", deploy_control.DEPLOY_UNIT])
        self.assertEqual(response["message"], "Deployment check queued.")

    def test_recovery_marker_blocks_all_deployment_actions(self) -> None:
        (self.state_dir / "deploy-failed").write_text("target_sha=" + "b" * 40, encoding="ascii")

        with patch.object(deploy_control, "STATE_DIR", self.state_dir), patch.object(
            deploy_control, "run_command"
        ) as run_command:
            status, response = deploy_control.start_deployment("check")

        self.assertEqual(status, 409)
        self.assertEqual(response["message"], "Deployment recovery is required before another run.")
        run_command.assert_not_called()

    def test_status_exposes_failed_gate_and_recovery_state(self) -> None:
        (self.state_dir / "deploy-state").write_text("deployed_sha=" + "c" * 40, encoding="ascii")
        (self.state_dir / "gate-failed").write_text("d" * 40, encoding="ascii")
        (self.state_dir / "deploy-failed").write_text(
            "target_sha=" + "e" * 40 + "\n", encoding="ascii"
        )
        command_results = [
            subprocess.CompletedProcess([], 0, "ActiveState=failed\nSubState=failed\nResult=exit-code\nExecMainStatus=1\n", ""),
            subprocess.CompletedProcess([], 0, "The gate failed\n", ""),
        ]

        with patch.object(deploy_control, "STATE_DIR", self.state_dir), patch.object(
            deploy_control, "run_command", side_effect=command_results
        ):
            status = deploy_control.deployment_status()

        self.assertEqual(status["deployedSha"], "c" * 40)
        self.assertEqual(status["gateFailedSha"], "d" * 40)
        self.assertTrue(status["recoveryRequired"])
        self.assertEqual(status["failureTargetSha"], "e" * 40)
        self.assertEqual(status["recentOutput"], ["The gate failed"])

if __name__ == "__main__":
    unittest.main()
