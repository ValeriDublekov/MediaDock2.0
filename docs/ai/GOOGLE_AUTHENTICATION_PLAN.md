# Plan: Google Authentication and Registration Requests

**Status:** Sessions 0 through 4 complete. Cloudflare Tunnel and Access are active, and the Google Web OAuth client credentials plus callback and trusted Tunnel CIDR are configured in the protected production `.env`. Sign-in remains disabled until the trusted-proxy code change is deployed and the consent-screen test-user setup is verified.

## Purpose and sequencing

This phase adds optional Google sign-in and a way to submit a registration request. It creates the initial user and external-identity model before personal data is split by user. It deliberately does **not** enforce authorization or restrict any existing application feature.

Complete this plan before the [users and access-control implementation plan](USERS_AND_ACCESS_CONTROL_IMPLEMENTATION_PLAN.md). That later plan must reuse the identity schema and Google sign-in implementation rather than create them again. The product-level data and permission model remains in the [users and access-control plan](USERS_AND_ACCESS_CONTROL_PLAN.md).

## Behavior and security boundary

- Sign-in is optional. Anonymous users continue to use the application exactly as they do today; no existing API or screen requires a Google session.
- A signed-in user can identify an existing linked account. When a verified Google identity matches no account and includes both name claims, successful sign-in automatically submits a registration request. A request does not grant or remove access to application features.
- User status and roles may be stored, but they have no authorization effect in this phase. Do not add role-based endpoint policies, owner scoping, or UI restrictions.
- Favorites and personal ratings remain in the current shared-profile model. Do not assign existing rows to a new user or imply that they have become private.
- Show a persistent warning that sign-in is optional and does not protect application data. Keep the existing loopback default and the specific-interface plus firewall allowlist requirements for LAN operation. Do not expose the API to the public internet.
- Do not add unauthenticated user-approval, role-change, or user-administration endpoints. Until authorization is enforced, an operator can review/approve requests using a controlled host-side procedure; an authenticated admin UI belongs to the later access-control plan.

Authentication proves an identity; it does not establish permission. This phase must not be presented as making the currently open application safe for multiple users.

## Recommended authentication design

- Use Google OpenID Connect Authorization Code flow with a server-managed session, using the supported ASP.NET Core authentication middleware instead of hand-rolling protocol validation.
- Keep the web client and API same-origin. Store the application session in a `Secure`, `HttpOnly` cookie with an appropriate `SameSite` policy. Do not store provider tokens in browser storage or return them to the React client.
- Validate the OIDC signature, issuer, audience, expiry, state, and nonce. Require Google's verified-email claim before using an email for account matching or a registration request. Request `openid`, `email`, and `profile`; registration needs Google's separate given-name and family-name claims.
- Persist the provider identity as `(issuer, subject)` and use it as the durable login key. Email is a normalized account attribute, not a durable identity. Never accept an email, provider subject, user ID, or role supplied by the browser as proof of identity.
- On a first login matching a pre-provisioned account by verified email, require an explicit, one-time confirmation before persisting the validated `(issuer, subject)` association. The server derives the matched account and identity from validated claims; never accept either from the browser. Reject ambiguous matches and already-linked identities; do not silently link based only on email.
- Protect OIDC callback state and registration-request submission against CSRF and replay. Do not log authorization codes, tokens, or session cookies.

## LAN callback and deployment requirements

The user's browser returns from Google to the application's callback URL; Google does not need an inbound network route to the MediaDock server. The server does need outbound HTTPS access to Google's identity endpoints for the code exchange and key discovery, and each user needs internet access to complete sign-in.

For local development, use the registered localhost callback. For LAN use, configure a stable hostname with HTTPS and a certificate trusted by client devices, then register the exact callback URI and required domain in Google Cloud. The hostname may resolve to a private LAN address; the app and API remain inaccessible from outside the LAN. A raw private IP over HTTP is not the production callback/session configuration.

## Data model and request flow

Add an additive migration with these concepts:

- `users`: internal stable ID, normalized unique email, `given_name`, `family_name`, pending/active/deactivated state, role, and timestamps. Store the Google profile's given-name and family-name values separately; do not parse them from the display-name claim or email. New registration creates a pending `users` row with both names. Assign a role only when the request is approved. Roles are not enforced in this phase.
- `external_identities`: user foreign key, issuer, subject, and creation timestamp, with a unique constraint on `(issuer, subject)`.
- `registration_requests`: user foreign key, request time, and pending/approved/rejected state, plus decision time and the host operator identifier for an approved or rejected request. Keep the verified email and provider identity in the related `users` and `external_identities` rows instead of duplicating them. Prevent duplicate pending requests for the same user. Do not store access or ID tokens. Approval atomically activates the user, assigns the regular-user role, and records the decision; rejection leaves the user pending and records the decision. Neither action is exposed through an unauthenticated endpoint.

Registration requires both Google name claims. If either is absent, explain that the profile information is incomplete and do not create a pending user or request. Capture the names at registration and do not silently overwrite them during later sign-ins.

