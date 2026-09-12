#Requires -Version 5.1
<#
.SYNOPSIS
  Build and publish Clipboard.Net to the publish folder.
.DESCRIPTION
  Version comes from Clipboard.csproj <Version> (semantic versioning, driven by
  git tags). The script does NOT bump the version anymore.
  Produces a single-file exe WITHOUT the .NET runtime (the target machine must
  have the .NET Desktop Runtime installed).
  Pass -SelfContained to bundle the .NET runtime into the exe.
.EXAMPLE
  .\scripts\publish.ps1
  .\scripts\publish.ps1 -SelfContained
  .\scripts\publish.ps1 -Runtime win-arm64 -OutputDir D:\dist\clipboard
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    # Bundle the .NET runtime into a single-file exe. Default is a single-file
    # exe that does NOT include the .NET runtime.
    [switch]$SelfContained,

    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "Clipboard.csproj"

# 输出目录缺省为仓库根目录下 publish/；相对路径一律相对仓库根目录解析
if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot $OutputDir
}

if (-not (Test-Path $Project)) {
    throw "Project file not found: $Project"
}

# 版本单一来源：Clipboard.csproj 的 <Version>（语义化版本，由 git tag 驱动）
$proj = Get-Content -LiteralPath $Project -Raw -Encoding UTF8
if ($proj -notmatch '<Version>([^<]+)</Version>') {
    throw "Failed to parse <Version> from Clipboard.csproj"
}
$version = $Matches[1]

$scFlag = if ($SelfContained) { "true" } else { "false" }

Write-Host "==> Publishing v$version ($Configuration) -> $OutputDir" -ForegroundColor Cyan
Write-Host "    Runtime=$Runtime  SelfContained=$scFlag  SingleFile=true" -ForegroundColor DarkGray

# Clear previous publish output so leftover self-contained files are not mixed in.
if (Test-Path -LiteralPath $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

$publishArgs = @(
    "publish", $Project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $scFlag,
    "-o", $OutputDir,
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:DebugType=None",
    "/p:DebugSymbols=false"
)

if ($SelfContained) {
    $publishArgs += @("/p:EnableCompressionInSingleFile=true")
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed (exit $LASTEXITCODE)"
}

$exePath = Join-Path $OutputDir "Clipboard.exe"
if (Test-Path -LiteralPath $exePath) {
    $sizeMb = [math]::Round((Get-Item -LiteralPath $exePath).Length / 1MB, 2)
    Write-Host "==> Done: $exePath ($sizeMb MB)  v$version" -ForegroundColor Green
}
else {
    $fallback = Get-ChildItem -LiteralPath $OutputDir -Filter *.exe -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($fallback) {
        $sizeMb = [math]::Round($fallback.Length / 1MB, 2)
        Write-Host "==> Done: $($fallback.FullName) ($sizeMb MB)  v$version" -ForegroundColor Green
    }
    else {
        Write-Host "==> Done: $OutputDir  v$version" -ForegroundColor Green
    }
}
