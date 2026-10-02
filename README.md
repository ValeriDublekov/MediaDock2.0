# MediaDock 2.0

[AI documentation index](docs/ai/README.md)

Standalone local MVP using React, .NET, and PostgreSQL. The unauthenticated API defaults to loopback. Any LAN deployment must bind to a specific trusted interface and enforce a matching host firewall allowlist.

## Production status (2026-09-30)

The standalone `MediaDock2.0` repository was first deployed from GitHub `main` commit
`2dd39927d5a0b02d74c9f7f8e3212a8e74c731c8` at
`/opt/docker/projects/mediadock-next`. The previous monorepo checkout remains
at `/opt/docker/projects/mediadock-next-legacy-20260930` for rollback. The new
app uses the separate PostgreSQL database `mediadock2` with the single baseline
migration `20260930122500_InitialRelationalSchema`; the old `mediadock` database
and its six-migration history remain intact. Catalog, source, and settings
tables were empty at cutover. The live deployed SHA is recorded in the
root-managed `/var/lib/mediadock-deploy/deploy-state` on the server.

From the trusted LAN, open `http://<server-LAN-IPv4>:8081/`. The real host
address is kept in server-only configuration and intentionally omitted from
Git. The app has no login or authorization: every client allowed by the trusted
LAN can read and change application data. The API uses a specific trusted
interface; PostgreSQL remains loopback-only at `127.0.0.1:5432`. LAN checks for
the UI, readiness, and catalog returned HTTP 200. Router port-forward and
non-LAN denial checks remain unverified.

The API, UI, and catalog returned HTTP 200 after deployment. The API remains
bound to the trusted LAN interface at port `8081`; PostgreSQL remains
loopback-only at `127.0.0.1:5432`. The deploy timer is enabled and checks the
new GitHub `main` every five minutes. Its first poll completed as a no-op on
the deployed SHA. The pre-migration dump
`daily-20260930T150707Z.dump` passed `pg_restore -l` and is present in Restic.
Optional success email is not configured.

The production host has not yet been cut over to API-hosted ingestion. The
currently deployed release predates the background-job migrations; its legacy
Worker service is installed and its timer is not installed or enabled. Before
deploying this architecture, verify a fresh backup and migration state, stop
and disable the legacy Worker units, then follow the staged migration procedure
in the [systemd runbook](deploy/systemd/README.md). No scan has run. Router
port-forward and non-LAN denial checks remain unverified.

## Prerequisites

- .NET 10 SDK
- Node.js and npm
- Docker Compose

## Ubuntu Staging Test Gate

Run from the repository root on the Ubuntu staging host, using a clean checkout of a trusted `main` commit:

```bash
DEPLOY_COMMIT="$(git rev-parse HEAD)" bash ./deploy/test.sh
```

The script checks that the requested commit matches `HEAD` and that the worktree is clean. It derives the app root from its own location. Web lint, tests, and build run in a Node container; .NET unit tests run without the Docker socket, and only the Testcontainers integration-test container receives it. That socket grants root-equivalent host access. Temporary PostgreSQL host-port bindings are limited to `127.0.0.1`. The gate uses validation-only Compose values, does not load the production `.env` or mount production volumes, and builds only the API image. Keep these checks server-side; the legacy repository's GitHub Actions workflows remain separate.

## Local Docker setup

Create the ignored environment file once, then replace the sample database password with a local-only value. OMDb credentials and runtime request limits are configured in the web UI after applying migrations and starting the API. Oscar CSV imports do not call OMDb.

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
```

From the repository root, validate the Compose configuration, start PostgreSQL, apply pending EF Core migrations, then start the API and static React app:

```powershell
docker compose -f compose.yaml config
docker compose -f compose.yaml up --build -d db
docker compose -f compose.yaml run --rm migrate
docker compose -f compose.yaml up --build -d api
```

The UI is at `http://127.0.0.1:8080/`; readiness is `http://127.0.0.1:8080/health/ready`, and the catalog API is `http://127.0.0.1:8080/api/catalog`. `APP_BIND_ADDRESS` defaults to `127.0.0.1`. For a LAN deployment, bind to that host's specific trusted IPv4 address and use a matching `DOCKER-USER` allowlist with a deny rule for other sources. Keep those host-specific values in a root-owned host configuration file, not in Git. Do not use a wildcard bind or expose the API through a proxy. PostgreSQL's host port remains bound to `127.0.0.1` and defaults to `5432` (override with `POSTGRES_PORT`). The existing `postgres_data` volume is retained across container stops and recreation.

Schema changes are checked in as EF Core migrations under `server/src/MediaDock.Infrastructure/Persistence/Migrations/`. The API does not migrate on startup; run the one-shot `migrate` service after adding a migration and before starting the API. For the system-source-profile migration, create and validate a fresh database backup first. After the API is ready, verify `GET /api/sources` returns exactly the fixed `movie`, `series_complete`, and `series_ongoing` profiles and that each pre-migration URL appears under its expected profile; legacy `series` URLs belong under `series_ongoing`.

