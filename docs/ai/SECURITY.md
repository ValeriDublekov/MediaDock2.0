# Security

This page records security boundaries for the standalone application. See [project context](PROJECT_CONTEXT.md) for local-versus-production boundaries, the [local runbook](../../README.md) for application setup, and the [systemd runbook](../../deploy/systemd/README.md) for host configuration.

## Network And Authorization

Compose publishes the API on `${APP_BIND_ADDRESS:-127.0.0.1}:${APP_PORT:-8080}` and PostgreSQL on loopback ([Compose](../../compose.yaml)). The API endpoints use HTTP, not TLS. Loopback limits remote network access but does not authenticate local callers. A LAN deployment must bind the API to a specific trusted interface and use an API-port-only `DOCKER-USER` source allowlist; PostgreSQL remains loopback-only. Do not publish or forward the API to untrusted clients.

Forwarded-header processing is disabled when `ForwardedHeaders:TrustedNetwork` is empty. When configured, the API trusts only `X-Forwarded-Proto` from that CIDR and accepts one hop. For Cloudflare Tunnel, use the dedicated bridge containing only the API and Tunnel connector, not the default Compose network or a wildcard CIDR. This setting handles the Tunnel's HTTP origin connection; it does not authorize requests.

Google sign-in is optional and disabled by default. `ACCESS_CONTROL_ENFORCED` also defaults to `false`, leaving most API routes unprotected. Favorites and personal ratings always require an active linked user; other protected routes require an active account and, for global configuration, user administration, operations, and jobs, an administrator when enforcement is enabled. In open mode, reachable clients can change configuration and manage accounts. Anti-forgery validation applies only to designated mutations and is not authorization. A Google session or Cloudflare Access does not provide application-level API authorization. Keep open mode within the configured loopback or trusted-network boundary.

Enable sign-in only after the HTTPS route, Cloudflare Access policy, and exact Google callback have been verified end to end; the raw HTTP LAN origin is not an acceptable sign-in origin. See the [local runbook](../../README.md#cloudflare-tunnel-google-callback) for callback setup and [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md) for the authentication contract.

The host-only `bootstrap-admin` tool trusts operator-supplied identity values and does not verify Google claims. Obtain them from an authenticated `/api/auth/session` response and remove the bootstrap values from `.env` after success. See the [bootstrap procedure](../../README.md#initial-administrator-bootstrap).

The optional Configuration deployment panel lets any client inside the same LAN boundary trigger the full host deployment pipeline, including migrations. The host service exposes only fixed actions over a Unix socket, but this remains a high-impact unauthenticated control. Keep the API inaccessible outside the trusted LAN; see [operations and jobs](API_OPERATIONS_AND_JOBS.md) and the [systemd runbook](../../deploy/systemd/README.md#web-deployment-control).

## Secrets

Keep real values in the ignored `.env` file or host environment, never in source, documentation, or the browser bundle. `.env` is plaintext and needs host permissions and protected backups. Replace the development-only database password in the checked-in [`.env.example`](../../.env.example) before local use.

The MVP has no secrets manager. Do not reuse production credentials for local work.

Keep `GOOGLE_AUTH_CLIENT_SECRET` in ignored host configuration. Never log authorization codes, provider tokens, or session cookies, and never put OAuth secrets in bootstrap fields. Provider tokens are not persisted in the browser or session ticket.

The OMDb key is stored as plaintext in PostgreSQL settings and is included in SQL backups. The API does not return its value, but provider-settings writes are unauthenticated in open mode. Protect backups and rotate the key if one is exposed. The key is sent to OMDb in a query parameter; do not log full outbound request URLs ([client](../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs)).

Access to the Docker socket is root-equivalent on the host. Run the [server-side test gate](../../deploy/test.sh) only from a clean checkout of trusted `main`; only its Testcontainers integration container receives the socket. The gate uses validation-only values and does not mount production `.env` or volumes. See [testing](TESTING.md).

## RSS Fetching

The RSS transport accepts HTTPS URLs only on `feed.rutracker.cc`, rejects URL credentials, and requires public DNS answers. It connects to validated addresses and revalidates redirect targets; automatic redirects are disabled. Responses and entry counts are bounded, and XML DTD and external resolution are disabled. These are feed-fetch controls, not a general network egress policy. See the [transport](../../server/src/MediaDock.Infrastructure/Rss/RssFeedTransport.cs) and [transport tests](../../server/tests/MediaDock.UnitTests/RssFeedTransportTests.cs).