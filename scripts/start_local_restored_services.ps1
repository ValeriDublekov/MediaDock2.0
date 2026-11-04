[CmdletBinding()]
param(
    [string]$Database = "mediadock_prod_restore_20261029",
    [switch]$InstallDailyTask,
    [switch]$Stop,
    [switch]$RemoveDailyTask
)

$ErrorActionPreference = "Stop"
$taskName = "MediaDock Restored Local"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot "compose.yaml"
$apiRunner = Join-Path $PSScriptRoot "run_local_restored.ps1"
$viteEntry = Join-Path $repositoryRoot "web\node_modules\vite\bin\vite.js"
$stateDirectory = Join-Path $repositoryRoot ".test-tmp\restored-local"
$statePath = Join-Path $stateDirectory "processes.json"
$apiAddress = "http://localhost:5280"
$webAddress = "http://127.0.0.1:5173"

if ($Database -notmatch "^[a-z][a-z0-9_]*$") {
    throw "Database must be a lowercase PostgreSQL identifier."
}
if ($Stop -and $InstallDailyTask) {
    throw "Use -Stop and -InstallDailyTask in separate invocations."
}

New-Item -ItemType Directory -Force -Path $stateDirectory | Out-Null
$mutex = [System.Threading.Mutex]::new($false, "Local\MediaDockRestoredServices")
$hasMutex = $false

function Get-ListenerProcessId([int]$Port) {
    $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    $processIds = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($processIds.Count -gt 1) {
        throw "More than one process is listening on port $Port."
    }
    if ($processIds.Count -eq 0) {
        return $null
    }
    return [int]$processIds[0]
}

function Get-ProcessInfo([int]$ProcessId) {
    Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
}

function Test-RestoredApiProcess([int]$ProcessId, [string]$ExpectedDatabase) {
    $currentProcessId = $ProcessId
    for ($depth = 0; $depth -lt 6 -and $currentProcessId -gt 0; $depth++) {
        $process = Get-ProcessInfo $currentProcessId
        if ($null -eq $process) {
            return $false
        }

        $commandLine = [string]$process.CommandLine
        if ($commandLine -like "*$apiRunner*") {
            $databaseArgument = [regex]::Match($commandLine, '(?:^|\s)-Database\s+"?([^"\s]+)"?')
            if ($databaseArgument.Success) {
                return $databaseArgument.Groups[1].Value -ceq $ExpectedDatabase
            }
            return $ExpectedDatabase -ceq "mediadock_prod_restore_20261029"
        }

        $parentProcessId = [int]$process.ParentProcessId
        if ($parentProcessId -eq $currentProcessId) {
            break
        }
        $currentProcessId = $parentProcessId
    }
    return $false
}

function Test-HttpEndpoint([string]$Uri, [string]$RequiredText = "") {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -TimeoutSec 3
        return $response.StatusCode -eq 200 -and ($RequiredText -eq "" -or $response.Content.Contains($RequiredText))
    } catch {
        return $false
    }
}

function Save-ProcessState($State) {
    $State | ConvertTo-Json | Set-Content -Path $statePath -Encoding UTF8
}

function Stop-ManagedProcess([string]$Service, [int]$ProcessId, [string]$ExpectedDatabase) {
    $process = Get-ProcessInfo $ProcessId
    if ($null -eq $process) {
        Write-Host "$Service is already stopped."
        return $true
    }

    $isExpectedProcess = if ($Service -eq "API") {
        Test-RestoredApiProcess -ProcessId $ProcessId -ExpectedDatabase $ExpectedDatabase
    } else {
        [string]$process.CommandLine -like "*$viteEntry*"
    }
    if (-not $isExpectedProcess) {
        Write-Warning "Not stopping PID $ProcessId because it no longer matches the expected $Service command."
        return $false
    }

    & taskkill.exe /PID $ProcessId /T /F 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not stop $Service PID $ProcessId."
        return $false
    }
    Write-Host "Stopped $Service."
    return $true
}