## Health and logs

```powershell
docker compose -f compose.yaml ps
docker compose -f compose.yaml logs --tail=100 api db
Invoke-WebRequest http://127.0.0.1:8080/health/ready
Invoke-RestMethod http://127.0.0.1:8080/api/catalog
Invoke-WebRequest http://127.0.0.1:8080/
```

## Background ingestion

The API owns one PostgreSQL-backed queue and one hosted dispatcher. Use
**Configuration > Ingestion > Start scan** for RSS parsing and
**Enrich Oscar films** for a separate Oscar metadata run. The API
queues additional scans daily at 07:00 and 18:00 in `Europe/Sofia`; missed
slots coalesce to at most one catch-up job. Jobs survive browser close and API
restarts. A PostgreSQL advisory lock serializes RSS scans, Oscar enrichment,
and dataset imports. RSS scans do not start Oscar enrichment automatically.
Migration-only startup applies schema changes and exits before the hosted
dispatcher starts.

Configure the OMDb key, confirmed shared daily quota, Oscar per-run cap, and
Oscar daily cap in Configuration. RSS and Oscar attempts share the UTC-day
budget, which stops at 50 requests below the configured shared quota (950 for
a quota of 1,000); the Oscar cap is additional, not reserved capacity. Every
non-cache HTTP attempt reserves before sending, including fallbacks and
retries. Cache hits use no slot. Provider quota exhaustion stops further
attempts for that UTC day and leaves unprocessed Oscar candidates eligible. Each enrichment
continues to write its separate `oscar_enrichment_runs` audit record, while RSS
`scan_runs` and `parse_logs` retain their existing meanings. Status, safe events,
and scan/parse history are available in the UI and the background-job API.

### Import Oscar dataset

The source is Kaggle's [The Oscar Award dataset](https://www.kaggle.com/datasets/unanimad/the-oscar-award); its page identifies the dataset as CC0. Download it yourself and keep it outside the repository. Record the source URL, retrieval date/version, SHA-256, and year threshold in the operator's import notes. The API accepts bounded CSV/TSV uploads and queues the bytes; it never accepts a server filesystem path.

```powershell
$sourcePath = "$HOME\Downloads\full_data.csv"
Get-FileHash -Path $sourcePath -Algorithm SHA256
& .\scripts\prepare-oscar-csv.ps1 -InputPath $sourcePath -OutputPath "$HOME\Downloads\oscar_films_after_1980_compact.csv" -Force
```

Open Configuration, choose the prepared CSV/TSV under **Oscar dataset**, set
**Film year after**, and queue the import. The API-hosted executor uses the
same advisory lock, supports compact CSV and the tab-separated Kaggle format,
and imports Best Picture, Directing, Original/Adapted Screenplay, and
Cinematography only. Imports are idempotent and additive; they preserve existing
OMDb metadata, do not make OMDb requests, and do not create torrent occurrences.
Uploaded bytes are erased at terminal state; the filename and safe result
summary remain available in job history.

## Database backup and restore

The `settings` table stores the OMDb key as plain text. A SQL backup therefore contains the provider credential; protect backup files and their storage with the same care as `.env`, and rotate the key if a backup is exposed.

For a new Ubuntu host, install `deploy/backup.sh` with `deploy/systemd/mediadock-next-backup.service` and `deploy/systemd/mediadock-next-backup.timer`. It creates a root-only custom-format dump at 03:00 UTC under `/opt/docker/backups/mediadock-next`, validates it with `pg_restore -l`, and keeps the newest 14 daily dumps. The existing Restic job backs up `/opt/docker` afterward. Do not enable the timer until the production `.env` and Compose paths have been reviewed.

The optional deployment service is documented in `deploy/systemd/README.md`.
It fetches only GitHub `main`, runs the clean staging gate, creates a
pre-migration dump, uses versioned images, and checks readiness after startup.
On a new host, keep its timer disabled until the manual deployment and rollback
procedure have been reviewed.

Create a plain SQL backup inside the container, then copy it to the host:

```powershell
docker compose -f compose.yaml exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" > /tmp/mediadock.sql'
docker compose -f compose.yaml cp db:/tmp/mediadock.sql .\mediadock.sql
```

Restore into an empty database so existing data is not overwritten:

```powershell
docker compose -f compose.yaml cp .\mediadock.sql db:/tmp/mediadock.sql
docker compose -f compose.yaml exec -T db sh -c 'createdb -U "$POSTGRES_USER" mediadock_restore'
docker compose -f compose.yaml exec -T db sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" mediadock_restore < /tmp/mediadock.sql'
```

## Stop

Stop the local services without deleting the database volume:

```powershell
docker compose -f compose.yaml stop
```