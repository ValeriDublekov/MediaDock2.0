# Security And Operations

This guide covers security and operations for this standalone application; see [project context](PROJECT_CONTEXT.md). The [local runbook](../../README.md) is canonical for Docker, backup, and restore commands. The [systemd runbook](../../deploy/systemd/README.md) is canonical for host scheduling and unit installation.

## Current Production Status (2026-09-30)

Production was first deployed from GitHub `main` commit
`2dd39927d5a0b02d74c9f7f8e3212a8e74c731c8` from
`/opt/docker/projects/mediadock-next`. The live SHA is in the server's root-managed
`/var/lib/mediadock-deploy/deploy-state`. The legacy checkout is retained beside
it for rollback. The new app uses database `mediadock2` with one baseline
migration; the original `mediadock` database and six-migration history remain
unchanged. The pre-migration dump passed `pg_restore -l` and was verified in
the latest Restic snapshot.

Readiness, UI, and catalog checks returned HTTP 200. The API is bound to the
specific trusted LAN interface at port `8081`; PostgreSQL remains loopback-only.
The deploy timer is enabled for the new GitHub `main`. The production image
predates the background-job migrations. The legacy Worker service is installed,
its timer is disabled/uninstalled, and no scan has run. The API has no authentication, so
allowed LAN clients can read and change application data. Router port-forward
and non-LAN denial checks remain outstanding; do not treat LAN checks as proof
of public-network isolation. See the
[systemd runbook](../../deploy/systemd/README.md) for current operations.

## Trust Boundary

The Compose stack publishes the API on `${APP_BIND_ADDRESS:-127.0.0.1}:${APP_PORT:-8080}` and PostgreSQL on `127.0.0.1:${POSTGRES_PORT:-5432}` ([Compose](../../compose.yaml)). The API defaults to loopback. A LAN deployment may bind only to a specific trusted interface and must use a matching `DOCKER-USER` source allowlist; PostgreSQL stays loopback-only. The API's container port is for Compose networking. The documented endpoints use HTTP, not TLS. Loopback limits remote network access but does not authenticate local processes or users that can reach the port.

The API does not register authentication or authorization middleware ([API contracts](API_CONTRACTS.md), [API startup](../../server/src/MediaDock.Api/Program.cs)). This includes source creation/update, matching settings, and provider settings writes. A client that can reach the API can read catalog and operational data and change feed configuration, matching rules, and the saved OMDb key. Do not publish the port on a wildcard interface or forward it through a proxy, tunnel, or other network path for untrusted clients. This MVP has no user identity, roles, or user-attributed audit trail; loopback is not a substitute for authentication.

A LAN deployment must use a specific trusted IPv4 bind and an API-port-only `DOCKER-USER` policy that allows the trusted source subnet and drops other sources; a systemd unit can reapply that policy after Docker starts. Store the host IP and trusted subnet in a root-owned host configuration file, not in Git. This does not authenticate LAN clients: every client in the trusted subnet can read and change app data. Do not enable the API listener until the trusted deployment checkout contains the bind-address parameter and Compose confirms the expected mapping; verify LAN access and non-LAN denial during the initial deployment smoke tests.

When enabled, the Configuration deployment panel can start the full host deployment pipeline from any client allowed by that same LAN rule. A root-owned host service exposes only status and fixed `check`/`retry_failed_gate` actions over a Unix socket mounted read-only into the API container; no Docker socket or shell is exposed to the API. This remains a high-impact unauthenticated control: clients can trigger a deploy of current `main` and its migrations. Do not publish or forward the API port outside the trusted LAN. A `deploy-failed` recovery marker blocks both scheduled and web-triggered deploys.

## Secrets

- Keep real values in the ignored `.env` file or the host environment, never in source, documentation, the browser bundle, or the checked-in example. The `.env` file is excluded by both [`.gitignore`](../../.gitignore) and [`.dockerignore`](../../.dockerignore); it is still plaintext and must be protected by host file permissions and backup handling.
- The checked-in [`.env.example`](../../.env.example) contains a development-only database password. Replace it with a local-only value before starting services. `POSTGRES_PASSWORD` is required and is supplied to PostgreSQL and the API connection string by Compose.
- Configure the OMDb API key and request limits in the web UI. The key is stored as plain text in PostgreSQL `settings`; the API returns only whether a key is configured, not its value. SQL backups contain the key, so protect them and rotate the credential if a backup is exposed. Provider-settings writes are unauthenticated; any client on the trusted LAN can replace or clear the key. API-hosted scan jobs require a positive shared daily limit matching the verified quota. Oscar enrichment additionally uses per-run film and daily request caps. The total and Oscar counters plus a provider-quota stop flag are stored by UTC date in `omdb_daily_usage`, without the key or a key hash. Each non-cache HTTP attempt reserves a slot before sending, including fallback requests and retries; cache hits do not count. A process failure between reservation and send may conservatively leave a slot unused. A provider 429 or quota response blocks further HTTP reservations for the UTC day across API restarts. The Oscar maximum is not reserved from RSS usage. The API sends the key to the fixed OMDb HTTPS endpoint as a query parameter ([OMDb client](../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs)); avoid logging full outbound request URLs or enabling wire dumps that include them.
- The MVP does not provide a secrets manager. Do not reuse production credentials for local work. On the systemd host, access to the Docker socket is privileged: the [systemd runbook](../../deploy/systemd/README.md) notes that membership in the `docker` group grants root-equivalent host access.
- Run the [server-side test gate](../../deploy/test.sh) only against a clean checkout of a trusted `main` commit. It uses an empty Compose env file and validation-only values; it does not start production services or mount the production `.env` or volumes. Only the Testcontainers integration-test container receives the Docker socket, which grants root-equivalent host access. Its temporary PostgreSQL host-port binding is limited to `127.0.0.1`.

