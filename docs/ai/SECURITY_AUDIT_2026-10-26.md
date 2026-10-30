# Application Code Security Review (2026-10-30)

## Summary

**There are application-code improvements to make.** A10's Production login delivery defect and anonymous page redirects are now fixed and verified locally; deployment of this follow-up is still pending. Other work concerns shared-data permissions, vulnerable development dependencies, rate-limit coverage, browser response policy, session revocation and security-event instrumentation.

This document covers only application source, dependencies and tests under `server/` and `web/`. Deployed-version state, Cloudflare/firewall/TLS, Compose/database roles, host deployment scripts/socket permissions, keys/backups and operational checks are in the separate [deployment and infrastructure review](SECURITY_DEPLOYMENT_REVIEW_2026-10-30.md). A deployment observation is not listed here as an unfixed code defect.

The target remains a validated Google identity linked to an active, administrator-approved MediaDock account. Unauthenticated visitors should see only a minimal sign-in page, with no application navigation, data or actions. A hidden React route is not API authorization; public sign-in JavaScript/CSS are not private application data. This is a risk-based code review, not a penetration test or formal verification of every algorithm/migration.

## Scope and evidence

Baseline source commit from the 2026-10-30 review: `9f6ddee8055e0e311eb9fdcd399c01ca29f5d0ab`. Only documentation edits were authored for the audit and this scope separation; no application fixes or package upgrades were performed. Concurrent frontend edits were preserved; a final access-gate read and App test rerun passed during the original audit. Unrelated UI changes are not covered by that evidence.

Reviewed API startup and endpoint registrations across every API group, authorization and Google session/linking code, request/response protections, owner-scoped data services, upload/job boundaries, RSS/OMDb transports, React access gates/API client and Vite configuration. Findings identify the code owner, proposed change and regression check. Original finding IDs are retained; gaps in numbering refer to the separate deployment findings or already-addressed issues.

### Recorded code checks

The following results are from the preceding 2026-10-30 audit, not new runs during this documentation split. Every test used the [required repository runner](TESTING.md). API integration tests used disposable PostgreSQL containers; server tests used `Configuration=Release` to avoid the running development API's locked Debug outputs.

| Check | Result |
| --- | --- |
| `python -B scripts/run_tests.py server-integration --filter "Category=Api"` | **76/76 passed**: auth, authorization, personal-data isolation, uploads, jobs, settings, and deployment API coverage. |
| `python -B scripts/run_tests.py web --filter App.test.tsx` | **36/36 passed**. |
| `python -B scripts/run_tests.py web --filter AuthStatus.test.tsx` | **9/9 passed**. |
| `python -B scripts/run_tests.py web --filter client.test.ts` | **37/37 passed**. |
| `python -B scripts/run_tests.py web --filter vite.config.test.ts` | **7/7 passed**. |
| `python -B scripts/run_tests.py server-unit --filter "Category=RssTransport\|FullyQualifiedName~OmdbClient"` | **18/18 passed**: selected outbound transport safeguards. The backslash in this Markdown table only escapes the table separator; the actual filter contains a literal pipe. |
| Local Production-mode HTTP probe | **Launch blocker confirmed:** anonymous HTML and JavaScript return 401; see A10. |
| `npm audit --prefix web --json` | **5 affected packages: 3 high, 2 moderate**, all in development/build dependencies; see A12. |
| `npm audit --prefix web --omit=dev --audit-level=moderate` | **0 reported production dependency vulnerabilities**. |
| `dotnet list server/MediaDock.sln package --vulnerable --include-transitive --format json` | **No reported vulnerable packages** across 5 projects, source `https://api.nuget.org/v3/index.json`. |

Total distinct passing application tests: **183**; the 36 App tests also passed on a final rerun. The earlier combined total of 186 included 3 deployment-tool tests, now recorded separately. One initial combined web-filter command failed in Windows shell parsing before tests ran; literal file selections succeeded. Passing tests did not catch A10. Logs and dependency reports are under ignored `.test-tmp/`. Advisory results are time- and database-dependent.

