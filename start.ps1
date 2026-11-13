<#
.SYNOPSIS
Starts the local Docker development stack, including BE and FE by default.
.EXAMPLE
.\start.ps1
.EXAMPLE
.\start.ps1 -Services be
.EXAMPLE
.\start.ps1 -Services fe -ApiPort 8081
.EXAMPLE
.\start.ps1 -Services postgres,nats
.EXAMPLE
.\start.ps1 -Action stop
#>
[CmdletBinding()]
param(
	[ValidateSet("be", "fe", "api", "worker", "postgres", "nats", "migrate")]
	[string[]]$Services = @("be", "fe"),

	[ValidateSet("start", "stop", "status", "logs", "down")]
	[string]$Action = "start",

	[ValidatePattern("^[a-z0-9][a-z0-9_-]*$")]
	[string]$ProjectName = "nuntivus-local",

	[ValidateRange(1, 65535)]
	[int]$ApiPort = 8080,

	[ValidateRange(1, 65535)]
	[int]$FrontendPort = 5173,

	[string]$ApiBaseUrl = "",
	[string]$AdminUsername = "admin",

	[ValidateRange(30, 3600)]
	[int]$WaitTimeoutSeconds = 300,

	[switch]$NoBuild,
	[switch]$SkipAdminSetup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Services.Count -eq 0) { throw "Select at least one service." }
$selected = @($Services | ForEach-Object {
	switch ($_)
	{
		"be" { "api"; "worker" }
		"fe" { "dashboard" }
		default { $_ }
	}
} | Select-Object -Unique)

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) { $ApiBaseUrl = "http://localhost:$ApiPort" }
$uri = $null
if (-not [Uri]::TryCreate($ApiBaseUrl, [UriKind]::Absolute, [ref]$uri) -or
	$uri.Scheme -notin @("http", "https") -or -not [string]::IsNullOrEmpty($uri.UserInfo))
{
	throw "ApiBaseUrl must be an absolute HTTP(S) URL without embedded credentials."
}
if ($selected -contains "api" -and $selected -contains "dashboard" -and $ApiPort -eq $FrontendPort)
{
	throw "ApiPort and FrontendPort must be different when starting BE and FE."
}

$root = $PSScriptRoot
$compose = @("compose", "-p", $ProjectName, "-f", (Join-Path $root "docker-compose.yml"),
	"-f", (Join-Path $root "docker-compose.local.yml"), "--profile", "fe")
$environment = @{
	NUNTIVUS_LOCAL_API_PORT = [string]$ApiPort
	NUNTIVUS_LOCAL_FE_PORT = [string]$FrontendPort
	NUNTIVUS_LOCAL_API_URL = $ApiBaseUrl
}
$previousEnvironment = @{}

function Invoke-Compose([string[]]$Arguments)
{
	& docker @compose @Arguments
	if ($LASTEXITCODE -ne 0)
	{
		throw "Docker Compose failed ($LASTEXITCODE). Inspect '.\start.ps1 -ProjectName $ProjectName -Action logs'. Check occupied ports; use -ApiPort or -FrontendPort to change them."
	}
}

function Initialize-Admin
{
	$count = @(Invoke-Compose -Arguments @("exec", "-T", "postgres", "psql", "-U", "postgres", "-d", "nuntivus", "-Atc", "SELECT count(*) FROM admin_users;"))
	if ($count.Count -ne 1 -or $count[0].Trim() -notmatch "^\d+$")
	{
		throw "Unable to determine whether an initial admin already exists."
	}
	if ([long]$count[0].Trim() -ne 0) { return }
	if ($SkipAdminSetup -or [Console]::IsInputRedirected -or -not [Environment]::UserInteractive)
	{
		Write-Host "[nuntivus-local] No admin exists. Rerun interactively without -SkipAdminSetup to provision one."
		return
	}
	$answer = Read-Host "Create initial local administrator '$AdminUsername' now? [Y/n]"
	if ([string]::IsNullOrWhiteSpace($answer) -or $answer -match "^(y|yes)$")
	{
		Invoke-Compose -Arguments @("exec", "-e", "AdminBootstrap__Username=$AdminUsername", "api", "dotnet", "NuntivusServer.Api.dll", "--create-admin")
	}
}

Get-Command docker -ErrorAction Stop | Out-Null
$dockerOs = @(& docker info --format "{{.OSType}}")
if ($LASTEXITCODE -ne 0 -or $dockerOs.Count -ne 1 -or $dockerOs[0].Trim() -ne "linux")
{
	throw "Start Docker Desktop with Linux containers before running this script."
}
$composeVersion = & docker compose version --short
if ($LASTEXITCODE -ne 0 -or $composeVersion -notmatch "v?(\d+\.\d+\.\d+)")
{
	throw "Docker Compose 2.24.4 or newer is required."
}
if ([version]$Matches[1] -lt [version]"2.24.4") { throw "Docker Compose 2.24.4 or newer is required." }

try
{
	foreach ($key in $environment.Keys)
	{
		$previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
		[Environment]::SetEnvironmentVariable($key, $environment[$key], "Process")
	}
	Write-Host "[nuntivus-local] ${Action}: $($selected -join ', ') (project: $ProjectName)"
	switch ($Action)
	{
		"start"
		{
			if ($selected -contains "migrate" -and $selected -notcontains "api" -and $selected -notcontains "worker")
			{
				$migrationArguments = @("run", "--rm")
				if (-not $NoBuild) { $migrationArguments += "--build" }
				Invoke-Compose -Arguments ($migrationArguments + "migrate")
			}
			$runningServices = @($selected | Where-Object { $_ -ne "migrate" })
			if ($runningServices.Count -gt 0)
			{
				$arguments = @("up", "-d", "--wait", "--wait-timeout", [string]$WaitTimeoutSeconds)
				if (-not $NoBuild) { $arguments += "--build" }
				Invoke-Compose -Arguments ($arguments + $runningServices)
			}
			if ($selected -contains "api")
			{
				Invoke-Compose -Arguments @("exec", "-T", "api", "wget", "-qO-", "http://127.0.0.1:8080/health/ready") | Out-Null
				Write-Host "[nuntivus-local] API: http://localhost:$ApiPort | Swagger: http://localhost:$ApiPort/swagger"
				Initialize-Admin
			}
			if ($selected -contains "dashboard")
			{
				Write-Host "[nuntivus-local] Dashboard: http://localhost:$FrontendPort | API target: $ApiBaseUrl"
			}
			Write-Host "[nuntivus-local] Services stay running in Docker. PostgreSQL/NATS ports are internal only."
		}
		"stop" { Invoke-Compose -Arguments (@("stop") + $selected) }
		"status" { Invoke-Compose -Arguments @("ps", "-a") }
		"logs" { Invoke-Compose -Arguments (@("logs", "--tail", "100", "-f") + $selected) }
		"down" { Invoke-Compose -Arguments @("down") }
	}
}
finally
{
	foreach ($key in $previousEnvironment.Keys)
	{
		[Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], "Process")
	}
}
