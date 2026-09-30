# Project Context

## Runtime boundary

This repository contains the standalone, unauthenticated local MVP. The API must remain bound to loopback by default; see the [local runbook](../../README.md) for the LAN deployment boundary.

## Production handoff (2026-09-30)

- Source of truth: `https://github.com/ValeriDublekov/MediaDock2.0.git`, branch `main`. The Ubuntu production checkout is `/opt/docker/projects/mediadock-next` (Compose project `mediadock-next`); the previous monorepo checkout is retained at `/opt/docker/projects/mediadock-next-legacy-20260930` for rollback. This local clone is not the production checkout.
- The first standalone deployment was commit `2dd39927d5a0b02d74c9f7f8e3212a8e74c731c8`. Documentation release `123d473764b9b26820a2479282d411c4e5647324` was deployed and verified on 2026-09-30. For the **current** SHA, check the root-managed `/var/lib/mediadock-deploy/deploy-state` on the server; never infer it from this dated snapshot or local `main`.
- Production uses PostgreSQL database `mediadock2` with baseline migration `20260930122500_InitialRelationalSchema`. The separate old `mediadock` database still has six migrations; never apply the new baseline to it or delete its volume. The API is bound to a specific trusted LAN interface on port `8081`; PostgreSQL's host port is loopback-only at `127.0.0.1:5432`. The app has no login, so an allowed LAN client can modify data. Host address, subnet, `.env`, credentials, and database contents are server-only, not repository context.
- The enabled deploy timer polls GitHub `main` every five minutes. A new commit runs the clean server-side [staging gate](../../deploy/test.sh), builds versioned images, validates a pre-migration dump, runs migrations, and checks readiness before recording the SHA. Publishing even documentation to `main` can trigger that full deployment; verify the deployed SHA and service result after pushing. Do not bypass the gate or manually run a migration against production using Compose's default image tag.
- The daily dump timer runs before Restic. Cutover dump `daily-20260930T150707Z.dump` was validated and confirmed in Restic; the later deployment dump `daily-20260930T151940Z.dump` had not yet been confirmed in a later Restic snapshot at handoff. The Worker timer is not installed and no scan has run. Do not enable it or run a manual scan until the OMDb key/quota and feeds are reviewed and the operator approves the run. SMTP notification, router forwarding review, and non-LAN denial test remain outstanding.

For current operations, security boundaries, and recovery use the [security guide](SECURITY_AND_OPERATIONS.md) and [systemd runbook](../../deploy/systemd/README.md). Keep host-specific handoff notes and secrets outside this public repository; repository visibility is not a security control for the unauthenticated API.

## Stack

- API and Worker: .NET 10 (`net10.0` in the [API project](../../server/src/MediaDock.Api/MediaDock.Api.csproj) and [Worker project](../../server/src/MediaDock.Worker/MediaDock.Worker.csproj)).
- Web UI: React, TypeScript, and Vite ([web package](../../web/package.json)).
- Database: PostgreSQL 17 in [Compose](../../compose.yaml), using the Npgsql EF Core provider in the [Infrastructure project](../../server/src/MediaDock.Infrastructure/MediaDock.Infrastructure.csproj).

## Main directories

- [API](../../server/src/MediaDock.Api/MediaDock.Api.csproj), [Application](../../server/src/MediaDock.Application/MediaDock.Application.csproj), [Infrastructure](../../server/src/MediaDock.Infrastructure/MediaDock.Infrastructure.csproj), and [Worker](../../server/src/MediaDock.Worker/MediaDock.Worker.csproj) are the server projects.
- [Unit tests](../../server/tests/MediaDock.UnitTests/MediaDock.UnitTests.csproj) and [integration tests](../../server/tests/MediaDock.IntegrationTests/MediaDock.IntegrationTests.csproj) are under `server/tests/`.
- [Web source](../../web/src/main.tsx) is under `web/src/`.
- [Compose configuration](../../compose.yaml) defines the local services; schema migrations are under `server/src/MediaDock.Infrastructure/Persistence/Migrations/` as noted in the [runbook](../../README.md).

## Local entrypoint

Start with the [local runbook](../../README.md), which documents prerequisites and the Docker Compose workflow. The [Compose file](../../compose.yaml) runs PostgreSQL and the API, with the Worker available as an explicit one-shot service. The UI is served at `http://127.0.0.1:8080/` when the documented stack is running. Keep operational commands in the runbook rather than duplicating them here.