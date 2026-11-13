Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$launcher = Join-Path $PSScriptRoot "../../start.ps1"
$global:calls = New-Object System.Collections.ArrayList
$global:adminCount = "1"
$global:failUp = $false
$global:version = "2.39.4"

function docker
{
	$arguments = @($args)
	$global:LASTEXITCODE = 0
	[void]$global:calls.Add([pscustomobject]@{
		Arguments = $arguments
		ApiPort = $env:NUNTIVUS_LOCAL_API_PORT
		FrontendPort = $env:NUNTIVUS_LOCAL_FE_PORT
		ApiUrl = $env:NUNTIVUS_LOCAL_API_URL
	})
	if ($arguments[0] -eq "info") { "linux"; return }
	if ($arguments -contains "version") { $global:version; return }
	if ($arguments -contains "up" -and $global:failUp) { $global:LASTEXITCODE = 1; return }
	if ($arguments -contains "psql") { $global:adminCount; return }
	if ($arguments -contains "wget") { '{"status":"Healthy"}' }
}

function Assert([bool]$Condition, [string]$Message)
{
	if (-not $Condition) { throw $Message }
}

function Reset-Calls
{
	$global:calls.Clear()
	$global:failUp = $false
	$global:adminCount = "1"
}

function Get-Up
{
	@($global:calls | Where-Object { $_.Arguments -contains "up" })[0]
}

$previousPort = $env:NUNTIVUS_LOCAL_API_PORT
$previousLocation = Get-Location
try
{
	$env:NUNTIVUS_LOCAL_API_PORT = "original"
	Set-Location $env:TEMP
	& $launcher -SkipAdminSetup
	$up = Get-Up
	Assert (($up.Arguments[-3..-1] -join ",") -eq "api,worker,dashboard") "Default must start BE and FE."
	Assert ($up.Arguments -contains "--wait") "Startup must wait for readiness."
	Assert ($up.Arguments -contains "--build") "Default must build backend images."
	Assert ($up.ApiPort -eq "8080" -and $up.FrontendPort -eq "5173") "Default ports are incorrect."
	Assert ($env:NUNTIVUS_LOCAL_API_PORT -eq "original") "Launcher must restore the caller's environment."
	Assert ((Get-Location).Path -eq $env:TEMP) "Launcher must not change the caller's working directory."
	Assert ($up.Arguments -contains (Join-Path (Split-Path $launcher -Parent | Resolve-Path) "docker-compose.local.yml")) "Compose paths must resolve independently of the current directory."

	Reset-Calls
	& $launcher -Services be -NoBuild -SkipAdminSetup
	$up = Get-Up
	Assert (($up.Arguments[-2..-1] -join ",") -eq "api,worker") "BE-only must not select FE."
	Assert (-not ($up.Arguments -contains "--build")) "NoBuild must omit build."

	Reset-Calls
	& $launcher -Services fe -ApiPort 8081 -FrontendPort 5174 -SkipAdminSetup
	$up = Get-Up
	Assert ($up.Arguments[-1] -eq "dashboard") "FE-only must select only dashboard."
	Assert ($up.ApiUrl -eq "http://localhost:8081" -and $up.FrontendPort -eq "5174") "Custom ports must reach Compose."
	Assert (@($global:calls | Where-Object { $_.Arguments -contains "psql" }).Count -eq 0) "FE-only must not access the backend database."

	Reset-Calls
	& $launcher -Services fe -ApiBaseUrl "https://api.example.test" -SkipAdminSetup
	Assert ((Get-Up).ApiUrl -eq "https://api.example.test") "FE must support an existing API URL."

	Reset-Calls
	& $launcher -Services postgres,nats -SkipAdminSetup
	Assert (((Get-Up).Arguments[-2..-1] -join ",") -eq "postgres,nats") "Infrastructure selection is incorrect."

	Reset-Calls
	& $launcher -Services migrate -NoBuild -SkipAdminSetup
	$migration = @($global:calls | Where-Object { $_.Arguments -contains "run" })[0]
	Assert ($migration.Arguments[-1] -eq "migrate" -and $migration.Arguments -contains "--rm") "One-shot migrations must use a disposable Compose run."
	Assert (@($global:calls | Where-Object { $_.Arguments -contains "up" }).Count -eq 0) "One-shot migrations must not be checked as long-running services."

	Reset-Calls
	& $launcher -Services be,api,fe -SkipAdminSetup
	Assert (((Get-Up).Arguments[-3..-1] -join ",") -eq "api,worker,dashboard") "Service aliases must be deduplicated."

	Reset-Calls
	$global:adminCount = "0"
	& $launcher -Services be -SkipAdminSetup
	Assert (@($global:calls | Where-Object { $_.Arguments -contains "--create-admin" }).Count -eq 0) "SkipAdminSetup must not create credentials."

	Reset-Calls
	& $launcher -Action down
	$down = @($global:calls | Where-Object { $_.Arguments -contains "down" })[0]
	Assert (-not ($down.Arguments -contains "--volumes")) "Shutdown must preserve stored data."
	Assert ($down.Arguments -contains "nuntivus-local") "Shutdown must be scoped to the local project."

	Reset-Calls
	& $launcher -Services fe -Action stop
	$stop = @($global:calls | Where-Object { $_.Arguments -contains "stop" })[0]
	Assert ($stop.Arguments[-1] -eq "dashboard") "Selective stop must not stop unrelated services."

	Reset-Calls
	$global:failUp = $true
	$failed = $false
	try { & $launcher -Services be -SkipAdminSetup } catch { $failed = $true }
	Assert $failed "Docker failures must not be reported as successful startup."
	Assert ($env:NUNTIVUS_LOCAL_API_PORT -eq "original") "Environment must be restored after Docker failure."

	Reset-Calls
	$failed = $false
	try { & $launcher -ApiPort 5173 -FrontendPort 5173 -SkipAdminSetup } catch { $failed = $true }
	Assert $failed "Overlapping BE and FE ports must be rejected."
	Assert ($global:calls.Count -eq 0) "Invalid options must be rejected before touching Docker."

	$global:version = "2.20.0"
	$failed = $false
	try { & $launcher -Services fe -SkipAdminSetup } catch { $failed = $true }
	Assert $failed "Compose versions without override support must be rejected."
	Write-Host "PASS: 13 launcher selection, environment, bootstrap, failure, and safety scenarios."
}
finally
{
	$env:NUNTIVUS_LOCAL_API_PORT = $previousPort
	Set-Location $previousLocation
}
