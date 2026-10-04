import importlib.util
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
