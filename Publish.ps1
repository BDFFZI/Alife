#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife distribution (Client + Plugins).
.DESCRIPTION
    Runs Publish-Client.ps1 and Publish-Plugins.ps1 in sequence.
.PARAMETER OutputDir
    Distribution root.
.PARAMETER Platform
    Client platform name, e.g. Windows (default) or Android. Publish-Client.ps1
    resolves the matching "Alife.Client.App.<Platform>" project. Plugins are
    RID-agnostic .NET assemblies and keep the repository default runtime identifier.
.EXAMPLE
    .\Publish.ps1
.EXAMPLE
    .\Publish.ps1 -Platform Android
.EXAMPLE
    .\Publish.ps1 -OutputDir "C:\Releases\Alife"
#>

param(
    [string]$OutputDir = "",
    [string]$Platform = "Windows"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Full Publish (Client + Plugins)" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

& "$Root\Publish-Client.ps1" -Platform $Platform -OutputDir $OutputDir
& "$Root\Publish-Plugins.ps1" -OutputDir $OutputDir

Write-Host ""
Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Full publish complete!" -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green