function Remove-LocalTask {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host "Removed scheduled task '$taskName'."
    } else {
        Write-Host "Scheduled task '$taskName' is not installed."
    }
}

function Install-LocalTask {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $powerShellPath = Join-Path $PSHOME "powershell.exe"
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Database "{1}"' -f $PSCommandPath, $Database
    $action = New-ScheduledTaskAction -Execute $powerShellPath -Argument $arguments -WorkingDirectory $repositoryRoot
    $triggers = @(
        (New-ScheduledTaskTrigger -AtLogOn -User $identity),
        (New-ScheduledTaskTrigger -Daily -At ([datetime]::Today.AddHours(8)))
    )
    $principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Description "Start the loopback-only MediaDock restored-data Development instance; reuse its existing local database." -Force | Out-Null
    Write-Host "Installed '$taskName' for this user at logon and daily at 08:00 local time."
}

function Start-LocalServices {
    if (-not (Test-Path $viteEntry)) {
        throw "Vite dependencies are missing. Run 'npm ci' in the web directory first."
    }

    $composeJson = & docker compose -f $composeFile config --format json
    if ($LASTEXITCODE -ne 0) {
        throw "Could not resolve the local Compose configuration."
    }
    $compose = $composeJson | ConvertFrom-Json
    $databaseService = $compose.services.db
    $databaseUser = [string]$databaseService.environment.POSTGRES_USER
    $databaseName = [string]$databaseService.environment.POSTGRES_DB
    $loopbackPort = @($databaseService.ports | Where-Object {
        $_.target -eq 5432 -and $_.host_ip -eq "127.0.0.1"
    })
    if ($loopbackPort.Count -ne 1) {
        throw "The local PostgreSQL service must publish port 5432 on 127.0.0.1."
    }

    $dockerReady = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        & docker info --format "{{.ServerVersion}}" 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $dockerReady = $true
            break
        }
        Start-Sleep -Seconds 2
    }
    if (-not $dockerReady) {
        throw "Docker is not ready. Start Docker Desktop, then retry."
    }

    & docker compose -f $composeFile up -d db | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not start the local PostgreSQL container."
    }

    $databaseReady = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        & docker compose -f $composeFile exec -T db pg_isready -U $databaseUser -d $databaseName 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $databaseReady = $true
            break
        }
        Start-Sleep -Seconds 2
    }
    if (-not $databaseReady) {
        throw "Local PostgreSQL did not become ready. The database volume was not changed."
    }

    $databaseExists = & docker compose -f $composeFile exec -T db psql -U $databaseUser -d postgres -Atc "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = '$Database')" 2>$null
    if ($LASTEXITCODE -ne 0 -or $databaseExists.Trim() -ne "t") {
        throw "Restored database '$Database' is missing. This script never creates or restores databases."
    }
    $identityCounts = & docker compose -f $composeFile exec -T db psql -U $databaseUser -d $Database -Atc "SELECT (SELECT count(*) FROM users WHERE status='active' AND role IN ('user','admin')) || ':' || (SELECT count(*) FROM external_identities WHERE issuer='https://accounts.google.com' AND user_id IN (SELECT id FROM users WHERE status='active' AND role IN ('user','admin')))" 2>$null
    if ($LASTEXITCODE -ne 0 -or $identityCounts.Trim() -ne "1:1") {
        throw "The restored database must contain exactly one active user with one linked Google identity. No database rows were changed."
    }

    $state = [ordered]@{
        database = $Database
        apiProcessId = $null
        webProcessId = $null
    }
    $apiProcessId = Get-ListenerProcessId 5280
    if ($apiProcessId) {
        if (-not (Test-RestoredApiProcess -ProcessId $apiProcessId -ExpectedDatabase $Database)) {
            throw "Port 5280 is not served by the restored-data runner for database '$Database'."
        }
        if (-not (Test-HttpEndpoint "$apiAddress/health/ready") -or -not (Test-HttpEndpoint "$apiAddress/api/catalog?page=1&pageSize=1")) {
            throw "The process on port 5280 is not a ready, authorized MediaDock API."
        }
        Write-Host "Reusing the restored-data API on port 5280."
    } else {
        $apiOutput = Join-Path $stateDirectory "api.out.log"
        $apiError = Join-Path $stateDirectory "api.err.log"
        $powerShellPath = Join-Path $PSHOME "powershell.exe"
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Database "{1}"' -f $apiRunner, $Database
        Start-Process -FilePath $powerShellPath -ArgumentList $arguments -WorkingDirectory $repositoryRoot -WindowStyle Hidden -RedirectStandardOutput $apiOutput -RedirectStandardError $apiError | Out-Null
        $apiReady = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if (Test-HttpEndpoint "$apiAddress/health/ready" -and (Test-HttpEndpoint "$apiAddress/api/catalog?page=1&pageSize=1")) {
                $apiReady = $true
                break
            }
            Start-Sleep -Seconds 2
        }
        if (-not $apiReady) {
            throw "The restored API did not become ready. Review local logs under '$stateDirectory'; they may contain restored-data details."
        }
        $apiProcessId = Get-ListenerProcessId 5280
        Write-Host "Started the restored-data API on port 5280."
    }
    $state.apiProcessId = $apiProcessId
    Save-ProcessState $state

    $webProcessId = Get-ListenerProcessId 5173
    if ($webProcessId) {
        $webProcess = Get-ProcessInfo $webProcessId
        if ([string]$webProcess.CommandLine -notlike "*$viteEntry*") {
            throw "Port 5173 is occupied by a process that is not Vite."
        }
        if (-not (Test-HttpEndpoint "$webAddress/" "<title>MediaDock</title>")) {
            throw "The Vite process on port 5173 is not serving this MediaDock web app."
        }
        Write-Host "Reusing the MediaDock Vite server on port 5173."
    } else {
        $nodePath = (Get-Command node.exe -ErrorAction Stop).Source
        $webOutput = Join-Path $stateDirectory "web.out.log"
        $webError = Join-Path $stateDirectory "web.err.log"
        $arguments = '"{0}" --host 127.0.0.1' -f $viteEntry
        Start-Process -FilePath $nodePath -ArgumentList $arguments -WorkingDirectory (Join-Path $repositoryRoot "web") -WindowStyle Hidden -RedirectStandardOutput $webOutput -RedirectStandardError $webError | Out-Null
        $webReady = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            if (Test-HttpEndpoint "$webAddress/" "<title>MediaDock</title>") {
                $webReady = $true
                break
            }
            Start-Sleep -Seconds 2
        }
        if (-not $webReady) {
            throw "The MediaDock Vite server did not become ready. Review local logs under '$stateDirectory'."
        }
        $webProcessId = Get-ListenerProcessId 5173
        Write-Host "Started Vite on port 5173."
    }
    $state.webProcessId = $webProcessId
    Save-ProcessState $state

    if ($InstallDailyTask) {
        Install-LocalTask
    }
    Write-Host "MediaDock is available at $webAddress. The existing restored database was reused; no restore was run."
}

try {
    $hasMutex = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
    if (-not $hasMutex) {
        throw "Another local MediaDock start/stop operation is already running."
    }

    if ($Stop) {
        if (-not (Test-Path $statePath)) {
            Write-Host "No managed local MediaDock processes were recorded."
        } else {
            $state = Get-Content -Path $statePath -Raw | ConvertFrom-Json
            $apiStopped = -not $state.apiProcessId -or (Stop-ManagedProcess -Service "API" -ProcessId ([int]$state.apiProcessId) -ExpectedDatabase ([string]$state.database))
            $webStopped = -not $state.webProcessId -or (Stop-ManagedProcess -Service "Vite" -ProcessId ([int]$state.webProcessId) -ExpectedDatabase ([string]$state.database))
            if ($apiStopped -and $webStopped) {
                Remove-Item -Path $statePath -Force
            }
        }
    }

    if ($RemoveDailyTask) {
        Remove-LocalTask
    }

    if (-not $Stop -and -not $RemoveDailyTask) {
        Start-LocalServices
    }
} finally {
    if ($hasMutex) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}