[CmdletBinding()]
param(
	[Parameter(Mandatory = $true, Position = 0)]
	[ValidateSet("backup", "restore", "upgrade", "run", "stop")]
	[string]$Action,

	[string]$ComposeFile = "docker-compose.prod.yml",
	[string]$EnvFile = ".env",
	[string]$BackupRoot = "backups",

	[string]$DbDumpPath,
	[string]$JetStreamDumpPath,
	[string]$Tag,
	[switch]$SkipBackup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info([string]$Message)
{
	Write-Host "[kairo-ops] $Message"
}

function Get-RequiredEnvValue([hashtable]$Map, [string]$Key, [string]$Default = "")
{
	if ($Map.ContainsKey($Key) -and -not [string]::IsNullOrWhiteSpace($Map[$Key]))
	{
		return $Map[$Key]
	}

	if (-not [string]::IsNullOrWhiteSpace($Default))
	{
		return $Default
	}

	throw "Missing required environment variable '$Key' in '$EnvFile'."
}

function Get-EnvMap([string]$Path)
{
	if (-not (Test-Path -LiteralPath $Path))
	{
		throw "Environment file '$Path' not found. Copy '.env.example' to '.env' and configure it first."
	}

	$map = @{}
	foreach ($line in Get-Content -LiteralPath $Path)
	{
		$trimmed = $line.Trim()
		if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#"))
		{
			continue
		}

		$separatorIndex = $trimmed.IndexOf("=")
		if ($separatorIndex -lt 1)
		{
			continue
		}

		$key = $trimmed.Substring(0, $separatorIndex).Trim()
		$value = $trimmed.Substring($separatorIndex + 1).Trim()
		$map[$key] = $value
	}

	return $map
}

function Set-EnvValue([string]$Path, [string]$Key, [string]$Value)
{
	$lines = Get-Content -LiteralPath $Path
	$updated = $false

	for ($i = 0; $i -lt $lines.Count; $i++)
	{
		if ($lines[$i] -match "^\s*$([regex]::Escape($Key))\s*=")
		{
			$lines[$i] = "$Key=$Value"
			$updated = $true
			break
		}
	}

	if (-not $updated)
	{
		$lines += "$Key=$Value"
	}

	Set-Content -LiteralPath $Path -Value $lines
}

function Invoke-Compose([string[]]$ComposeArgs)
{
	& docker compose --env-file $EnvFile -f $ComposeFile @ComposeArgs
	if ($LASTEXITCODE -ne 0)
	{
		throw "docker compose command failed: docker compose --env-file $EnvFile -f $ComposeFile $($ComposeArgs -join ' ')"
	}
}

function Get-ComposeContainerId([string]$Service)
{
	$id = (& docker compose --env-file $EnvFile -f $ComposeFile ps -q $Service).Trim()
	if ([string]::IsNullOrWhiteSpace($id))
	{
		throw "Unable to resolve container id for service '$Service'. Make sure the stack is running."
	}

	return $id
}

function Wait-ApiReady([int]$Port)
{
	$uri = "http://localhost:$Port/health/ready"
	$attempts = 30

	for ($attempt = 1; $attempt -le $attempts; $attempt++)
	{
		try
		{
			$response = Invoke-WebRequest -Uri $uri -Method Get -TimeoutSec 5 -UseBasicParsing
			if ($response.StatusCode -eq 200)
			{
				Write-Info "API readiness check succeeded at $uri"
				return
			}
		}
		catch
		{
		}

		Start-Sleep -Seconds 2
	}

	throw "API readiness check did not pass at $uri after $attempts attempts."
}

function Invoke-KairoBackup
{
	$envMap = Get-EnvMap -Path $EnvFile
	$dbUser = Get-RequiredEnvValue -Map $envMap -Key "KAIRO_POSTGRES_USER"
	$dbName = Get-RequiredEnvValue -Map $envMap -Key "KAIRO_POSTGRES_DB"
	$currentTag = Get-RequiredEnvValue -Map $envMap -Key "KAIRO_IMAGE_TAG" -Default "latest"

	$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
	$backupDir = Join-Path $BackupRoot $timestamp
	New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

	$dbDumpName = "kairo-postgres-$timestamp.sql"
	$dbDumpPath = Join-Path $backupDir $dbDumpName

	Write-Info "Creating PostgreSQL backup..."
	Invoke-Compose @("exec", "-T", "postgres", "sh", "-ec", "pg_dump -U '$dbUser' -d '$dbName' -Fp -f /tmp/$dbDumpName")
	$postgresContainer = Get-ComposeContainerId -Service "postgres"
	& docker cp "$postgresContainer`:/tmp/$dbDumpName" "$dbDumpPath"
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to copy PostgreSQL dump from container."
	}

	Invoke-Compose @("exec", "-T", "postgres", "sh", "-ec", "rm -f /tmp/$dbDumpName")

	$jetStreamDumpName = "kairo-jetstream-$timestamp.tgz"
	$jetStreamDumpPath = Join-Path $backupDir $jetStreamDumpName
	$resolvedBackupDir = (Resolve-Path -LiteralPath $backupDir).Path

	Write-Info "Creating JetStream backup..."
	& docker run --rm -v "kairo-nats-data:/data" -v "${resolvedBackupDir}:/backup" alpine sh -ec "cd /data && tar czf /backup/$jetStreamDumpName ."
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to create JetStream volume archive."
	}

	$manifest = [ordered]@{
		schemaVersion = "1.0"
		generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
		generatedBy = "scripts/ops.ps1"
		dashboardImportCompatible = $true
		imageTag = $currentTag
		composeFile = $ComposeFile
		envFile = $EnvFile
		files = @(
			[ordered]@{
				type = "postgres-sql"
				path = $dbDumpName
			},
			[ordered]@{
				type = "jetstream-archive"
				path = $jetStreamDumpName
			}
		)
	}

	$manifestPath = Join-Path $backupDir "manifest.json"
	$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath

	Write-Info "Backup completed: $backupDir"
	return $backupDir
}

