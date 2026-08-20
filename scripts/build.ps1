#Requires -Version 5.1
<#
.SYNOPSIS
  缂栬瘧銆岀啍宀╄秴绾у壀璐存澘銆嶃€?
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
    throw "鏈壘鍒伴」鐩枃浠? $Project"
}

Write-Host "==> 缂栬瘧 ($Configuration)" -ForegroundColor Cyan
dotnet build $Project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "缂栬瘧澶辫触 (exit $LASTEXITCODE)"
}

Write-Host "==> 瀹屾垚" -ForegroundColor Green
