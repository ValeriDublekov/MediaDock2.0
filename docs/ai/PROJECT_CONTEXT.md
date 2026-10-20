# Project Context

This repository is the standalone local MVP, not proof of production state. For operations, use the [local runbook](../../README.md) and [systemd runbook](../../deploy/systemd/README.md); verify the deployed SHA, migrations, backups, and units on the host.

- The API defaults to loopback. LAN access is unauthenticated: bind to a specific trusted interface and enforce the host firewall allowlist. Google sign-in and Cloudflare Access do not authorize API requests; see the [security guide](SECURITY.md).