function Invoke-KairoRestore
{
	if ([string]::IsNullOrWhiteSpace($DbDumpPath) -or [string]::IsNullOrWhiteSpace($JetStreamDumpPath))
	{
		throw "Restore requires -DbDumpPath and -JetStreamDumpPath."
	}

	if (-not (Test-Path -LiteralPath $DbDumpPath))
	{
		throw "Database dump file not found: $DbDumpPath"
	}

	if (-not (Test-Path -LiteralPath $JetStreamDumpPath))
	{
		throw "JetStream archive file not found: $JetStreamDumpPath"
	}

	$envMap = Get-EnvMap -Path $EnvFile
	$dbUser = Get-RequiredEnvValue -Map $envMap -Key "KAIRO_POSTGRES_USER"
	$dbName = Get-RequiredEnvValue -Map $envMap -Key "KAIRO_POSTGRES_DB"
	$apiPort = [int](Get-RequiredEnvValue -Map $envMap -Key "KAIRO_API_PORT" -Default "8080")

	Write-Info "Stopping API/worker/NATS before restore..."
	Invoke-Compose @("stop", "api", "worker", "nats")

	Write-Info "Restoring PostgreSQL..."
	$postgresContainer = Get-ComposeContainerId -Service "postgres"
	& docker cp "$DbDumpPath" "$postgresContainer`:/tmp/kairo-restore.sql"
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to copy SQL restore file into PostgreSQL container."
	}

	Invoke-Compose @("exec", "-T", "postgres", "sh", "-ec", "psql -U '$dbUser' -d '$dbName' -v ON_ERROR_STOP=1 -f /tmp/kairo-restore.sql")
	Invoke-Compose @("exec", "-T", "postgres", "sh", "-ec", "rm -f /tmp/kairo-restore.sql")

	Write-Info "Restoring JetStream volume..."
	$resolvedJetStreamPath = (Resolve-Path -LiteralPath $JetStreamDumpPath).Path
	$jetStreamParent = Split-Path -Path $resolvedJetStreamPath -Parent
	$jetStreamFile = Split-Path -Path $resolvedJetStreamPath -Leaf

	& docker run --rm -v "kairo-nats-data:/data" alpine sh -ec "rm -rf /data/*"
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to clear JetStream volume before restore."
	}

	& docker run --rm -v "kairo-nats-data:/data" -v "${jetStreamParent}:/backup" alpine sh -ec "cd /data && tar xzf /backup/$jetStreamFile"
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to extract JetStream archive."
	}

	Write-Info "Starting stack after restore..."
	Invoke-Compose @("up", "-d")
	Wait-ApiReady -Port $apiPort

	Write-Info "Restore completed successfully."
}

function Invoke-KairoUpgrade
{
	if ([string]::IsNullOrWhiteSpace($Tag))
	{
		throw "Upgrade requires -Tag <image-tag>."
	}

	$envMap = Get-EnvMap -Path $EnvFile
	$previousTag = Get-RequiredEnvValue -Map $envMap -Key "KAIRO_IMAGE_TAG" -Default "latest"
	$apiPort = [int](Get-RequiredEnvValue -Map $envMap -Key "KAIRO_API_PORT" -Default "8080")

	if (-not $SkipBackup)
	{
		Write-Info "Running pre-upgrade backup..."
		$null = Invoke-KairoBackup
	}

	Write-Info "Updating KAIRO_IMAGE_TAG from '$previousTag' to '$Tag'"
	Set-EnvValue -Path $EnvFile -Key "KAIRO_IMAGE_TAG" -Value $Tag

	try
	{
		Write-Info "Pulling updated images..."
		Invoke-Compose @("pull")

		Write-Info "Applying upgrade..."
		Invoke-Compose @("up", "-d")

		Wait-ApiReady -Port $apiPort
		Write-Info "Upgrade completed successfully."
	}
	catch
	{
		Write-Info "Upgrade failed. Rolling back to tag '$previousTag'..."
		Set-EnvValue -Path $EnvFile -Key "KAIRO_IMAGE_TAG" -Value $previousTag
		Invoke-Compose @("up", "-d")
		throw
	}
}

function Invoke-KairoRun
{
	$envMap = Get-EnvMap -Path $EnvFile
	$apiPort = [int](Get-RequiredEnvValue -Map $envMap -Key "KAIRO_API_PORT" -Default "8080")

	Write-Info "Starting Kairo production stack..."
	Invoke-Compose @("up", "-d")
	Wait-ApiReady -Port $apiPort

	Write-Info "Kairo stack is running."
}

function Invoke-KairoStop
{
	Write-Info "Stopping Kairo production stack..."
	Invoke-Compose @("down")

	Write-Info "Kairo stack stopped."
}

switch ($Action)
{
	"backup"
	{
		Invoke-KairoBackup | Out-Null
	}
	"restore"
	{
		Invoke-KairoRestore
	}
	"upgrade"
	{
		Invoke-KairoUpgrade
	}
	"run"
	{
		Invoke-KairoRun
	}
	"stop"
	{
		Invoke-KairoStop
	}
}