## Findings

Severity assumes potentially malicious visitors or approved users. A **functional blocker** prevents the requested login-only experience; it is not necessarily a data-disclosure vulnerability. Conditional findings require a permission/product decision before implementation. Optional hardening is distinguished from confirmed defects.

| ID | Priority / state | Finding and required outcome |
| --- | --- | --- |
| A10 | **Corrected locally; deployment pending** | Public shell/assets and `/login` redirects are regression-tested; business APIs remain protected. Verify the approved deployed release separately. |
| A12 | **High: development supply chain** | Five npm development/build packages have advisories. Patch the dependency tree/lockfile, retest and rescan. |
| A16 | **Conditional High: permission decision** | Any active user can rewrite and merge shared catalog titles through IMDb matching. Make this admin-only unless collaborative mutation is explicitly approved. |
| A15 | **Medium: incomplete code coverage** | Extend route limits to uncovered costly handlers; add a process-wide ceiling where per-IP concurrency is insufficient. |
| A7 | **Medium: browser hardening** | Add a tested enforceable CSP mode and deliberate HTML/private-response cache policy. |
| A17 | **Medium: session/audit hardening** | Choose logout revocation semantics; add the selected mechanism and missing structured actor/action/outcome events. |

Already-addressed source issues are not open tasks: unexpected 500 detail leakage (former A4), missing centralized CSRF (former A5), missing rate limiting entirely (former A6), and unsafe normal-Production startup (source portion of A1). Retain their tests. The earlier public-asset claim (A2) was incorrect for current Production composition and is replaced by reproduced A10.

### A10. Production blocks delivery of the login page (launch blocker)

At the audited baseline, [Program.cs](../../server/src/MediaDock.Api/Program.cs) ran `UseAuthorization()` before `UseDefaultFiles()` and `UseStaticFiles()`. The [fallback policy](../../server/src/MediaDock.Api/Authentication/AccessControl.cs) required an active user even without explicit endpoint authorization metadata, and the SPA fallback had no anonymous exception. Consequently, an unauthenticated browser could not fetch the page containing the Google sign-in button. This historical defect is superseded by the local follow-up below.

**Original reproduction:** started the baseline Release API on `127.0.0.1:5287` in Production with enforcement enabled, placeholder Google credentials/callback, an unreachable dummy database, dispatcher disabled, existing web build as web root, and temporary Data Protection keys under `.test-tmp/`. No real database or OAuth provider was contacted. The process was stopped after checking:

| Anonymous GET | Observed status |
| --- | --- |
| `/`, `/catalog`, `/index.html`, existing `/assets/index-BC9-4mVK.js` | **401** |
| `/api/auth/session` | **200** |
| `/api/version`, `/api/catalog` | **401** |
| `/health/live` | **200** |

**Implemented locally (2026-10-30):** static resources outside `/api` now run before authorization, and the SPA fallback explicitly allows anonymous delivery. A separate API fallback returns 404, including unknown paths with file extensions, without serving HTML. Known API authorization is unchanged. [App.tsx](../../web/src/App.tsx) keeps `/` and `/login` available to anonymous visitors and replaces other page locations with `/login` after the session check. Active approved users visiting `/login` continue to the catalog; pending/unapproved identities retain their account-status gates. HTML responses use `Cache-Control: no-store`. Public client code is not an authorization boundary and must contain no secrets.

**Follow-up verification:** the new Production-host regression in [GoogleAuthenticationApiTests.cs](../../server/tests/MediaDock.IntegrationTests/GoogleAuthenticationApiTests.cs) passed, followed by **77/77 API integration tests** using disposable PostgreSQL and **220/220 web tests** through the required runner. The App selection passed **45/45**; lint completed with existing warnings outside this change, and the production web build passed. These are new follow-up results, separate from the original audit table above.

