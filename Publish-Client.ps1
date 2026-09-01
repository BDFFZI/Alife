#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife Client (Electron).
.DESCRIPTION
    Packages Alife.Client as an elevated, branded Electron directory.
.PARAMETER OutputDir
    Distribution root. Alife.Client is emitted to "$OutputDir\Alife.Client".
.EXAMPLE
    .\Publish-Client.ps1
.EXAMPLE
    .\Publish-Client.ps1 -OutputDir "C:\Releases\Alife"
#>

param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "sources"
$ClientProject = Join-Path $Src "Alife\Alife.Client\Alife.Client.csproj"
$ElectronStagingRoot = Join-Path $Root ".build-validation\Publish-Electron"

if (-not $OutputDir) {
    $OutputDir = Join-Path $Root "..\Shared\Alife\Outputs"
}

$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$ClientTarget = Join-Path $OutputDir "Alife.Client"

function Invoke-DotnetPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Project,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet publish $Project @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Project with exit code $LASTEXITCODE."
    }
}

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Publish Client" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Distribution: $OutputDir"
Write-Host ""

Write-Host "[1/2] Cleaning distribution directory..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $ClientTarget) {
    Remove-Item -LiteralPath $ClientTarget -Recurse -Force
}
New-Item -ItemType Directory -Path $ClientTarget -Force | Out-Null
Write-Host "  Cleaned: $ClientTarget" -ForegroundColor Green
Write-Host ""

Write-Host "[2/2] Packaging Alife.Client with Electron..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $ElectronStagingRoot) {
    Remove-Item -LiteralPath $ElectronStagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ElectronStagingRoot -Force | Out-Null

$ClientBuildOutput = Join-Path $ElectronStagingRoot "Alife.Client"
Invoke-DotnetPublish -Project $ClientProject -Arguments @(
    "-c", "Release",
    "-r", "win-x64",
    "-p:OutputPath=$ClientBuildOutput\",
    "-nologo",
    "--verbosity", "minimal"
)

$ElectronPackage = Join-Path $ClientBuildOutput "publish\win-unpacked"
if (-not (Test-Path -LiteralPath (Join-Path $ElectronPackage "Alife.Client.exe"))) {
    throw "Electron package was not created at $ElectronPackage."
}

Get-ChildItem -LiteralPath $ElectronPackage -Force | Copy-Item -Destination $ClientTarget -Recurse -Force

Write-Host "  Electron package: $ClientTarget" -ForegroundColor Green
Write-Host ""

Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Client publish complete!" -ForegroundColor Green
Write-Host "  Output: $ClientTarget"
Write-Host "===================================================" -ForegroundColor Green
