#Requires -Version 5.1
<#
.SYNOPSIS
  Build the Clipboard.Net project.
.EXAMPLE
  .\scripts\build.ps1
  .\scripts\build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "Clipboard\Clipboard.csproj"

if (-not (Test-Path $Project)) {
    throw "Project file not found: $Project"
}

Write-Host "==> Building ($Configuration)" -ForegroundColor Cyan
dotnet build $Project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (exit $LASTEXITCODE)"
}

Write-Host "==> Done" -ForegroundColor Green
