#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife Windows distribution (Client + Plugins).
.DESCRIPTION
    Runs Publish-Client.ps1 and Publish-Plugins.ps1 in sequence.
.PARAMETER OutputDir
    Distribution root.
.EXAMPLE
    .\Publish.ps1
.EXAMPLE
    .\Publish.ps1 -OutputDir "C:\Releases\Alife"
#>

param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Full Publish (Client + Plugins)" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

if ($OutputDir) {
    & "$Root\Publish-Client.ps1" -OutputDir $OutputDir
    & "$Root\Publish-Plugins.ps1" -OutputDir $OutputDir
} else {
    & "$Root\Publish-Client.ps1"
    & "$Root\Publish-Plugins.ps1"
}

Write-Host ""
Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Full publish complete!" -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green
