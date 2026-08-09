#Requires -Version 5.1
<#
.SYNOPSIS
  缂栬瘧骞跺彂甯冦€岀啍宀╄秴绾у壀璐存澘銆嶅埌 publish 鐩綍銆?
.DESCRIPTION
  榛樿浼氶€掑 BuildNumber锛屽苟鍚屾鏇存柊 VersionInfo.cs 涓?Clipboard.csproj 涓殑鐗堟湰瀛楁銆?
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
    throw "鏈壘鍒伴」鐩枃浠? $Project"
}
if (-not (Test-Path $VersionInfoPath)) {
    throw "鏈壘鍒扮増鏈枃浠? $VersionInfoPath"
}

function Get-VersionState {
    $cs = Get-Content -LiteralPath $VersionInfoPath -Raw -Encoding UTF8

    if ($cs -notmatch 'public const string Version = "([^"]+)";') {
        throw "鏃犳硶浠?VersionInfo.cs 瑙ｆ瀽 Version"
    }
    $version = $Matches[1]

    if ($cs -notmatch 'public const int BuildNumber = (\d+);') {
        throw "鏃犳硶浠?VersionInfo.cs 瑙ｆ瀽 BuildNumber"
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
    Write-Host "==> 鐗堟湰閫掑: $fullVersion  (BuiltAt $builtAt)" -ForegroundColor Cyan
}
else {
    $fullVersion = $state.FullVersion
    Write-Host "==> 淇濇寔鐗堟湰: $fullVersion" -ForegroundColor Cyan
}

$scFlag = if ($SelfContained) { "true" } else { "false" }

Write-Host "==> 鍙戝竷 Release -> $OutputDir" -ForegroundColor Cyan
Write-Host "    Runtime=$Runtime  SelfContained=$scFlag" -ForegroundColor DarkGray

$publishArgs = @(
    "publish", $Project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $scFlag,
    "-o", $OutputDir,
    "/p:PublishSingleFile=false"
)

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "鍙戝竷澶辫触 (exit $LASTEXITCODE)"
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
    Write-Host "==> 瀹屾垚: $exePath ($sizeMb MB)  v$fullVersion" -ForegroundColor Green
}
else {
    $fallback = Get-ChildItem -LiteralPath $OutputDir -Filter *.exe -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($fallback) {
        $sizeMb = [math]::Round($fallback.Length / 1MB, 2)
        Write-Host "==> 瀹屾垚: $($fallback.FullName) ($sizeMb MB)  v$fullVersion" -ForegroundColor Green
    }
    else {
        Write-Host "==> 瀹屾垚: $OutputDir  v$fullVersion" -ForegroundColor Green
    }
}
