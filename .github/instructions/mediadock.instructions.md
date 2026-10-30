---
description: "Essential MediaDock repository rules; task-specific details live in docs/ai."
applyTo: "**"
---

# MediaDock 2.0

Respond only in English or Bulgarian.

- Before edits, inspect `git status --short` and preserve user changes. Read the [AI index](../../docs/ai/README.md), then only the references needed for the task. Source and tests in `server/` and `web/` are authoritative.
- Follow the [task workflow](../../docs/ai/WORKFLOW.md) for implementation, cross-layer changes, migrations, and multi-session plans; do not load unrelated plans or historical documents.
- Use `python -B scripts/run_tests.py <suite>` for every test run, not direct test commands. Choose the narrowest suite/filter; changes to helpers require `runner`. Relevant integration tests require Docker; check availability and report blockers rather than silently skipping. See [Testing](../../docs/ai/TESTING.md).
- Store selectable logs/reports in ignored `.test-tmp/`. Use `python -B scripts/inspect_diff.py` for bounded diff output and `python -B scripts/check_markdown_links.py` after Markdown edits. See [Development tools](../../docs/ai/DEVELOPMENT_TOOLS.md).
- Make the smallest coherent edit and immediately run its focused check. Keep required migration preflights and repository gates; read only failure excerpts, fix locally, and rerun the same check. Stop once relevant checks pass; report unverified requirements.
- Keep the API loopback-bound by default. LAN deployments must retain the specific-interface bind and firewall allowlist. Production state must be verified, not inferred from source; never deploy or migrate production as part of routine coding. Follow [Security](../../docs/ai/SECURITY.md) and [Operations](../../docs/ai/OPERATIONS.md) when relevant.
- Reuse existing SSH sessions when practical; load [Server access](../../docs/ai/SERVER_ACCESS.md) only for host work. Never print secrets. Do not stage, commit, push, or bypass approvals without explicit authorization.