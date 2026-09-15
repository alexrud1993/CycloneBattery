<#
.SYNOPSIS
    Produces the release CycloneBattery.exe for Windows x64.

.DESCRIPTION
    Publishes self-contained, single-file, untrimmed, so the end user needs no .NET runtime
    installed. If single-file packaging ever causes a reproducible problem on the target machine,
    re-run with -FolderMode to produce a self-contained folder release instead (and record why).

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER FolderMode
    Publish a self-contained folder instead of a single file.

.EXAMPLE
    .\scripts\publish-win-x64.ps1
    .\scripts\publish-win-x64.ps1 -FolderMode
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $FolderMode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0"
}

$project = Join-Path $repositoryRoot 'src\CycloneBattery.App\CycloneBattery.App.csproj'
if (-not (Test-Path $project)) { throw "Project not found: $project" }

$publishProperties = @(
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishTrimmed=false'
)

if ($FolderMode) {
    $publishProperties += '-p:PublishSingleFile=false'
}
else {
    $publishProperties += '-p:PublishSingleFile=true'
    $publishProperties += '-p:IncludeNativeLibrariesForSelfExtract=true'
}

Write-Host "==> Restore" -ForegroundColor Cyan
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE" }

Write-Host "==> Publish (win-x64, self-contained, single-file=$(-not $FolderMode))" -ForegroundColor Cyan
dotnet publish $project @publishProperties
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }

$publishDirectory = Join-Path $repositoryRoot "src\CycloneBattery.App\bin\$Configuration\net10.0-windows\win-x64\publish"
$executable = Join-Path $publishDirectory 'CycloneBattery.exe'

if (Test-Path $executable) {
    $sizeMb = [math]::Round((Get-Item $executable).Length / 1MB, 1)
    Write-Host ''
    Write-Host "PUBLISH OK" -ForegroundColor Green
    Write-Host "Executable : $executable"
    Write-Host "Size       : $sizeMb MB"
    Write-Host ''
    Write-Host 'Next: run the real-hardware checks in docs\HARDWARE_TEST.md before distributing this file.'
    exit 0
}

Write-Warning "Publish reported success but the executable was not found at $executable."
Write-Warning "Inspect the publish output above for the actual location."
exit 1
