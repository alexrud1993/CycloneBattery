<#
.SYNOPSIS
    Runs the automated test suite.

.DESCRIPTION
    The tests are hardware-free: they cover the protocol parser, interface probing, state machine,
    alert hysteresis, settings persistence, autostart entry generation and diagnostics formatting.
    They do NOT prove the real controller works — see docs\HARDWARE_TEST.md for that.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.EXAMPLE
    .\scripts\test.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0"
}

Write-Host "==> Restore" -ForegroundColor Cyan
dotnet restore CycloneBattery.sln
if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE" }

Write-Host "==> Test ($Configuration)" -ForegroundColor Cyan
dotnet test CycloneBattery.sln -c $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host "TESTS PASSED ($Configuration)" -ForegroundColor Green
Write-Host 'Note: real GameSir Cyclone 2 validation is a separate step (docs\HARDWARE_TEST.md).'
exit 0
