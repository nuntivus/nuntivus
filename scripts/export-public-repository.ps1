[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string]$Destination,

	[string[]]$AdditionalPaths = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-GitPaths([string[]]$Arguments)
{
	$output = & git -C $root @Arguments
	if ($LASTEXITCODE -ne 0)
	{
		throw "Git could not enumerate export files."
	}
	($output -join "`n") -split "`0" | Where-Object { $_.Length -gt 0 }
}

function Assert-NoReparsePoint([string]$Path)
{
	while (-not [string]::IsNullOrEmpty($Path))
	{
		if (Test-Path -LiteralPath $Path)
		{
			$item = Get-Item -LiteralPath $Path -Force
			if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
			{
				throw "Symbolic links and junctions are not supported in export paths: $Path"
			}
		}
		$Path = Split-Path -Path $Path -Parent
	}
}

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$repositoryRoot = & git -C $root rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($repositoryRoot) -ne $root)
{
	throw "Run this helper from a Git working tree rooted at the application directory."
}

$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($target -eq $root -or $target.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase))
{
	throw "The destination must be outside the source repository."
}
if (Test-Path -LiteralPath $target)
{
	throw "The destination already exists. Choose a new directory; nothing will be overwritten."
}
Assert-NoReparsePoint $target

$ignored = @(Get-GitPaths -Arguments @("ls-files", "-z", "--cached", "--ignored", "--exclude-standard"))
if ($ignored.Count -gt 0)
{
	throw "Tracked files match ignore rules. Review and untrack local artifacts before exporting."
}

$paths = @(Get-GitPaths -Arguments @("ls-files", "-z", "--cached")) + $AdditionalPaths
$files = @()
foreach ($path in ($paths | Sort-Object -Unique))
{
	if ([IO.Path]::IsPathRooted($path) -or $path -match '(^|[\\/])\.git([\\/]|$)')
	{
		throw "Export paths must be repository-relative files outside .git."
	}
	$source = [IO.Path]::GetFullPath((Join-Path $root $path))
	if (-not $source.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase))
	{
		throw "An export path escapes the repository: $path"
	}
	Assert-NoReparsePoint $source
	if (-not (Test-Path -LiteralPath $source -PathType Leaf))
	{
		throw "Export file is missing or is not a regular file: $path. Stage intended deletions first."
	}
	& git -C $root check-ignore --no-index --quiet -- $path
	if ($LASTEXITCODE -eq 0)
	{
		throw "An export path matches ignore rules: $path"
	}
	if ($LASTEXITCODE -ne 1)
	{
		throw "Git could not check ignore rules for: $path"
	}
	$files += [pscustomobject]@{ Source = $source; Relative = $source.Substring($rootPrefix.Length) }
}
if ($files.Count -eq 0)
{
	throw "There are no files to export."
}

[void][IO.Directory]::CreateDirectory($target)
foreach ($file in $files)
{
	$destinationFile = Join-Path $target $file.Relative
	[void][IO.Directory]::CreateDirectory((Split-Path $destinationFile -Parent))
	Copy-Item -LiteralPath $file.Source -Destination $destinationFile
}

Write-Host "Exported $($files.Count) working-tree files to $target"
Write-Host "No history, branches, tags, or remotes were copied. Untracked files require -AdditionalPaths."
Write-Host "Review and scan this directory before initializing master or publishing."