A real loopback Kestrel Production host on port 5291 served the built web app with enforcement enabled, synthetic OAuth configuration, an unreachable dummy database, dispatcher disabled and keys under `.test-tmp/`. Fresh desktop/mobile browser contexts loaded `/` and `/login` with HTTP 200 and redirected `/catalog`, `/favorites` and `/configuration/users` to `/login`. JS/CSS returned 200; the only browser API call before sign-in was `/api/auth/session`. Direct catalog/version calls remained 401 and unknown API calls returned 404 without HTML. Screenshots are in ignored `.test-tmp/`. No real Google login or deployed-host acceptance was performed; the source fix still needs an approved release.

### A12. Vulnerable development/build dependencies (high)

The current npm advisory report and `npm ls --prefix web vite vitest @vitest/mocker source-map-js undici --all` identify:

| Package | Installed | npm severity | Advisory examples / exposure |
| --- | --- | --- | --- |
| `vite` | `8.3.1` | High | Windows launch-editor command handling and development-server file exposure; [GHSA-587h-7wmj-h5pm](https://github.com/advisories/GHSA-587h-7wmj-h5pm). |
| `undici` | `8.11.2` via `jsdom` | High | TLS-option loss, redirect/cache disclosure, and denial-of-service reports; [GHSA-2m5v-pch6-74hx](https://github.com/advisories/GHSA-2m5v-pch6-74hx). |
| `source-map-js` | `1.2.1` | High | Indexed source-map processing denial of service; [GHSA-68fv-2mgg-jv7q](https://github.com/advisories/GHSA-68fv-2mgg-jv7q). |
| `@vitest/mocker` | `5.0.2` | Moderate | Unauthenticated mock registration and Windows redirect traversal; [GHSA-qxg6-89mg-rqv8](https://github.com/advisories/GHSA-qxg6-89mg-rqv8), [GHSA-xp26-3454-mjgp](https://github.com/advisories/GHSA-xp26-3454-mjgp). |
| `vitest` | `5.0.2` | Moderate | Affected through the mocker dependency. |

All five packages belong to development/build paths in [package.json](../../web/package.json); the production-only npm audit reported zero. These reports do not establish a vulnerability in the shipped browser runtime. They do affect developer/test/build trust, especially on Windows and with restored private data. npm reports fixes available; use advisory-approved patched versions, not an unreviewed forced major upgrade.

**Code change:** update the affected dependency tree and [lockfile](../../web/package-lock.json), then run the web suite, lint/build and full npm audit. Acceptance is a patched tree with the reported paths resolved or an explicit advisory-specific justification, not only a clean production-only audit. Release/image scanning is in the deployment document.

### A7. Enforceable browser response policy (medium hardening)

[Response middleware](../../server/src/MediaDock.Api/Middleware/ApiResponseHeadersMiddleware.cs) emits MIME-sniffing, frame, referrer and permissions protections and `no-store` for `/api` and the OIDC callback. Its CSP is hardcoded as **report-only**, allows any HTTPS image source and inline styles, and has no report endpoint. This is a defense-in-depth gap, not a demonstrated XSS exploit.

**Code change:** add a deliberate report-only/enforced policy mode and a policy compatible with actual app resources; reduce broad image/style allowances where practical. Retain the HTML `no-store` behavior introduced with A10 and keep API/auth errors and success responses `no-store`. Report collection is a separate optional feature, not a reason to leave policy permanently unenforced.

**Acceptance:** middleware tests cover both CSP modes, expected resource directives, HTML cache behavior and private API/error responses. Browser tests exercise login, account status, catalog posters, dialogs and uploads without policy breakage. Actual edge HTTPS/HSTS/header verification belongs to the deployment review.

### A15. Rate-limit route coverage and shared resource ceilings (medium)

[Rate limiting](../../server/src/MediaDock.Api/Middleware/ApiRateLimiting.cs) supplies auth, registration, search, upload and job-dispatch sliding windows, generic 429/`Retry-After`, and per-IP upload/dispatch concurrency caps. However `/signin-oidc`, health probes, title details/occurrences, several personal reads, catalog match application and some settings operations have no bucket. Per-IP limits alone do not provide a process-wide concurrency budget.

**Code change:** classify uncovered routes by actual cost and add the needed limits, particularly provider-spending/shared-mutation handlers and callback/database-backed health paths. Add process-wide upload/dispatch concurrency limits where required and use server-resolved user identity for per-user quotas where appropriate. Account for the current limiter running before authentication when choosing that implementation; do not trust a caller-supplied user ID. Preserve upload/body/time bounds, no-queue rejection and safe diagnostics.

**Acceptance:** focused integration tests exercise the newly covered handlers, global concurrency across multiple simulated clients, 429/`Retry-After`, recovery and absence of side effects on rejection. Retain tests for trusted versus spoofed forwarding headers. Traffic thresholds, multi-replica budgets and host/edge resource limits are deployment concerns, not a claim that the current per-process limiter is distributed.

### A16. Normal users can change and merge shared catalog records (conditional high)

[Catalog IMDb match endpoints](../../server/src/MediaDock.Api/Catalog/TorrentTitleMatchEndpoints.cs) inherit the active-user fallback policy rather than the administrator policy. [ApplyCatalogTitleMatchAsync](../../server/src/MediaDock.Api/Catalog/TorrentTitleMatchApiService.cs) changes shared IMDb identity/metadata and can move releases to another title and remove the original record. In contrast, direct catalog deletion, parse-log matching, and Golden Globes linking are explicitly admin-only. This difference is confirmed in source, not a demonstrated anonymous bypass.

**Code decision/change:** if normal users are meant only to browse and manage their own data, require administrator or a distinct curator permission for shared matching, and review user-triggered RSS refresh/provider spending as well. If trusted collaborative editing is intentional, document that permission, bound provider cost, and audit its actor/outcome. This is conditional on the intended product policy, not an already demonstrated role bypass.

**Acceptance:** integration tests prove the chosen user/admin permissions; denied calls must not change titles, move releases, update shared metadata or consume avoidable provider calls. Keep personal ownership independent of shared-data roles.

### A17. Session revocation and structured security events (medium hardening)

[Cookie configuration](../../server/src/MediaDock.Api/Authentication/GoogleSignInOptions.cs) uses a 12-hour, non-sliding, Secure/HttpOnly `__Host-` session. [Logout](../../server/src/MediaDock.Api/Authentication/AuthenticationEndpoints.cs) clears the browser cookie; no server-side ticket/session revocation store is configured in the reviewed registration. A previously copied valid ticket can therefore remain usable until expiry while its linked account remains active. Resolving account state/role on every protected request makes deactivation/demotion an effective immediate access denial, but it is not individual-session revocation. No theft/replay attack was executed.

**Code decision/change:** choose the required logout semantics. If logout must invalidate a copied cookie, add revocable tickets or a session/security-version check rather than only deleting the browser cookie. Review the 12-hour lifetime against the selected model; shorter expiry alone is not revocation. This is optional stronger session hardening if account-level deactivation plus expiry is explicitly accepted.

Add structured actor/action/target/outcome events where missing for account decisions/status changes, shared catalog edits, imports and application-side deploy requests. Derive actors from the validated session, never request fields. Persisted registration decisions are useful evidence but do not alone cover every privileged action. Log no cookies, provider tokens, API keys or uploaded private data.

**Acceptance:** if revocation is selected, capture a valid test cookie, sign out, replay it and verify denial while unrelated sessions behave according to the chosen scope. Test deactivation/demotion with existing cookies regardless. Event tests verify trustworthy actor identity, outcome, and secret redaction. Administrator MFA, log retention/alert delivery and incident recovery are addressed separately in the deployment document.

## Verified controls and limits of assurance

| Boundary | Current evidence |
| --- | --- |
| Authentication | Google authorization-code flow with PKCE, signed issuer/audience/lifetime validation, verified email and fixed callback-origin checks; provider tokens are not saved. Tests use synthetic principals, not a real Google browser journey. |
| Approval and roles | [Current-user resolution](../../server/src/MediaDock.Api/Authentication/AccessControl.cs) uses issuer/subject plus current database status/role. An arbitrary Google account is not an approved user. [Administration tests](../../server/tests/MediaDock.IntegrationTests/UserAdministrationApiTests.cs) cover anonymous/unknown/deactivated/user/admin denial and demotion. Linking/registration are transactional; new requests are pending, not automatically privileged. |
| Personal-data isolation | Favorites, ignored titles, ratings, recommendations and personal enrichment jobs resolve server-side owner identity. [Personal-ratings tests](../../server/tests/MediaDock.IntegrationTests/PersonalRatingsApiTests.cs) include cross-owner reads/deletes/job status; a caller-supplied `userId` does not select another owner. Admin rights do not imply owner impersonation. |
| CSRF | [Central middleware](../../server/src/MediaDock.Api/Middleware/ApiRequestProtectionMiddleware.cs) validates request token/cookie and origin for unsafe API methods carrying a session cookie; auth and upload paths always require tokens. It rejects `Sec-Fetch-Site: same-site` as well as cross-site. Missing Origin/Referer still requires a valid token. Framework `.DisableAntiforgery()` on uploads is not a bypass of this middleware. [The client](../../web/src/api/client.ts) attaches tokens to unsafe methods. Focused tests cover missing/invalid tokens and same-site cross-origin rejection; exhaustive real-cookie mutation coverage is still a launch gate. |
| Error/data exposure | [Unexpected 500s and oversized requests](../../server/src/MediaDock.Api/Middleware/ApiExceptionHandler.cs) use generic responses with trace IDs; sentinel exception text is excluded and retained in server logs by tests. Expected validation/conflict diagnostics remain visible and must stay non-secret. Provider-settings responses omit the actual OMDb key. |
| SSRF and XML | [RSS transport](../../server/src/MediaDock.Infrastructure/Rss/RssFeedTransport.cs) restricts HTTPS to `feed.rutracker.cc`, rejects credentials/private DNS answers, revalidates redirects, and bounds response bytes/entries/time. [The HTTP factory](../../server/src/MediaDock.Infrastructure/Rss/RssFeedHttpClientFactory.cs) pins connections through the public-address policy and disables automatic redirects; DTD/external XML resolution are disabled. This is not a general host egress firewall. |
| Provider calls and injection | [OMDb](../../server/src/MediaDock.Infrastructure/Metadata/OmdbClient.cs) uses a fixed HTTPS endpoint, encoded parameters, time/response bounds and request budgets in normal ingestion/search paths. Reviewed persistence paths use EF and parameterized interpolation; no confirmed SQL-injection issue was found in the targeted review. The admin provider diagnostic is a separate privileged request path. |
| XSS and uploads | React renders untrusted strings as text; targeted searches found no raw-HTML/eval sinks in application source. Feed links are HTTP(S)-restricted with safe new-tab attributes. Personal CSV uploads are capped at 1 MiB; admin import content defaults to 10 MiB with request limits. File names are sanitized and input is stored as data, not deployed/executed. These observations are not fuzzing or malicious-file guarantees. |
| UI state | The app gates routes until an active linked account is present and keys personal providers by account identity to reset state. No auth tokens were found in browser local/session storage; reviewed localStorage usage is a rating-filter preference. Existing UI tests do not establish network-level Production delivery; see A10. |
| Production configuration guards | [Startup](../../server/src/MediaDock.Api/Program.cs) rejects disabled Google sign-in or authorization in normal Production. The one-shot migration/bootstrap paths do not serve HTTP. Keep the existing guard tests; older deployed configuration is not an unresolved code finding here. |
| Development mock guards | [Mock registration](../../server/src/MediaDock.Api/Authentication/GoogleSignInOptions.cs) rejects non-Development use, non-loopback databases and simultaneous Google auth; the handler rejects non-loopback peers. [Vite](../../web/vite.config.ts) rejects non-loopback binds. These guards are implemented/tested; externally forwarding a loopback service is a separate deployment/topology concern. |

## Login-only access contract

Recommended design: keep Google sign-in and administrator approval; do not add a separate local password database just to create a login page. An unknown Google identity may request approval only if that policy is wanted; otherwise provision approved accounts privately and disable the request flow. Signing into Google alone must never expose the app.

| Caller | Browser experience | Server permissions |
| --- | --- | --- |
| Anonymous | Only MediaDock name, Google sign-in, and necessary error/privacy information; no menus, title previews, counts or app requests. | Public shell/assets; start-login/session/anti-forgery protocol. No application data/actions. |
| Google identity without approval, pending, conflicted or deactivated | Only relevant account status, permitted link/request controls, and sign-out. | Own account protocol only; no catalog/personal/admin API access. |
| Active approved user | Application and own personal data. | Shared reads and explicitly approved user capabilities; no other owner's data or administrator operations. Resolve A16 explicitly. |
| Active approved admin | Application plus administrator UI. | Explicit admin routes; own personal data, not automatic impersonation. |

The public application protocol is `/api/auth/google/login`, `/api/auth/session`, `/api/auth/antiforgery`, `/signin-oidc`, and required shell/assets. `/api/auth/google/link` and `/api/auth/registration-requests` bypass the active-account fallback only so validated Google identities can complete the account protocol; handlers still require valid identity and CSRF. `/api/auth/logout` requires CSRF; the stronger revocation decision is A17. These exceptions do not grant business-data access. `/api/version` stays protected; OpenAPI stays Development-only. Health probes are machine endpoints, not part of the public app; their network restriction is tracked separately.

For every protected endpoint, test anonymous denial, unapproved-identity denial, user/admin separation, and owner isolation. A request blocked by CSRF may return 400 before authorization; supply a valid token when testing the underlying expected 401/403. No denial may disclose application data or cause side effects. Cached responses and deep links must obey the same contract.

## Code Implementation Order

1. **A10 (source fix complete):** retain the Production-host regression and login redirects; verify delivery and API denials on the approved deployed release. Anonymous presentation still uses the existing welcome styling/copy.
2. **A12:** patch the development dependency tree/lockfile and verify web tests, lint/build and full dependency audit.
3. **A16:** settle shared-edit permission, implement the selected server policy and add role/side-effect regression tests.
4. **A15:** extend uncovered expensive-route limits and necessary process-wide ceilings with focused rejection/recovery tests.
5. **A7:** implement/test enforceable CSP and HTML/private-response cache behavior.
6. **A17:** choose copied-cookie logout semantics; implement revocation if required, and fill missing structured security events with redaction tests.

For each change, run the narrowest relevant repository-runner check first, then all affected backend integration/web suites. Keep CSRF, current-role resolution and owner isolation tests when changing middleware or authentication. Code completion means the selected behaviors are implemented and regression-tested; it does not depend on declaring an old deployment updated.

## Review Limits

No application fixes were made during the original audit/documentation work; the later A10 source fix and its local verification are recorded separately above. Existing tests do not establish an exhaustive real-cookie route matrix or hostile-file/load/XSS/CSRF/SSRF fuzzing result. Dependency evidence is a dated snapshot and must be refreshed when packages change. The remaining production and network acceptance work is intentionally excluded from this document and retained in the separate deployment review.