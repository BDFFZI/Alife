#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife Windows distribution.
.DESCRIPTION
    Packages Alife.Client as an elevated, branded Electron directory, publishes
    DeskPet, then refreshes source-based Function plugins for client-side builds.
.PARAMETER OutputDir
    Distribution root. Alife.Client is emitted to "$OutputDir\Alife.Client".
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
$Src = Join-Path $Root "sources"
$ClientProject = Join-Path $Src "Alife\Alife.Client\Alife.Client.csproj"
$ElectronStagingRoot = Join-Path $Root ".build-validation\Publish-Electron"
$PluginBuildRoot = Join-Path $Root ".build-validation\Publish-PluginBuild"

if (-not $OutputDir) {
    $OutputDir = Join-Path $Root "..\Shared\Alife\Outputs"
}

$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$PluginTarget = Join-Path (Split-Path $OutputDir -Parent) "Plugins"
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

function Invoke-DotnetBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Project,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet build $Project @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $Project with exit code $LASTEXITCODE."
    }
}

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Publish Mode" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Distribution: $OutputDir"
Write-Host "[Alife] Plugins:      $PluginTarget"
Write-Host ""

Write-Host "[0/3] Cleaning distribution directory..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Write-Host "  Cleaned: $OutputDir" -ForegroundColor Green
Write-Host ""

Write-Host "[1/3] Packaging Alife.Client with Electron..." -ForegroundColor Yellow
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

New-Item -ItemType Directory -Path $ClientTarget -Force | Out-Null
Get-ChildItem -LiteralPath $ElectronPackage -Force | Copy-Item -Destination $ClientTarget -Recurse -Force

$safeModeScript = Join-Path $ClientTarget "Alife.Client.exe (SafeMode).cmd"
@"
@echo off
Alife.Client.exe --no-sandbox --disable-gpu
"@ | Set-Content -Path $safeModeScript -Encoding ASCII

Write-Host "  Electron package: $ClientTarget" -ForegroundColor Green
Write-Host ""

Write-Host "[2/3] Publishing DeskPet and building plugin sources..." -ForegroundColor Yellow
Invoke-DotnetPublish -Project (Join-Path $Src "Alife.DeskPet\Alife.DeskPet.Client\Alife.DeskPet.Client.csproj") -Arguments @(
    "-c", "Release",
    "-o", (Join-Path $OutputDir "Alife.DeskPet.Client"),
    "-nologo",
    "--verbosity", "quiet"
)

if (Test-Path -LiteralPath $PluginBuildRoot) {
    Remove-Item -LiteralPath $PluginBuildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $PluginBuildRoot -Force | Out-Null
$functionDirs = Get-ChildItem (Join-Path $Src "Alife.Function") -Directory |
    Where-Object {
        $_.Name -match '^Alife\.Function\.' -and
        (Test-Path -LiteralPath (Join-Path $_.FullName "$($_.Name).csproj"))
    }
foreach ($dir in $functionDirs) {
    $csproj = Join-Path $dir.FullName "$($dir.Name).csproj"
    $pluginBuildOutput = Join-Path $PluginBuildRoot $dir.Name
    Invoke-DotnetBuild -Project $csproj -Arguments @(
        "-c", "Release",
        "-p:OutputPath=$pluginBuildOutput\",
        "-nologo",
        "--verbosity", "quiet"
    )
}
Write-Host ""

Write-Host "[3/3] Refreshing source-based plugins..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $PluginTarget) {
    Remove-Item -LiteralPath $PluginTarget -Recurse -Force
}
New-Item -ItemType Directory -Path $PluginTarget -Force | Out-Null

foreach ($dir in $functionDirs) {
    $target = Join-Path $PluginTarget $dir.Name
    New-Item -ItemType Directory -Path $target -Force | Out-Null

    Get-ChildItem $dir.FullName -Filter "*.cs" -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\obj\\' } |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($dir.FullName.Length + 1)
            $destFile = Join-Path $target $relativePath
            $destDir = Split-Path $destFile -Parent
            if (-not (Test-Path -LiteralPath $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            Copy-Item -LiteralPath $_.FullName -Destination $destFile -Force
        }

    $generatedDir = Join-Path $dir.FullName "obj\Release\generated\Microsoft.CodeAnalysis.Razor.Compiler"
    if (Test-Path -LiteralPath $generatedDir) {
        Get-ChildItem $generatedDir -Filter "*_razor.g.cs" -Recurse -File |
            ForEach-Object {
                $razorName = $_.Name -replace '_razor\.g\.cs$', ''
                $razorFile = Join-Path $dir.FullName "$razorName.razor"
                if (Test-Path -LiteralPath $razorFile) {
                    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
                } else {
                    Write-Host "  [skip] $($_.Name)"
                }
            }
    }

    Write-Host "  [done] $($dir.Name)" -ForegroundColor Green
}

Write-Host ""
Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Publish complete!" -ForegroundColor Green
Write-Host "  Electron app: $ClientTarget"
Write-Host "  Plugins:      $PluginTarget"
Write-Host "===================================================" -ForegroundColor Green
