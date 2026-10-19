# MediaDock 2.0 AI Documentation

Start here for work in this repository. Use the [local operations runbook](../../README.md) for setup, Compose, and operational commands.

## Project orientation

[Project context](PROJECT_CONTEXT.md) summarizes the runtime boundary, stack, main directories, and local entrypoint.

## Choose by task

| Task | Read |
| --- | --- |
| Frontend | [Architecture](ARCHITECTURE.md) for the web/API boundary and request flow; [Testing](TESTING.md) for frontend tests and commands. |
| API | [API contracts](API_CONTRACTS.md) for routes, DTOs, validation, and errors; [Architecture](ARCHITECTURE.md) for cross-layer flows. |
| Persistence | [Data contracts](DATA_CONTRACTS.md) for entities, schema, and invariants; [Architecture](ARCHITECTURE.md) for persistence flows. |
| Background ingestion | [Architecture](ARCHITECTURE.md) for the API-hosted queue, dispatcher, and scheduler; [API contracts](API_CONTRACTS.md) for job routes; [Implementation status](IMPLEMENTATION_STATUS.md) for source and production rollout status. |
| Personal IMDb ratings work | [Implementation plan](PERSONAL_IMDB_RATINGS_PLAN.md) for the cross-session schema, enrichment, API, and UI rollout. |
| Tests | [Testing](TESTING.md) for scoped commands, test categories, and required dependencies; [test diagnostics plan](TEST_DIAGNOSTICS_PLAN.md) for workspace-local artifacts, concise failures, and file line inventory. |
| Operations | [Server access](SERVER_ACCESS.md) for the local SSH profile and session workflow; [Security and operations](SECURITY_AND_OPERATIONS.md) for runtime boundaries; the [local runbook](../../README.md) for setup and maintenance. |

## Current Status

[Implementation status](IMPLEMENTATION_STATUS.md) summarizes shipped behavior and known limitations. Use the [local runbook](../../README.md) and [security and operations guide](SECURITY_AND_OPERATIONS.md) for operator procedures and deployment gates. Verify production state on the host; repository source and documentation do not prove what is deployed.
