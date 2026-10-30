param(
    [string]$Database = "mediadock_prod_restore_20261029"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentKeys = @(
    "ConnectionStrings__MediaDock",
    "Authentication__LocalMock__Enabled",
    "Authentication__Google__Enabled",
    "AccessControl__Enforced",
    "BackgroundJobs__DispatcherEnabled",
    "ASPNETCORE_ENVIRONMENT",
    "DOTNET_ENVIRONMENT",
    "ASPNETCORE_URLS"
)
$previousEnvironment = @{}

foreach ($key in $environmentKeys) {
    $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
}

Push-Location $repositoryRoot
try {
    if ($Database -notmatch "^[a-z][a-z0-9_]*$") {
        throw "Database must be a lowercase PostgreSQL identifier."
    }

    $composeJson = docker compose -f compose.yaml config --format json
    if ($LASTEXITCODE -ne 0) {
        throw "Could not resolve the local Compose configuration."
    }

    $compose = $composeJson | ConvertFrom-Json
    $databaseService = $compose.services.db
    $loopbackPort = @($databaseService.ports | Where-Object {
        $_.target -eq 5432 -and $_.host_ip -eq "127.0.0.1"
    } | Select-Object -First 1)
    if ($loopbackPort.Count -ne 1) {
        throw "The local PostgreSQL service must publish port 5432 on 127.0.0.1."
    }

    $databaseUser = [string]$databaseService.environment.POSTGRES_USER
    $databasePassword = [string]$databaseService.environment.POSTGRES_PASSWORD
    if ([string]::IsNullOrWhiteSpace($databaseUser) -or [string]::IsNullOrWhiteSpace($databasePassword)) {
        throw "The local Compose PostgreSQL credentials are not configured."
    }

    $activeUserCount = docker compose -f compose.yaml exec -T db psql `
        -U $databaseUser -d $Database -Atc "SELECT count(*) FROM users WHERE status='active' AND role IN ('user','admin')"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the selected local database. Restore it first."
    }

    $linkedIdentityCount = docker compose -f compose.yaml exec -T db psql `
        -U $databaseUser -d $Database -Atc "SELECT count(*) FROM external_identities WHERE issuer='https://accounts.google.com' AND user_id IN (SELECT id FROM users WHERE status='active' AND role IN ('user','admin'))"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not verify the local Google identity."
    }

    if ($activeUserCount.Trim() -ne "1" -or $linkedIdentityCount.Trim() -ne "1") {
        throw "The local mock requires exactly one active user with one linked Google identity in '$Database'."
    }

    $connectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connectionBuilder["Host"] = "127.0.0.1"
    $connectionBuilder["Port"] = [int]$loopbackPort[0].published
    $connectionBuilder["Database"] = $Database
    $connectionBuilder["Username"] = $databaseUser
    $connectionBuilder["Password"] = $databasePassword

    $env:ConnectionStrings__MediaDock = $connectionBuilder.ConnectionString
    $env:Authentication__LocalMock__Enabled = "true"
    $env:Authentication__Google__Enabled = "false"
    $env:AccessControl__Enforced = "true"
    $env:BackgroundJobs__DispatcherEnabled = "false"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = "http://localhost:5280"

    dotnet run --no-launch-profile --project server/src/MediaDock.Api/MediaDock.Api.csproj
    if ($LASTEXITCODE -ne 0) {
        throw "The local API exited with code $LASTEXITCODE."
    }
}
finally {
    foreach ($key in $environmentKeys) {
        [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], "Process")
    }
    Pop-Location
}