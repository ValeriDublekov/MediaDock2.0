# Security

This page records security boundaries for the standalone application. Open application/dependency changes are in the [code security review](SECURITY_AUDIT_2026-10-26.md); host/release findings are in the separate [deployment review](SECURITY_DEPLOYMENT_REVIEW_2026-10-30.md). See [project context](PROJECT_CONTEXT.md) for local-versus-production boundaries, the [local runbook](../../README.md) for application setup, and the [systemd runbook](../../deploy/systemd/README.md) for host configuration.

## Network And Authorization

Compose publishes the API on `${APP_BIND_ADDRESS:-127.0.0.1}:${APP_PORT:-8080}` and PostgreSQL on loopback ([Compose](../../compose.yaml)). The API endpoints use HTTP, not TLS. Loopback limits remote network access but does not authenticate local callers. A LAN deployment must bind the API to a specific trusted interface and use an API-port-only `DOCKER-USER` source allowlist; PostgreSQL remains loopback-only. Do not publish or forward the API to untrusted clients.

Forwarded-header processing is disabled when `ForwardedHeaders:TrustedNetwork` is empty. When configured, the API trusts `X-Forwarded-Proto` and `X-Forwarded-For` only from that CIDR and accepts one hop. The forwarded client address is used for application rate-limit partitions; untrusted client-supplied forwarding headers do not change the rate-limit identity. For Cloudflare Tunnel, use the dedicated bridge containing only the API and Tunnel connector, not the default Compose network or a wildcard CIDR. Forwarded headers do not authorize requests.

Google sign-in and `AccessControl:Enforced` may be disabled in Development. A normal Production API startup fails unless Google sign-in is enabled with valid client credentials and callback configuration and access enforcement is enabled. The Production Compose API therefore does not start with the disabled values in `.env.example`; configure the approved Google client and `ACCESS_CONTROL_ENFORCED=true` first. The migration and administrator-bootstrap one-shot commands remain available without browser-auth settings. `/api/version` is protected by the authorization fallback policy; only the sign-in/session/account protocol and internal health probes are anonymous. A Google session or Cloudflare Access does not replace application-level authorization; Cloudflare Access remains a separate edge control. Keep Development open mode loopback-bound.

