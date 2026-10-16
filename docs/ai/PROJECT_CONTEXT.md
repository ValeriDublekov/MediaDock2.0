# Project Context

## Runtime Boundary

This repository contains the standalone local MVP. The API binds to loopback by default; any LAN deployment must use a specific trusted interface and a matching host firewall allowlist. Optional Google sign-in does not itself authorize API access. See the [security guide](SECURITY_AND_OPERATIONS.md) for the access-control boundary.

## Production Boundary

This workspace is not the production checkout, and deployment details change over time. Use the [local runbook](../../README.md) and [systemd runbook](../../deploy/systemd/README.md), then verify the deployed SHA, migration history, backups, and installed units on the host before an operation. Never infer production state from local `main` or this document. The production database `mediadock2` is distinct from the legacy `mediadock` database; do not apply the new baseline to the legacy schema or delete its volume. Cloudflare Access is an edge control, not application authorization.

## Stack

- API: .NET 10; the API is the only ingestion runtime and composition root.
- Web UI: React, TypeScript, and Vite.
- Database: PostgreSQL 17 with the Npgsql EF Core provider.

## Main Directories

- [API](../../server/src/MediaDock.Api/MediaDock.Api.csproj), [Application](../../server/src/MediaDock.Application/MediaDock.Application.csproj), and [Infrastructure](../../server/src/MediaDock.Infrastructure/MediaDock.Infrastructure.csproj) are the server projects.
- [Unit tests](../../server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj) and [integration tests](../../server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj) are under `server/tests/`.
- [Web source](../../web/src/main.tsx) is under `web/src/`.
- [Compose configuration](../../compose.yaml) defines local services; schema migrations are under `server/src/MediaDock.Infrastructure/Persistence/Migrations/`.

## Local Entry Point

Start with the [local runbook](../../README.md) for prerequisites and Docker Compose commands. Compose runs PostgreSQL and the API; an explicit migration service applies schema updates. Manual and scheduled ingestion are durable API-hosted jobs, not a separate process or systemd scan timer. The local UI is served at `http://127.0.0.1:8080/` when the stack is running.