The login flow should behave as follows:

1. The user can continue anonymously or choose **Sign in with Google**.
2. After successful OIDC validation, an already-linked identity is associated with its internal user for display/session purposes. This association does not alter API authorization.
3. If a verified email matches a pre-provisioned account without a linked identity, offer the controlled one-time linking flow. If it matches no account and both Google name claims are present, automatically atomically create a pending `users` row with the Google `given_name` and `family_name`, link the validated provider identity, and create a `registration_requests` row for that user. If either name claim is missing, explain that the profile is incomplete and do not create the request.
4. Show the request state to the requester where possible. Approval in this phase is an operator-controlled operation, not a public API or an unprotected admin screen. Approval activates the same user row and assigns its role; it still does not change application access until a later authorization phase.

Seed or provision the first administrator through an explicit operator-controlled bootstrap, populate the name fields from the verified Google profile, and link the verified Google identity. Do not infer administrator status from email address, domain, or first-login order.

## Implementation sessions

### Session 0: Confirm decisions and callback setup

Record and use these decisions:

- Use Google's OIDC Authorization Code flow with ASP.NET Core OpenID Connect and cookie-authentication middleware, same-origin with the API. Accept any Google account only when Google asserts a verified email; do not add a domain allowlist. An unmatched identity may request registration, but remains pending and receives no application permissions until an operator approves it.
- A verified-email match to a pre-provisioned user is never linked silently. Show the matched account and require an explicit, one-time confirmation from the already-authenticated Google session. Derive the account, email, issuer, and subject on the server. Reject ambiguous email matches, identities already linked elsewhere, and repeat link attempts.
- Use a 12-hour absolute authentication lifetime with sliding renewal disabled. Use an `HttpOnly`, `Secure`, `SameSite=Lax`, host-only cookie scoped to `/`; do not persist it across browser sessions or store Google tokens in the browser. Use ASP.NET Core's protected cookie ticket, clear it on sign-out, and apply antiforgery protection to state-changing requests authenticated by the cookie. OIDC state, nonce, and correlation handling remain middleware-managed.
- Bootstrap the first administrator only through an explicit host/operator-controlled procedure using a successfully validated Google identity. Require both Google name claims, assign the administrator role explicitly, and make bootstrap one-time. Do not infer administrator status from email, domain, or login order. Registration requests are reviewed through the protected Users workflow by an active administrator; approve/reject transactionally, retain decision time and administrator identity, and never add unauthenticated approval or role-management endpoints.
- Configure Google's consent screen for external users and publish it before accepting arbitrary Google accounts; Google's Testing mode is limited to its configured test users. For the local Compose app, register the exact redirect URI `http://localhost:8080/signin-oidc` on a Google OAuth client of type **Web application**. Use `localhost` consistently in the browser; do not substitute `127.0.0.1`. Keep the UI and callback same-origin. The client ID and secret are host configuration only (for example, ignored `.env` or the host environment), never tracked files or browser configuration.
- Sign-in is implemented but disabled by default. `media.valyo.eu` is the selected public HTTPS hostname via Cloudflare Tunnel. Configure the Google Web application client with Authorized JavaScript origin `https://media.valyo.eu`, Authorized redirect URI `https://media.valyo.eu/signin-oidc` (no trailing slash), and set `GOOGLE_AUTH_CALLBACK_URI` to that exact redirect. Keep sign-in disabled until Tunnel routing, HTTPS, and a Cloudflare Access policy are active and the callback works end to end. Cloudflare Access does not provide application-level API authorization; verify that no alternate untrusted route reaches the origin. Do not add a public listener or weaken the loopback default, specific-interface LAN bind, or firewall allowlist.

The localhost callback and policy are implementation decisions; creating the Google Cloud client, setting its secret outside Git, and choosing/configuring the future LAN hostname and certificate are operator actions. Do not claim LAN callback setup is complete until those actions are verified. Record configuration metadata, but never secrets, in the operator's protected setup notes.

**Exit checks:** local and LAN callback forms, cookie/session policy, account linking, bootstrap, and request review are documented. No unresolved product decision changes the identity schema or login flow. Google credentials remain outside tracked files; LAN sign-in stays gated until its HTTPS callback is configured. No public inbound route is introduced.

### Session 1: Add identity persistence

Add the `users`, `external_identities`, and `registration_requests` entities, EF configuration, constraints, and an additive migration. Include separate `given_name` and `family_name` fields on `users`, and link each registration request to its pending user. Keep favorites, ratings, and all existing API behavior unchanged. Provide an operator-controlled bootstrap for the initial administrator without creating an unauthenticated role-management endpoint.

**Exit checks:** migration tests cover a fresh and upgraded database, normalized-email and provider-identity uniqueness, duplicate requests, required user names, and bootstrap conflicts. Existing shared favorite/rating rows remain unchanged.

