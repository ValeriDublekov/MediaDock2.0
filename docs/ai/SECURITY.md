# Security

This page records security boundaries for the standalone application. See [project context](PROJECT_CONTEXT.md) for local-versus-production boundaries, the [local runbook](../../README.md) for application setup, and the [systemd runbook](../../deploy/systemd/README.md) for host configuration.

## Network And Authorization

Compose publishes the API on `${APP_BIND_ADDRESS:-127.0.0.1}:${APP_PORT:-8080}` and PostgreSQL on loopback ([Compose](../../compose.yaml)). The API endpoints use HTTP, not TLS. Loopback limits remote network access but does not authenticate local callers. A LAN deployment must bind the API to a specific trusted interface and use an API-port-only `DOCKER-USER` source allowlist; PostgreSQL remains loopback-only. Do not publish or forward the API to untrusted clients.

Forwarded-header processing is disabled when `ForwardedHeaders:TrustedNetwork` is empty. When configured, the API trusts only `X-Forwarded-Proto` from that CIDR and accepts one hop. For Cloudflare Tunnel, use the dedicated bridge containing only the API and Tunnel connector, not the default Compose network or a wildcard CIDR. This setting handles the Tunnel's HTTP origin connection; it does not authorize requests.

Google sign-in and `AccessControl:Enforced` may be disabled in Development. A normal Production API startup fails unless Google sign-in is enabled with valid client credentials and callback configuration and access enforcement is enabled. The Production Compose API therefore does not start with the disabled values in `.env.example`; configure the approved Google client and `ACCESS_CONTROL_ENFORCED=true` first. The migration and administrator-bootstrap one-shot commands remain available without browser-auth settings. `/api/version` is protected by the authorization fallback policy; only the sign-in/session/account protocol and internal health probes are anonymous. A Google session or Cloudflare Access does not replace application-level authorization; Cloudflare Access remains a separate edge control. Keep Development open mode loopback-bound.

Enable sign-in only after the HTTPS route, Cloudflare Access policy, and exact Google callback have been verified end to end; the raw HTTP LAN origin is not an acceptable sign-in origin. See the [local runbook](../../README.md#cloudflare-tunnel-google-callback) for callback setup and [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md) for the authentication contract.

The host-only `bootstrap-admin` tool trusts operator-supplied identity values and does not verify Google claims. Obtain them from an authenticated `/api/auth/session` response and remove the bootstrap values from `.env` after success. See the [bootstrap procedure](../../README.md#initial-administrator-bootstrap).

The optional Configuration deployment panel lets any client inside the same LAN boundary trigger the full host deployment pipeline, including migrations. The host service exposes only fixed actions over a Unix socket, but this remains a high-impact unauthenticated control. Keep the API inaccessible outside the trusted LAN; see [operations and jobs](API_OPERATIONS_AND_JOBS.md) and the [systemd runbook](../../deploy/systemd/README.md#web-deployment-control).

## Local Development Mock Authentication

`Authentication:LocalMock:Enabled` is an authentication bypass for local testing. Never add it to `.env`, `compose.yaml`, a production host environment, or a shared launch profile. The production Compose service sets `ASPNETCORE_ENVIRONMENT=Production` and does not pass this setting.

Start the restored-data development API only with [`run_local_restored.ps1`](../../scripts/run_local_restored.ps1). The script reads credentials from the local Compose configuration, targets the local restored database, verifies exactly one active user and linked Google identity, enables authorization enforcement, disables Google OAuth and the background-job dispatcher, and binds the API to `http://localhost:5280`. It sets the mock flag only in the runner process and restores prior environment values when the process exits.

The API rejects the mock flag outside `Development`, when Google sign-in is enabled, or when the database connection is not loopback. The mock scheme is registered only in `Development` and rejects non-loopback client addresses. If the flag is accidentally configured for production, service registration fails rather than enabling the mock.

Stop the API with **Ctrl+C** in the terminal running the script. This stops only the local API; the Vite server and local PostgreSQL container remain running, and the restored database is retained. The restored database contains production data and settings, including the plaintext OMDb key; keep it local and replace that key before enabling background jobs.

## Secrets

Keep real values in the ignored `.env` file or host environment, never in source, documentation, or the browser bundle. `.env` is plaintext and needs host permissions and protected backups. Replace the development-only database password in the checked-in [`.env.example`](../../.env.example) before local use.

The MVP has no secrets manager. Do not reuse production credentials for local work.

Keep `GOOGLE_AUTH_CLIENT_SECRET` in ignored host configuration. Never log authorization codes, provider tokens, or session cookies, and never put OAuth secrets in bootstrap fields. Provider tokens are not persisted in the browser or session ticket.

The OMDb key is stored as plaintext in PostgreSQL settings and is included in SQL backups. The API does not return its value, but provider-settings writes are unauthenticated in open mode. Protect backups and rotate the key if one is exposed. The key is sent to OMDb in a query parameter; do not log full outbound request URLs ([client](../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs)).

Access to the Docker socket is root-equivalent on the host. Run the [server-side test gate](../../deploy/test.sh) only from a clean checkout of trusted `main`; only its Testcontainers integration container receives the socket. The gate uses validation-only values and does not mount production `.env` or volumes. See [testing](TESTING.md).

## RSS Fetching

The RSS transport accepts HTTPS URLs only on `feed.rutracker.cc`, rejects URL credentials, and requires public DNS answers. It connects to validated addresses and revalidates redirect targets; automatic redirects are disabled. Responses and entry counts are bounded, and XML DTD and external resolution are disabled. These are feed-fetch controls, not a general network egress policy. See the [transport](../../server/src/MediaDock.Infrastructure/Rss/RssFeedTransport.cs) and [transport tests](../../server/tests/MediaDock.UnitTests/RssFeedTransportTests.cs).