# MediaDock 2.0 AI Documentation

Start here for work in this repository. Use the [local operations runbook](../../README.md) for setup, Compose, and operational commands.

## Project orientation

[Project context](PROJECT_CONTEXT.md) records local-versus-production boundaries and deployment cautions. The [local operations runbook](../../README.md) covers setup and commands.

## Choose by task

| Task | Read |
| --- | --- |
| Frontend | [Architecture](ARCHITECTURE.md) for the web/API boundary and request flow; [Testing](TESTING.md) for frontend tests and commands. |
| Frontend design review | [Remaining UI design improvements](plans/ui-design-improvements-remaining.md) for open and unverified catalog, personal, award, and administration findings. |
| API | [Shared API conventions](API_CONTRACTS.md); [catalogs and awards](API_CATALOGS_AND_AWARDS.md), [identity and personal data](API_IDENTITY_AND_PERSONAL_DATA.md), or [operations and jobs](API_OPERATIONS_AND_JOBS.md) for route-specific contracts; [Architecture](ARCHITECTURE.md) for cross-layer flows. |
| Golden Globes IMDb imports | [Import procedure](GOLDEN_GLOBES_IMPORT.md) for page capture, category scope, audit output, and the reusable SQL template. |
| Persistence | [Data contracts](DATA_CONTRACTS.md) for canonical ownership and migration cautions; [Data schema](DATA_SCHEMA.md) for entities and constraints; [Data behavior](DATA_BEHAVIOR.md) for transactions and idempotency; [Architecture](ARCHITECTURE.md) for cross-layer flows. |
| Background ingestion | [Architecture](ARCHITECTURE.md) for the API-hosted queue, dispatcher, and scheduler; [operations and jobs API](API_OPERATIONS_AND_JOBS.md) for job routes. Verify production rollout on the host; repository source and documentation do not prove what is deployed. |
| Personal IMDb ratings | [Identity and personal data API](API_IDENTITY_AND_PERSONAL_DATA.md) for routes and DTOs; [Data contracts](DATA_CONTRACTS.md) for ownership and migration cautions; [Data schema](DATA_SCHEMA.md) for keys and foreign keys; [Data behavior](DATA_BEHAVIOR.md) for import and enrichment persistence. |
| Movie and series recommendations | [Engine reference](MOVIE_RECOMMENDATION_ENGINE.md) for the implemented formulas and settings; [personal recommendation plan](plans/movie-recommendations.md) for the original movie architecture decisions and phase acceptance criteria. |
| Tests | [Testing](TESTING.md) for scoped commands, test categories, required dependencies, and runner artifacts; [development tools](DEVELOPMENT_TOOLS.md) for diff and migration inspection, file inventories, complexity analysis, and Markdown link validation. |
| Engineering follow-ups | [Technical follow-ups](TECHNICAL_FOLLOW_UPS.md) for pending database-model and source-refactoring decisions, with links to the dated assessments. |
| Operations | [Server access](SERVER_ACCESS.md) for the local SSH profile and session workflow; [Security](SECURITY.md) for trust boundaries, authorization, and secrets; [internet exposure audit (reassessed 2026-10-30)](SECURITY_AUDIT_2026-10-26.md) for findings and launch gates; [Operations](OPERATIONS.md) for migration safeguards; the [local runbook](../../README.md) for setup and maintenance. |

## Current Status

Use the [local runbook](../../README.md) and [operations guide](OPERATIONS.md) for operator procedures and deployment gates; see the [security guide](SECURITY.md) for runtime boundaries. Verify production state on the host; repository source and documentation do not prove what is deployed.
