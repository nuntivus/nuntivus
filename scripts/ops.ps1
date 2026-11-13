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
	[switch]$SkipBackup,
	[switch]$Build
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info([string]$Message)
{
	Write-Host "[nuntivus-ops] $Message"
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

function Invoke-Compose
{
	param([string[]]$ComposeArgs)

	& docker compose --env-file $EnvFile -f $ComposeFile @ComposeArgs
	if ($LASTEXITCODE -ne 0) { throw "docker compose command failed ($LASTEXITCODE)." }
}

function Get-ComposeContainerId([string]$Service)
{
	$ids = @(& docker compose --env-file $EnvFile -f $ComposeFile ps -a -q $Service | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
	if ($LASTEXITCODE -ne 0 -or $ids.Count -ne 1)
	{
		throw "Unable to resolve exactly one container for service '$Service'. Make sure the stack exists."
	}
	return $ids[0].Trim()
}

function Wait-ApiReady
{
	for ($attempt = 1; $attempt -le 30; $attempt++)
	{
		& docker compose --env-file $EnvFile -f $ComposeFile exec -T api wget -qO- http://localhost:8080/health/ready 2>$null | Out-Null
		if ($LASTEXITCODE -eq 0) { Write-Info "API readiness check succeeded inside the container."; return }
		Start-Sleep -Seconds 2
	}
	throw "API container readiness check did not pass."
}

function Get-JetStreamVolume
{
	$id = Get-ComposeContainerId -Service "nats"
	$inspection = & docker inspect $id
	if ($LASTEXITCODE -ne 0) { throw "Unable to inspect the NATS container." }
	$container = @($inspection | ConvertFrom-Json)[0]
	$mounts = @($container.Mounts | Where-Object { $_.Destination -eq "/data" -and $_.Type -eq "volume" })
	if ($mounts.Count -ne 1 -or [string]::IsNullOrWhiteSpace($mounts[0].Name)) { throw "Unable to resolve the NATS data volume." }
	return $mounts[0].Name
}

function Invoke-NuntivusBackup
{
	$envMap = Get-EnvMap -Path $EnvFile
	$dbUser = Get-RequiredEnvValue -Map $envMap -Key "NUNTIVUS_POSTGRES_USER"
	$dbName = Get-RequiredEnvValue -Map $envMap -Key "NUNTIVUS_POSTGRES_DB"
	$currentTag = Get-RequiredEnvValue -Map $envMap -Key "NUNTIVUS_IMAGE_TAG" -Default "latest"

	$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
	$backupDir = Join-Path $BackupRoot $timestamp
	New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

	$dbDumpName = "nuntivus-postgres-$timestamp.sql"
	$dbDumpPath = Join-Path $backupDir $dbDumpName

	$jetStreamDumpName = "nuntivus-jetstream-$timestamp.tgz"
	$jetStreamDumpPath = Join-Path $backupDir $jetStreamDumpName
	$resolvedBackupDir = (Resolve-Path -LiteralPath $backupDir).Path

	Write-Info "Creating backups..."
	$jetStreamVolume = Get-JetStreamVolume

	try
	{
		Invoke-Compose @("stop", "api", "worker", "nats")
		Write-Info "Creating PostgreSQL backup..."
		Invoke-Compose @("exec", "-T", "postgres", "sh", "-ec", "pg_dump -U '$dbUser' -d '$dbName' -Fp --clean --if-exists --no-owner --no-privileges -f /tmp/$dbDumpName")
		$postgresContainer = Get-ComposeContainerId -Service "postgres"
		& docker cp "$postgresContainer`:/tmp/$dbDumpName" "$dbDumpPath"
		if ($LASTEXITCODE -ne 0)
		{
			throw "Failed to copy PostgreSQL dump from container."
		}

		Invoke-Compose @("exec", "-T", "postgres", "sh", "-ec", "rm -f /tmp/$dbDumpName")

		Write-Info "Creating JetStream backup..."
		& docker run --rm -v "${jetStreamVolume}:/data:ro" -v "${resolvedBackupDir}:/backup" alpine:3.21 sh -ec "cd /data && tar czf /backup/$jetStreamDumpName ."
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
					sha256 = (Get-FileHash -LiteralPath $dbDumpPath -Algorithm SHA256).Hash
					bytes = (Get-Item -LiteralPath $dbDumpPath).Length
				},
				[ordered]@{
					type = "jetstream-archive"
					path = $jetStreamDumpName
					sha256 = (Get-FileHash -LiteralPath $jetStreamDumpPath -Algorithm SHA256).Hash
					bytes = (Get-Item -LiteralPath $jetStreamDumpPath).Length
				}
			)
		}

		$manifestPath = Join-Path $backupDir "manifest.json"
		$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath

		Write-Info "Backup completed: $backupDir"
		return $backupDir
	}
	finally
	{
		Write-Info "Starting services back up..."
		Invoke-Compose @("start", "nats", "worker", "api")
		Wait-ApiReady
	}
}

