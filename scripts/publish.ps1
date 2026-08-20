#Requires -Version 5.1
<#
.SYNOPSIS
  Build and publish Clipboard.Net to the publish folder.
.DESCRIPTION
  By default increments BuildNumber and syncs VersionInfo.cs / Clipboard.csproj.
  Produces a single-file exe WITHOUT the .NET runtime (the target machine must
  have the .NET Desktop Runtime installed).
  Pass -SelfContained to bundle the .NET runtime into the exe.
.EXAMPLE
  .\scripts\publish.ps1
  .\scripts\publish.ps1 -NoBump
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

    [switch]$NoBump,

    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "Clipboard\Clipboard.csproj"
$VersionInfoPath = Join-Path $RepoRoot "Clipboard\VersionInfo.cs"

if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot "publish"
}

if (-not (Test-Path $Project)) {
    throw "Project file not found: $Project"
}
if (-not (Test-Path $VersionInfoPath)) {
    throw "Version file not found: $VersionInfoPath"
}

function Get-VersionState {
    $cs = Get-Content -LiteralPath $VersionInfoPath -Raw -Encoding UTF8

    if ($cs -notmatch 'public const string Version = "([^"]+)";') {
        throw "Failed to parse Version from VersionInfo.cs"
    }
    $version = $Matches[1]

    if ($cs -notmatch 'public const int BuildNumber = (\d+);') {
        throw "Failed to parse BuildNumber from VersionInfo.cs"
    }
    $build = [int]$Matches[1]

    [pscustomobject]@{
        Version     = $version
        BuildNumber = $build
        FullVersion = if ($build -gt 0) { "$version.$build" } else { $version }
    }
}

function Update-VersionFiles {
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][int]$BuildNumber,
        [Parameter(Mandatory)][string]$BuiltAt
    )

    $full = if ($BuildNumber -gt 0) { "$Version.$BuildNumber" } else { $Version }

    $cs = Get-Content -LiteralPath $VersionInfoPath -Raw -Encoding UTF8
    $cs = [regex]::Replace($cs, 'public const string Version = "[^"]+";', "public const string Version = `"$Version`";")
    $cs = [regex]::Replace($cs, 'public const int BuildNumber = \d+;', "public const int BuildNumber = $BuildNumber;")
    $cs = [regex]::Replace($cs, 'public const string BuiltAt = "[^"]+";', "public const string BuiltAt = `"$BuiltAt`";")
    Set-Content -LiteralPath $VersionInfoPath -Value $cs -Encoding UTF8 -NoNewline

    $proj = Get-Content -LiteralPath $Project -Raw -Encoding UTF8
    $proj = [regex]::Replace($proj, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
    $proj = [regex]::Replace($proj, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$full</FileVersion>")
    $proj = [regex]::Replace($proj, '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$full</InformationalVersion>")
    Set-Content -LiteralPath $Project -Value $proj -Encoding UTF8 -NoNewline

    return $full
}

$state = Get-VersionState
$version = $state.Version
$build = $state.BuildNumber

if (-not $NoBump) {
    $build++
    $builtAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $fullVersion = Update-VersionFiles -Version $version -BuildNumber $build -BuiltAt $builtAt
    Write-Host "==> Bumped version: $fullVersion  (BuiltAt $builtAt)" -ForegroundColor Cyan
}
else {
    $fullVersion = $state.FullVersion
    Write-Host "==> Keeping version: $fullVersion" -ForegroundColor Cyan
}

$scFlag = if ($SelfContained) { "true" } else { "false" }

Write-Host "==> Publishing $Configuration -> $OutputDir" -ForegroundColor Cyan
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
    "/p:IncludeNativeLibrariesForSelfExtract=true"
)

if ($SelfContained) {
    $publishArgs += @("/p:EnableCompressionInSingleFile=true")
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed (exit $LASTEXITCODE)"
}

$projXml = Get-Content -LiteralPath $Project -Raw -Encoding UTF8
$exeName = if ($projXml -match '<AssemblyName>([^<]+)</AssemblyName>') {
    "$($Matches[1]).exe"
}
else {
    "Clipboard.exe"
}

$exePath = Join-Path $OutputDir $exeName
if (Test-Path -LiteralPath $exePath) {
    $sizeMb = [math]::Round((Get-Item -LiteralPath $exePath).Length / 1MB, 2)
    Write-Host "==> Done: $exePath ($sizeMb MB)  v$fullVersion" -ForegroundColor Green
}
else {
    $fallback = Get-ChildItem -LiteralPath $OutputDir -Filter *.exe -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($fallback) {
        $sizeMb = [math]::Round($fallback.Length / 1MB, 2)
        Write-Host "==> Done: $($fallback.FullName) ($sizeMb MB)  v$fullVersion" -ForegroundColor Green
    }
    else {
        Write-Host "==> Done: $OutputDir  v$fullVersion" -ForegroundColor Green
    }
}
