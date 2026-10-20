# MediaDock 2.0 AI Documentation

Start here for work in this repository. Use the [local operations runbook](../../README.md) for setup, Compose, and operational commands.

## Project orientation

[Project context](PROJECT_CONTEXT.md) records local-versus-production boundaries and deployment cautions. The [local operations runbook](../../README.md) covers setup and commands.

## Choose by task

| Task | Read |
| --- | --- |
| Frontend | [Architecture](ARCHITECTURE.md) for the web/API boundary and request flow; [Testing](TESTING.md) for frontend tests and commands. |
| API | [Shared API conventions](API_CONTRACTS.md); [catalogs and awards](API_CATALOGS_AND_AWARDS.md), [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md), or [operations and jobs](API_OPERATIONS_AND_JOBS.md) for route-specific contracts; [Architecture](ARCHITECTURE.md) for cross-layer flows. |
| Persistence | [Data contracts](DATA_CONTRACTS.md) for canonical ownership and migration cautions; [Data schema](DATA_SCHEMA.md) for entities and constraints; [Data behavior](DATA_BEHAVIOR.md) for transactions and idempotency; [Architecture](ARCHITECTURE.md) for cross-layer flows. |
| Background ingestion | [Architecture](ARCHITECTURE.md) for the API-hosted queue, dispatcher, and scheduler; [operations and jobs API](API_OPERATIONS_AND_JOBS.md) for job routes. Verify production rollout on the host; repository source and documentation do not prove what is deployed. |
| Personal IMDb ratings | [Identity and personal data API](API_IDENTITY_AND_PERSONAL_DATA.md) for routes and DTOs; [Data contracts](DATA_CONTRACTS.md) for ownership and migration cautions; [Data schema](DATA_SCHEMA.md) for keys and foreign keys; [Data behavior](DATA_BEHAVIOR.md) for import and enrichment persistence. |
| Tests | [Testing](TESTING.md) for scoped commands, test categories, required dependencies, runner artifacts, file line inventory, and Markdown link validation. |
| AI workflow | [Workflow efficiency plan](WORKFLOW_EFFICIENCY_PLAN.md) for linked multi-phase plans and focused verification; [documentation cleanup plan](DOCUMENTATION_CLEANUP_PLAN.md) for reducing duplicated reference content. |
| Engineering follow-ups | [Technical follow-ups](TECHNICAL_FOLLOW_UPS.md) for pending database-model and source-refactoring decisions, with links to the dated assessments. |
| Operations | [Server access](SERVER_ACCESS.md) for the local SSH profile and session workflow; [Security](SECURITY.md) for trust boundaries, authorization, and secrets; [Operations](OPERATIONS.md) for migration safeguards; the [local runbook](../../README.md) for setup and maintenance. |

## Current Status

Use the [local runbook](../../README.md) and [operations guide](OPERATIONS.md) for operator procedures and deployment gates; see the [security guide](SECURITY.md) for runtime boundaries. Verify production state on the host; repository source and documentation do not prove what is deployed.