**Handoff:** Added the three identity tables and constraints in additive migration `20261006091421_AddIdentityPersistence`. The one-shot `bootstrap-admin` Compose tool is host-only and accepts operator-supplied profile values; it does not validate Google claims and must only be run after Session 2 can supply a validated identity. No sign-in, approval endpoint, authorization policy, or user scoping was added. Focused PostgreSQL tests pass for fresh/upgrade migration, preserved shared personal rows, constraints, and concurrent/repeated bootstrap conflicts.

### Session 2: Add optional Google sign-in

Implement OIDC login/callback/logout, validated claims, server-managed session, and a minimal current-session response for the UI. Keep all existing APIs usable anonymously; do not add authorization middleware or change endpoint behavior based on the session.

**Exit checks:** tests cover valid and invalid OIDC responses, state/nonce failures, issuer/audience/signature/expiry validation, verified-email and profile-name requirements, session creation and logout, and account-link conflicts. Tests use a local/test identity handler, not live Google credentials.

**Handoff:** Added optional ASP.NET Core Google OIDC Authorization Code flow and protected cookie session. Host configuration is `GOOGLE_AUTH_ENABLED` (default false), `GOOGLE_AUTH_CLIENT_ID`, `GOOGLE_AUTH_CLIENT_SECRET`, and `GOOGLE_AUTH_CALLBACK_URI` (local default `http://localhost:8080/signin-oidc`). The callback origin is checked against that exact configured URI; non-local callbacks require HTTPS on a hostname. Sessions expire after 12 hours without sliding renewal, provider tokens are not saved, and link/logout state changes use antiforgery. `GET /api/auth/session` exposes the validated provider issuer/subject/profile for the operator bootstrap and linked-account state; `POST /api/auth/google/link` only links the server-derived verified-email match after explicit confirmation. All existing APIs remain anonymous and no authorization policies, registration-request endpoint, or login UI were added. Focused API tests pass against a local signed-token OIDC authority and disposable PostgreSQL; no live Google credentials are used.

### Session 3: Add registration requests and login UI

Add the optional login button and signed-in identity/request status. Let a successfully authenticated, otherwise-unmatched identity submit an idempotent registration request. Keep review and approval operator-controlled until a protected admin workflow is implemented in the later plan. Show the persistent warning that authentication is optional and no application permissions are enforced.

**Exit checks:** web tests cover anonymous use, sign-in, linked account, registration request, duplicate/pending request, missing Google name claims, and sign-out. API tests prove registration cannot forge another email, provider identity, or user names. Existing anonymous route behavior remains unchanged.

**Handoff:** Added the antiforgery-protected `POST /api/auth/registration-requests` endpoint. It derives email, names, issuer, and subject only from the validated Google session; atomically creates a pending user, external identity, and request; and returns the existing request on same-identity retries. The session response includes sign-in availability and the latest request status. The web shell now offers optional Google sign-in, explicit account-link confirmation, registration submission/status, and sign-out on every route, with a persistent warning that identity is not authorization. Focused API and web tests pass; no approval endpoint or application authorization was added.

### Session 4: Documentation and handoff

Update API contracts, architecture, security and operations, and local setup documentation with callback configuration, secret handling, operator bootstrap/review, and the distinction between authentication and authorization. Update the later implementation plan to reuse this foundation.

**Exit checks:** documented callback matches configuration; no secrets are committed; web and server focused tests/builds pass; the open-mode warning and network boundary are accurate.

**Handoff:** Updated the API contracts, architecture, security and operations guide, root local runbook, and the later access-control implementation plan. At that handoff, review had no endpoint, admin UI, or CLI. The incremental protected workflow is now documented in the API contracts and access-control plan. The docs specify the exact localhost callback and host-only credential handling, the operator-supplied bootstrap procedure, transactional request review, and that authentication does not authorize or isolate application data. Focused checks at the original handoff passed: Google authentication/OIDC integration tests (14/14), web authentication-status tests (6/6), server solution build, and web lint/build. The builds reported dependency/lint warnings; no credentials were added. Relative Markdown links and `git diff --check` passed. Google Cloud consent/client setup and LAN HTTPS callback remain operator gates.

## Handoff to access-control implementation

Before starting the later plan, verify that the `users` and `external_identities` schema, optional OIDC session, bootstrap identity, and registration-request workflow are present. The later plan may add per-user ownership and authorization, but must not recreate the identity tables or Google login flow. In particular, the later data migration must explicitly select the owner of legacy favorites and ratings; this plan does not make that choice or backfill those rows.

## Remaining Operator Setup

The design decisions above are closed. Production has the Google Web application client credentials, exact callback, and `FORWARDED_HEADERS_TRUSTED_NETWORK` value in the protected `.env`; `GOOGLE_AUTH_ENABLED` remains false. Before enabling sign-in, deploy the trusted-proxy code change, confirm this dedicated bridge remains limited to the API and Tunnel connector, and verify that the Google consent screen includes the intended test user (or is published before accepting arbitrary accounts). Then enable sign-in and verify the full callback. The end-to-end callback is not yet verified. Keep the persistent warning: authentication does not authorize or protect application data. Missing Google given-name or family-name claims are handled as specified above: do not create a user or request, and explain that profile information is incomplete.