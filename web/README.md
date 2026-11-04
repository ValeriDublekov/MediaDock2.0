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

### Restored-data Development instance

Use this workflow for the API-backed UI against the local production snapshot. It uses the single active Google-linked database user as the local mock identity, so no browser sign-in is needed. It is separate from the standard HTTP launch profile above, which uses the `mediadock` development database, and from the Production Compose API on port 8080.

From the repository root, run one command to start the restored-data UI and API and install the daily user-level start task:

```powershell
.\scripts\start_local_restored_services.ps1 -InstallDailyTask
```

The script starts the local Compose PostgreSQL service if needed and reuses its existing volume; it never creates, drops, or restores a database. The volume must already contain `mediadock_prod_restore_20261029` (or specify another restored database with `-Database <database-name>`). It reads local Compose credentials without printing them, checks loopback-only publication, and requires exactly one active user with one linked Google identity. Missing restore data or a mismatched user/identity count stops startup without changing database rows.

The API uses local mock authentication with authorization enforcement still enabled, while Google OAuth and background jobs remain disabled. Healthy MediaDock API and Vite processes on ports 5280 and 5173 are reused; only missing services are started. The scheduled task runs at this user's logon and once daily at 08:00 local time, and skips duplicate instances. Docker Desktop must be running; the task waits up to two minutes for it.

Open `http://127.0.0.1:5173/`. Vite proxies `/api` to the restored-data API on port 5280. Verify API readiness at `http://localhost:5280/health/ready`.

Stop only the API and Vite processes managed by this script with:

```powershell
.\scripts\start_local_restored_services.ps1 -Stop
```

Add `-RemoveDailyTask` to remove the scheduled start; use both switches to stop the services and remove the task. PostgreSQL and the restored database are retained. The script's process IDs and logs are stored in the gitignored `.test-tmp/restored-local` directory; logs may contain restored-data details and should not be shared without review.

Do not use the Compose UI on port 8080 or the standard HTTP launch profile when you intend to use this restored database. The restored snapshot contains production data and settings, including the plaintext OMDb key. Keep it local, never tunnel or port-forward ports 5173/5280, and do not enable background jobs until restored secrets have been replaced. Never set `Authentication__LocalMock__Enabled` in `.env`, Compose, or production configuration; the script sets it only for its child process. See the [security guide](../docs/ai/SECURITY.md#local-development-mock-authentication).

The UI does not require the Worker, live RSS feeds, or an OMDb key.

## Golden Globes Design Explorations

The static design concepts are separate from the React app and can be reopened through the Vite development server. From the repository root, start the server with:

```powershell
cd web
npm run dev -- --host 127.0.0.1
```

Open `http://127.0.0.1:5173/design-explorations/golden-globes-variants.html?v=1` and change `v=1` to any number from `1` to `13` to compare the designs. Variants 11-13 are **Grand Noir**, **Silver Screen**, and **Noir Journal**. The original, unvaried static page is at `http://127.0.0.1:5173/design-explorations/golden-globes.html`.

## Frontend Checks

Run these from `web`; the scripts are defined in [package.json](package.json):

```powershell
npm run lint
npm run test
npm run build
npm run preview
```

`npm run test` runs Vitest in jsdom and uses mocked API responses. Run `npm run build` before `npm run preview`. See the [testing guide](../docs/ai/TESTING.md) for .NET unit and PostgreSQL integration test commands and dependencies.