## RSS Fetching And SSRF Controls

The API's RSS feed transport accepts only absolute HTTPS URLs on `feed.rutracker.cc`, without URL credentials or whitespace, and caps URL length at 2,048 characters ([transport](../../server/src/MediaDock.Infrastructure/Rss/RssFeedTransport.cs)). DNS answers must be public addresses; private, loopback, link-local, and reserved ranges are rejected. The connection callback resolves and validates again and connects directly to the validated address, and automatic redirects are disabled. Each redirect is limited and its URL and DNS results are revalidated before following it ([DNS policy and HTTP factory](../../server/src/MediaDock.Infrastructure/Rss/RssFeedDnsResolver.cs), [HTTP factory](../../server/src/MediaDock.Infrastructure/Rss/RssFeedHttpClientFactory.cs)).

Per feed request, connection timeout is 5 seconds, request timeout is 20 seconds, the decompressed response is limited to 4 MiB, and RSS/Atom entry count is limited to 500. Only recognized XML feed content types are accepted. XML DTD processing is prohibited and external resolution is disabled ([XML validator](../../server/src/MediaDock.Infrastructure/Rss/RssFeedXmlValidator.cs)). These are application-level feed-fetch controls, not a general network egress policy. The isolated [transport tests](../../server/tests/MediaDock.UnitTests/RssFeedTransportTests.cs) cover URL validation, private/mixed DNS answers, redirects, timeouts, response limits, and malformed or oversized feeds.

## Migrations And Operations

Schema changes are checked in as EF Core migrations ([migration history](../../server/src/MediaDock.Infrastructure/Persistence/Migrations/)). The normal API startup does not migrate. The explicit Compose `migrate` service sets `migrate=true`, applies pending migrations, and exits; run it in the order documented in the [local runbook](../../README.md) before starting the API after a schema change. Compose does not make API startup depend on that service. `/health/ready` checks database connectivity, not whether the expected schema migration has been applied.

The API-hosted `BackgroundJobDispatcher` processes manual and scheduled work
from `background_jobs`; it is the only executor. Manual producers return after
durable enqueue. The scheduler creates slots at 07:00 and 18:00 in
`Europe/Sofia`, coalescing downtime into at most one catch-up job. The
PostgreSQL advisory lock spans claim through terminal job state and serializes
RSS, separate Oscar enrichment, and CSV import. A job interrupted by shutdown
or crash is marked failed with the safe code `interrupted` after restart; it is
not retried automatically. CSV upload bytes are bounded, persisted for queued
work, omitted from responses, and erased at terminal state.

Production has not yet migrated to this source architecture: the installed API
image predates the background-job schema; the legacy Worker service is
installed, its timer is not installed/enabled, and no scan has run. Before
cutover, verify a fresh backup and the deployed migration state, stop/disable
the legacy service/timer, test migrations on an isolated database, then follow
the [systemd runbook](../../deploy/systemd/README.md). Do not treat publishing
this source as proof that production has migrated.

Oscar enrichment lifecycle and progress are stored in the separate `oscar_enrichment_runs` table; RSS `scan_runs` and `parse_logs` retain their existing meanings. The [local runbook](../../README.md) documents the audit and UTC-budget queries and the response to a provider quota stop. The Kaggle Oscar dataset page identifies the source dataset as CC0; keep the downloaded file, retrieval date/version, and checksum outside Git. OMDb describes its content terms as CC BY-NC 4.0; review the applicable terms before redistributing data or using it commercially.

Keep operational commands in their existing runbooks: [README.md](../../README.md) owns Compose setup, ingestion workflow, and manual database backup/restore; [deploy/systemd/README.md](../../deploy/systemd/README.md) owns host deployment, backup, and firewall unit operations. This guide records boundaries and policy, not duplicate command sequences.