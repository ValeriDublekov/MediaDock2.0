# Internet Exposure Phase 3: Runtime and Host

## Goal

Keep the public path HTTPS-only, close origin bypasses, add browser/cache protections, and prevent development or host tooling from widening access.

## Remaining Checks (2026-10-30)

- Record the exact deployed SHA and inspect effective Compose bindings, Docker networks, Tunnel and Access policies, DNS/hostnames, firewall, and router forwarding. Repository configuration is not live-state evidence.
- From an external network, verify direct-origin and alternate-host denial. From an untrusted LAN source, verify the API port is denied. Confirm only approved routes pass Cloudflare Access and the Google OIDC callback works.
- In a browser, verify HTTPS, HSTS, report-only CSP behavior, security headers, cookie flags, private-response `no-store`, and absence of public version/data endpoints. Do not enforce CSP until compatibility is checked.
- Verify Data Protection key persistence, encryption, permissions, and behavior after restart/recreation; perform a controlled backup restore without printing secrets or production data.
- Review host secret and backup protection, database privileges, deploy-socket access, image provenance/scanning, and patch cadence.
- During restored-data development, verify ports 5173 and 5280 are unreachable from untrusted interfaces and Production rejects local mock authentication.
- Use read-only host inspection first. Obtain separate operator approval before changing production networks, firewall rules, secrets, images, or services.

## Exit Criteria

- No unapproved direct-origin, router, hostname, or LAN bypass exists; PostgreSQL remains private.
- HTTPS, proxy trust, headers, and private cache behavior match the approved design and pass browser/OIDC checks.
- Secrets, key material, images, host services, backups, and deploy controls have documented ownership and protection.
- The mock-auth development workflow is restricted to loopback and cannot be mistaken for an internet-facing login mode.