Enable sign-in only after the HTTPS route, Cloudflare Access policy, and exact Google callback have been verified end to end; the raw HTTP LAN origin is not an acceptable sign-in origin. See the [local runbook](../../README.md#cloudflare-tunnel-google-callback) for callback setup and [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md) for the authentication contract.

The host-only `bootstrap-admin` tool trusts operator-supplied identity values and does not verify Google claims. Obtain them from an authenticated `/api/auth/session` response and remove the bootstrap values from `.env` after success. See the [bootstrap procedure](../../README.md#initial-administrator-bootstrap).

In current source, the optional Configuration deployment panel requires an active administrator when enforcement is enabled, and cookie-authenticated mutations pass the central CSRF/origin checks. The host service exposes only fixed actions, but its Unix socket is currently world-writable in a traversable directory; local callers can bypass those application checks. Restrict its caller identity/permissions or disable the bridge before exposure. An older deployment with enforcement disabled also remains unsafe regardless of LAN or Cloudflare access; see the [deployment review](SECURITY_DEPLOYMENT_REVIEW_2026-10-30.md), [operations and jobs](API_OPERATIONS_AND_JOBS.md), and [systemd runbook](../../deploy/systemd/README.md#web-deployment-control).

## Browser And Cache Policy

Production serves the public static web root before authorization, excluding `/api` paths, and explicitly allows the SPA shell anonymously. `/` and `/login` are public entry pages. Once the session check confirms an anonymous visitor, React redirects other page URLs to `/login` without mounting private views. Account approval remains required for application content. Known business APIs retain authorization; unknown `/api` paths return 404 rather than the SPA shell. This fixes A10 in source; it is not evidence that the running release contains the fix.

The API adds `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, a restrictive referrer and permissions policy, and a `Content-Security-Policy-Report-Only` policy. CSP remains report-only while browser behavior is assessed; this policy has no report collection endpoint. HSTS is disabled by default. After HTTPS coverage for every public hostname is verified, set `HSTS_ENABLED=true`; the API then emits HSTS only in Production when ASP.NET sees an HTTPS request, including HTTPS established by a trusted forwarded-proto header. It uses a one-year max age without `includeSubDomains`. HTML, all `/api/` responses and the `/signin-oidc` callback use `Cache-Control: no-store`. These application headers do not establish what Cloudflare or another edge returns to clients.

Feed-derived source links are rendered only for absolute HTTP or HTTPS URLs, and links opened in a new tab use `noopener noreferrer`.

## Application Rate Limits

The API uses per-client-IP sliding windows for authentication/session/token routes, registration, expensive searches and reads, uploads, and job dispatch. Upload and dispatch requests also use no-queue concurrency limits. A rejected request receives a generic 429 Problem Details response and `Retry-After`; upload size overruns receive a generic 413. Existing upload byte caps, paging limits, and job-level duplicate protection remain in force.

Every non-Development API startup requires explicit rate-limit settings; no production thresholds are embedded in source or supplied by `.env.example`. Configure these ignored `.env` variables only after reviewing expected legitimate traffic and coordinating with the Cloudflare policy:

| Route category | Required `.env` values |
| --- | --- |
| Authentication, session, and token issuance | `API_RATE_LIMIT_AUTHENTICATION_PERMIT_LIMIT`, `API_RATE_LIMIT_AUTHENTICATION_WINDOW_SECONDS` |
| Registration requests | `API_RATE_LIMIT_REGISTRATION_PERMIT_LIMIT`, `API_RATE_LIMIT_REGISTRATION_WINDOW_SECONDS` |
| Expensive search and read routes | `API_RATE_LIMIT_SEARCH_PERMIT_LIMIT`, `API_RATE_LIMIT_SEARCH_WINDOW_SECONDS` |
| Uploads | `API_RATE_LIMIT_UPLOADS_PERMIT_LIMIT`, `API_RATE_LIMIT_UPLOADS_WINDOW_SECONDS`, `API_RATE_LIMIT_UPLOADS_CONCURRENCY_LIMIT` |
| Job dispatch | `API_RATE_LIMIT_JOB_DISPATCH_PERMIT_LIMIT`, `API_RATE_LIMIT_JOB_DISPATCH_WINDOW_SECONDS`, `API_RATE_LIMIT_JOB_DISPATCH_CONCURRENCY_LIMIT` |

The Compose API maps these values to `AbuseControls:RateLimits:*`. Permit limits and windows must be positive; upload and dispatch concurrency limits must also be positive. When `ForwardedHeaders:TrustedNetwork` is empty, the remote TCP peer is the client key. Configure and verify the dedicated proxy network before relying on per-user IP partitions behind a proxy.

## Local Development Mock Authentication

`Authentication:LocalMock:Enabled` is an authentication bypass for local testing. Never add it to `.env`, `compose.yaml`, a production host environment, or a shared launch profile. The production Compose service sets `ASPNETCORE_ENVIRONMENT=Production` and does not pass this setting.

Start the restored-data development API only with [`run_local_restored.ps1`](../../scripts/run_local_restored.ps1). The script reads credentials from the local Compose configuration, targets the local restored database, verifies exactly one active user and linked Google identity, enables authorization enforcement, disables Google OAuth and the background-job dispatcher, and binds the API to `http://localhost:5280`. It sets the mock flag only in the runner process and restores prior environment values when the process exits.

The API rejects the mock flag outside `Development`, when Google sign-in is enabled, or when the database connection is not loopback. The mock scheme is registered only in `Development` and rejects non-loopback client addresses. If the flag is accidentally configured for production, service registration fails rather than enabling the mock.

Stop the API with **Ctrl+C** in the terminal running the script. This stops only the local API; the Vite server and local PostgreSQL container remain running, and the restored database is retained. The restored database contains production data and settings, including the plaintext OMDb key; keep it local and replace that key before enabling background jobs.

The Vite development server explicitly binds to `127.0.0.1` and rejects non-loopback host overrides. The API remains bound to `localhost:5280`. Do not tunnel or port-forward either service while mock authentication is enabled; a separate forwarding process can expose a loopback listener even when the application itself binds only to loopback. Use sanitized data for shared development.

## Data Protection Keys

The Production Compose API persists ASP.NET Data Protection keys in the `data_protection_keys` Docker volume at `/var/lib/aspnet/DataProtection-Keys`, with the application name fixed to `MediaDock`. The image initializes the directory for its non-root application UID with mode `0700`. The volume is separate from the PostgreSQL dump and must be included in protected host backups and restore drills. The application does not encrypt this key ring at rest; verify host volume permissions and configure/protect the storage or backup encryption before internet exposure. Do not copy key material into Git.

## Secrets

Keep real values in the ignored `.env` file or host environment, never in source, documentation, or the browser bundle. `.env` is plaintext and needs host permissions and protected backups. Replace the development-only database password in the checked-in [`.env.example`](../../.env.example) before local use.

The MVP has no secrets manager. Do not reuse production credentials for local work.

Keep `GOOGLE_AUTH_CLIENT_SECRET` in ignored host configuration. Never log authorization codes, provider tokens, or session cookies, and never put OAuth secrets in bootstrap fields. Provider tokens are not persisted in the browser or session ticket.

The OMDb key is stored as plaintext in PostgreSQL settings and is included in SQL backups. The API does not return its value, but provider-settings writes are unauthenticated in open mode. Protect backups and rotate the key if one is exposed. The key is sent to OMDb in a query parameter; do not log full outbound request URLs ([client](../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs)).

Access to the Docker socket is root-equivalent on the host. Run the [server-side test gate](../../deploy/test.sh) only from a clean checkout of trusted `main`; only its Testcontainers integration container receives the socket. The gate uses validation-only values and does not mount production `.env` or volumes. See [testing](TESTING.md).

## RSS Fetching

The RSS transport accepts HTTPS URLs only on `feed.rutracker.cc`, rejects URL credentials, and requires public DNS answers. It connects to validated addresses and revalidates redirect targets; automatic redirects are disabled. Responses and entry counts are bounded, and XML DTD and external resolution are disabled. These are feed-fetch controls, not a general network egress policy. See the [transport](../../server/src/MediaDock.Infrastructure/Rss/RssFeedTransport.cs) and [transport tests](../../server/tests/MediaDock.UnitTests/RssFeedTransportTests.cs).