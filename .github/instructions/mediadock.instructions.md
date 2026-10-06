---
applyTo: "**"
---

# MediaDock 2.0

Respond only in English or Bulgarian.
Reuse existing SSH sessions whenever practical to reduce repeated password prompts; follow [Server access](../../docs/ai/SERVER_ACCESS.md).
Before changing project files, read the [AI documentation index](../../docs/ai/README.md) and follow its task-specific references. Treat source and tests under `server/` and `web/` as authoritative. The root [README](../../README.md) is the local operations entrypoint. Keep the API loopback-bound by default; LAN deployment is unauthenticated and must retain its specific-interface bind and host firewall allowlist.

For every test run in this repository, use `python -B scripts/run_tests.py <suite>` rather than invoking `dotnet test`, Vitest, or `unittest` directly. Choose the narrowest relevant suite and `--filter`; use `all` when full-suite verification is needed. When integration tests are relevant, check Docker availability and run `server-integration`; do not skip them just because they require Docker. If Docker is unavailable, report that blocker. When changing the runner, run the `runner` suite. See the [testing guide](../../docs/ai/TESTING.md) for suite names and examples.