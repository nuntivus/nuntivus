Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$helper = Join-Path $PSScriptRoot "../../scripts/export-public-repository.ps1"
$fixture = Join-Path ([IO.Path]::GetTempPath()) ("nuntivus-export-test-" + [guid]::NewGuid().ToString("N"))
$source = Join-Path $fixture "source"
$previousLocation = Get-Location

function Assert([bool]$Condition, [string]$Message)
{
	if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock]$Action, [string]$Message)
{
	$rejected = $false
	try { & $Action } catch { $rejected = $true }
	Assert $rejected $Message
}

try
{
	[void][IO.Directory]::CreateDirectory((Join-Path $source "scripts"))
	Copy-Item -LiteralPath $helper -Destination (Join-Path $source "scripts/export-public-repository.ps1")
	Set-Content -LiteralPath (Join-Path $source ".gitignore") -Value @(".env", "bin/", "*.log")
	Set-Content -LiteralPath (Join-Path $source "README.md") -Value "initial"
	Set-Content -LiteralPath (Join-Path $source "file with spaces.txt") -Value "portable"
	& git -C $source init --quiet
	Assert ($LASTEXITCODE -eq 0) "Fixture Git initialization failed."
	& git -C $source add -- .
	Assert ($LASTEXITCODE -eq 0) "Fixture staging failed."
	Set-Content -LiteralPath (Join-Path $source "README.md") -Value "working tree"
	Set-Content -LiteralPath (Join-Path $source ".env") -Value "private local setting"
	Set-Content -LiteralPath (Join-Path $source "extra.md") -Value "explicit addition"
	Set-Content -LiteralPath (Join-Path $source "untracked.txt") -Value "do not include"
	[void][IO.Directory]::CreateDirectory((Join-Path $source "bin"))
	Set-Content -LiteralPath (Join-Path $source "bin/output.txt") -Value "generated"
	$fixtureHelper = Join-Path $source "scripts/export-public-repository.ps1"
	$before = (& git -C $source status --porcelain=v1) -join "`n"
	$indexHash = (Get-FileHash -LiteralPath (Join-Path $source ".git/index")).Hash
	Set-Location $fixture

	& $fixtureHelper -Destination "public" -AdditionalPaths "extra.md"
	$export = Join-Path $fixture "public"
	Assert ((Get-Content -LiteralPath (Join-Path $export "README.md")) -eq "working tree") "Export must use working-tree changes."
	Assert (Test-Path -LiteralPath (Join-Path $export "file with spaces.txt")) "Paths containing spaces must work."
	Assert (Test-Path -LiteralPath (Join-Path $export "extra.md")) "Explicit additions must be included."
	foreach ($excluded in @(".git", ".env", "bin", "untracked.txt"))
	{
		Assert (-not (Test-Path -LiteralPath (Join-Path $export $excluded))) "Export included $excluded."
	}
	Assert ((Get-Location).Path -eq $fixture) "Export must not change the caller's location."
	Assert (((& git -C $source status --porcelain=v1) -join "`n") -eq $before) "Export must preserve source status."
	Assert ((Get-FileHash -LiteralPath (Join-Path $source ".git/index")).Hash -eq $indexHash) "Export must preserve the index."

	Assert-Rejected { & $fixtureHelper -Destination $export } "Existing destinations must be refused."
	Assert-Rejected { & $fixtureHelper -Destination (Join-Path $source "nested") } "Nested destinations must be refused."
	Assert-Rejected { & $fixtureHelper -Destination (Join-Path $fixture "ignored") -AdditionalPaths ".env" } "Ignored additions must be refused."
	Assert-Rejected { & $fixtureHelper -Destination (Join-Path $fixture "escape") -AdditionalPaths "../outside.txt" } "Traversal must be refused."
	Assert-Rejected { & $fixtureHelper -Destination (Join-Path $fixture "metadata") -AdditionalPaths ".git/config" } "Git metadata must be refused."
	Assert-Rejected { & $fixtureHelper -Destination (Join-Path $fixture "missing") -AdditionalPaths "missing.txt" } "Missing additions must be refused."
	foreach ($rejected in @("ignored", "escape", "metadata", "missing"))
	{
		Assert (-not (Test-Path -LiteralPath (Join-Path $fixture $rejected))) "Validation failure must not create output."
	}
	& git -C $source add --force -- .env
	Assert ($LASTEXITCODE -eq 0) "Fixture secret staging failed."
	Assert-Rejected { & $fixtureHelper -Destination (Join-Path $fixture "tracked-secret") } "Ignored tracked files must be refused."
	Write-Host "Public repository export tests passed."
}
finally
{
	Set-Location $previousLocation
	if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
