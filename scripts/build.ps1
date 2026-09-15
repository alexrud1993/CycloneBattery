<#
.SYNOPSIS
    Restores and builds the Cyclone Battery solution.

.DESCRIPTION
    Runs from any working directory: the script always resolves the repository root from its own
    location. Fails loudly (non-zero exit code) on the first error.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.EXAMPLE
    .\scripts\build.ps1
    .\scripts\build.ps1 -Configuration Debug
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

function Invoke-Step {
    param([string] $Name, [string[]] $Arguments)

    Write-Host "==> $Name" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found on PATH. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0"
}

Write-Host "Repository root: $repositoryRoot"
Write-Host "Configuration  : $Configuration"
dotnet --version

Invoke-Step 'Restore' @('restore', 'CycloneBattery.sln')
Invoke-Step 'Build'   @('build', 'CycloneBattery.sln', '-c', $Configuration, '--no-restore')

Write-Host ''
Write-Host "BUILD OK ($Configuration)" -ForegroundColor Green
exit 0
