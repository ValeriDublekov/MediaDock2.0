# MediaDock 2.0 AI Documentation

Start here for work in this repository. Use the [local operations runbook](../../README.md) for setup, Compose, and operational commands.

## Project orientation

[Project context](PROJECT_CONTEXT.md) summarizes the runtime boundary, stack, main directories, and local entrypoint.

## Choose by task

| Task | Read |
| --- | --- |
| Frontend | [Architecture](ARCHITECTURE.md) for the web/API boundary and request flow; [Testing](TESTING.md) for frontend tests and commands. |
| API | [Shared API conventions](API_CONTRACTS.md); [catalogs and awards](API_CATALOGS_AND_AWARDS.md), [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md), or [operations and jobs](API_OPERATIONS_AND_JOBS.md) for route-specific contracts; [Architecture](ARCHITECTURE.md) for cross-layer flows. |
| Persistence | [Data contracts](DATA_CONTRACTS.md) for canonical ownership and migration cautions; [Data schema](DATA_SCHEMA.md) for entities and constraints; [Data behavior](DATA_BEHAVIOR.md) for transactions and idempotency; [Architecture](ARCHITECTURE.md) for cross-layer flows. |
| Background ingestion | [Architecture](ARCHITECTURE.md) for the API-hosted queue, dispatcher, and scheduler; [operations and jobs API](API_OPERATIONS_AND_JOBS.md) for job routes; [Implementation status](IMPLEMENTATION_STATUS.md) for source and production rollout status. |
| Personal IMDb ratings | [Identity and personal data API](API_IDENTITY_AND_PERSONAL_DATA.md) for routes and DTOs; [Data contracts](DATA_CONTRACTS.md) for ownership and migration cautions; [Data schema](DATA_SCHEMA.md) for keys and foreign keys; [Data behavior](DATA_BEHAVIOR.md) for import and enrichment persistence. |
| Tests | [Testing](TESTING.md) for scoped commands, test categories, required dependencies, runner artifacts, and file line inventory. |
| AI workflow and documentation structure | [Workflow efficiency plan](WORKFLOW_EFFICIENCY_PLAN.md) for linked multi-phase plans and focused verification; [documentation optimization review](DOCUMENTATION_OPTIMIZATION_REVIEW.md) for the file-by-file assessment and staged restructuring steps. |
| Operations | [Server access](SERVER_ACCESS.md) for the local SSH profile and session workflow; [Security and operations](SECURITY_AND_OPERATIONS.md) for runtime boundaries; the [local runbook](../../README.md) for setup and maintenance. |

## Current Status

[Implementation status](IMPLEMENTATION_STATUS.md) summarizes shipped behavior and known limitations. Use the [local runbook](../../README.md) and [security and operations guide](SECURITY_AND_OPERATIONS.md) for operator procedures and deployment gates. Verify production state on the host; repository source and documentation do not prove what is deployed.
