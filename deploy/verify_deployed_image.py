#!/usr/bin/env python3
"""Verify the running API container and its build metadata without exposing secrets."""

import json
import subprocess
import sys


def verify_deployed_image(container_id: str, image_name: str, commit_sha: str, version: str) -> bool:
    if not container_id:
        return False

    try:
        container_result = subprocess.run(
            ["docker", "inspect", "--type", "container", container_id],
            check=True, capture_output=True, text=True,
        )
        image_result = subprocess.run(
            ["docker", "image", "inspect", image_name],
            check=True, capture_output=True, text=True,
        )
        containers = json.loads(container_result.stdout)
        images = json.loads(image_result.stdout)
        if len(containers) != 1 or len(images) != 1:
            return False

        container = containers[0]
        image = images[0]
        environment = container["Config"]["Env"]
        expected = {"mediadock__commitsha": commit_sha, "mediadock__version": version}
        metadata = {key: [] for key in expected}
        for item in environment:
            key, separator, value = item.partition("=")
            if separator and key.casefold() in metadata:
                metadata[key.casefold()].append(value)
        return (
            container["State"]["Running"] is True
            and container["Image"] == image["Id"]
            and container["Config"]["Image"] == image_name
            and all(metadata[key] == [value] for key, value in expected.items())
        )
    except (subprocess.SubprocessError, OSError, ValueError, KeyError, TypeError, AttributeError):
        return False


if __name__ == "__main__":
    if len(sys.argv) != 5 or not verify_deployed_image(*sys.argv[1:]):
        sys.exit(1)