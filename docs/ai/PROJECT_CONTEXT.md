# Project Context

## Local and Production Boundaries

This workspace contains the standalone local MVP, not the production checkout. The API binds to loopback by default; any LAN deployment must use a specific trusted interface and a matching host firewall allowlist. Optional Google sign-in does not itself authorize API access. See the [security guide](SECURITY_AND_OPERATIONS.md) for the access-control boundary.

Use the [local runbook](../../README.md) and [systemd runbook](../../deploy/systemd/README.md) for operations, then verify the deployed SHA, migration history, backups, and installed units on the host. Repository source and this document do not prove production state. The production database `mediadock2` is distinct from the legacy `mediadock` database; do not apply the new baseline to the legacy schema or delete its volume. Cloudflare Access is an edge control, not application authorization.