function Invoke-NuntivusRestore
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
	$dbUser = Get-RequiredEnvValue -Map $envMap -Key "NUNTIVUS_POSTGRES_USER"
	$dbName = Get-RequiredEnvValue -Map $envMap -Key "NUNTIVUS_POSTGRES_DB"

	Write-Info "Stopping API/worker/NATS before restore..."
	Invoke-Compose @("stop", "api", "worker", "nats")

	$jetStreamVolume = Get-JetStreamVolume

	Write-Info "Restoring PostgreSQL..."
	$postgresContainer = Get-ComposeContainerId -Service "postgres"
	& docker cp "$DbDumpPath" "$postgresContainer`:/tmp/nuntivus-restore.sql"
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to copy SQL restore file into PostgreSQL container."
	}

	Invoke-Compose @("exec", "-T", "postgres", "sh", "-ec", "psql -U '$dbUser' -d '$dbName' -v ON_ERROR_STOP=1 -f /tmp/nuntivus-restore.sql")
	Invoke-Compose @("exec", "-T", "postgres", "sh", "-ec", "rm -f /tmp/nuntivus-restore.sql")

	Write-Info "Restoring JetStream volume..."
	$resolvedJetStreamPath = (Resolve-Path -LiteralPath $JetStreamDumpPath).Path
	$jetStreamParent = Split-Path -Path $resolvedJetStreamPath -Parent
	$jetStreamFile = Split-Path -Path $resolvedJetStreamPath -Leaf

	& docker run --rm -v "${jetStreamVolume}:/data" alpine:3.21 sh -ec "find /data -mindepth 1 -maxdepth 1 -exec rm -rf {} +"
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to clear JetStream volume before restore."
	}

	& docker run --rm -v "${jetStreamVolume}:/data" -v "${jetStreamParent}:/backup" alpine:3.21 sh -ec "cd /data && tar xzf /backup/$jetStreamFile"
	if ($LASTEXITCODE -ne 0)
	{
		throw "Failed to extract JetStream archive."
	}

	Write-Info "Starting stack after restore..."
	Invoke-Compose @("up", "-d")
	Wait-ApiReady

	Write-Info "Restore completed successfully."
}

function Invoke-NuntivusUpgrade
{
	if ([string]::IsNullOrWhiteSpace($Tag))
	{
		throw "Upgrade requires -Tag <image-tag>."
	}

	$envMap = Get-EnvMap -Path $EnvFile
	$previousTag = Get-RequiredEnvValue -Map $envMap -Key "NUNTIVUS_IMAGE_TAG" -Default "latest"

	if (-not $SkipBackup)
	{
		Write-Info "Running pre-upgrade backup..."
		$null = Invoke-NuntivusBackup
	}

	Write-Info "Updating NUNTIVUS_IMAGE_TAG from '$previousTag' to '$Tag'"
	Set-EnvValue -Path $EnvFile -Key "NUNTIVUS_IMAGE_TAG" -Value $Tag

	try
	{
		Write-Info "Pulling updated images..."
		if ($Build) { Invoke-Compose @("build", "api", "worker", "migrate") } else { Invoke-Compose @("pull", "api", "worker", "migrate") }

		Write-Info "Applying upgrade..."
		Invoke-Compose @("up", "-d")

		Wait-ApiReady
		Write-Info "Upgrade completed successfully."
	}
	catch
	{
		Write-Info "Upgrade failed. Rolling back to tag '$previousTag'..."
		Set-EnvValue -Path $EnvFile -Key "NUNTIVUS_IMAGE_TAG" -Value $previousTag
		Invoke-Compose @("up", "-d")
		throw
	}
}

function Invoke-NuntivusRun
{
	$envMap = Get-EnvMap -Path $EnvFile

	Write-Info "Starting Nuntivus production stack..."
	Invoke-Compose @("up", "-d")
	Wait-ApiReady

	Write-Info "Nuntivus stack is running."
}

function Invoke-NuntivusStop
{
	Write-Info "Stopping Nuntivus production stack..."
	Invoke-Compose @("down")

	Write-Info "Nuntivus stack stopped."
}

switch ($Action)
{
	"backup"
	{
		Invoke-NuntivusBackup | Out-Null
	}
	"restore"
	{
		Invoke-NuntivusRestore
	}
	"upgrade"
	{
		Invoke-NuntivusUpgrade
	}
	"run"
	{
		Invoke-NuntivusRun
	}
	"stop"
	{
		Invoke-NuntivusStop
	}
}
