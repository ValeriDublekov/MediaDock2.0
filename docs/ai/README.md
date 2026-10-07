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
| Tests | [Testing](TESTING.md) for scoped commands, test categories, and required dependencies. |
| Operations | [Server access](SERVER_ACCESS.md) for the local SSH profile and session workflow; [Security and operations](SECURITY_AND_OPERATIONS.md) for runtime boundaries; the [local runbook](../../README.md) for setup and maintenance. |

## Open Plans And Notes

- [UI design and visual acceptance](UI_DESIGN_PLAN.md)
- [Golden Globes enrichment and manual IMDb linking](GOLDEN_GLOBES_ENRICHMENT_PLAN.md)
- [Personal IMDb ratings follow-up](IMDB_PERSONAL_RATINGS_PLAN.md)
- [Torrent title recognition and feed acceptance](TORRENT_TITLE_RECOGNITION_PLAN.md)
- [Google authentication and registration requests](GOOGLE_AUTHENTICATION_PLAN.md)
- [API-hosted background ingestion production cutover](BACKGROUND_INGESTION_PLAN.md)
- [Users and access control design](USERS_AND_ACCESS_CONTROL_PLAN.md)
- [Users and access control implementation](USERS_AND_ACCESS_CONTROL_IMPLEMENTATION_PLAN.md)
