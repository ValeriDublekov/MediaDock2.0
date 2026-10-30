# MediaDock 2.0 Web

The React and TypeScript client for this standalone application. Start with the [AI documentation index](../docs/ai/README.md); see [architecture](../docs/ai/ARCHITECTURE.md), [testing](../docs/ai/TESTING.md), and the [local runbook](../README.md) for the full-stack setup.

## Local Development

The web client requires Node.js and npm. API-backed development also requires the .NET 10 SDK and local PostgreSQL; use the database and migration setup in the [local runbook](../README.md). From `web`:

```powershell
npm ci
npm run dev -- --host 127.0.0.1
```

Vite serves the development UI at `http://127.0.0.1:5173/` by default. The client sends requests to the same-origin `/api` path; [Vite configuration](vite.config.ts) proxies `/api` to `http://localhost:5280`. This target is configured in Vite, not through a `VITE_API_BASE_URL` setting.

After PostgreSQL is running and migrations are applied, start the API from the repository root with its HTTP development profile:

```powershell
dotnet run --project ../server/src/MediaDock.Api/MediaDock.Api.csproj --launch-profile http
```

For local testing with the restored production snapshot, run this from the repository root instead:

```powershell
.\scripts\run_local_restored.ps1
```

It reads local Compose credentials without printing them, requires exactly one active user with one linked Google identity, and starts the API with local mock authentication and authorization enforcement on `http://localhost:5280`. Google OAuth and the background-job dispatcher are disabled. Stop the API with **Ctrl+C** in that same terminal; this leaves Vite and PostgreSQL running and retains the restored database. The script resets its process-level environment settings on exit. Never set `Authentication__LocalMock__Enabled` in production configuration; see the [security guide](../docs/ai/SECURITY.md#local-development-mock-authentication).

That profile listens on `http://localhost:5280` and uses the local development connection string. The Compose runbook serves its bundled UI at `http://127.0.0.1:8080/`; use the Vite server above when working with the frontend dev server and hot reload. The UI does not require the Worker, live RSS feeds, or an OMDb key.

## Frontend Checks

Run these from `web`; the scripts are defined in [package.json](package.json):

```powershell
npm run lint
npm run test
npm run build
npm run preview
```

`npm run test` runs Vitest in jsdom and uses mocked API responses. Run `npm run build` before `npm run preview`. See the [testing guide](../docs/ai/TESTING.md) for .NET unit and PostgreSQL integration test commands and dependencies.
