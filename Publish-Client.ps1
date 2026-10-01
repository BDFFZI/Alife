#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife Client for a platform.
.DESCRIPTION
    Resolves the platform project by convention ("Alife.Client.App.<Platform>"),
    publishes it and packages the distribution into "$OutputDir\Alife.Client".
    The product name is always "Alife.Client" ("Alife.Client.exe" on Windows,
    "Alife.Client.app" on macOS) no matter what the project folder is called.
.PARAMETER Platform
    Platform name. The project is looked up as
    "sources\Alife.Client\Alife.Client.App.<Platform>\Alife.Client.App.<Platform>.csproj",
    for example Windows -> Alife.Client.App.Windows.
    Packaging strategy per platform:
      Windows -> Electron package ("win-unpacked"), RID win-x64.
      Android -> plain dotnet publish (Electron is not involved).
.PARAMETER OutputDir
    Distribution root. The client is emitted to "$OutputDir\Alife.Client".
.EXAMPLE
    .\Publish-Client.ps1
.EXAMPLE
    .\Publish-Client.ps1 -Platform Android
.EXAMPLE
    .\Publish-Client.ps1 -Platform Windows -OutputDir "C:\Releases\Alife"
#>

param(
    [string]$Platform = "Windows",

    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "sources"
$ClientRoot = Join-Path $Src "Alife.Client"
$ElectronStagingRoot = Join-Path $Root ".build-validation\Publish-Electron"

# The product name comes from the platform project (Title/AssemblyName in
# Alife.Client.App.Windows.csproj), not from the project folder name.
$ProductName = "Alife.Client"

# Packaging strategy per platform.
$Platforms = @{
    "Windows" = @{
        RuntimeIdentifier = "win-x64"
        Electron          = $true
        UnpackedDir       = "win-unpacked"
        Launcher          = "$ProductName.exe"
    }
    "Android" = @{
        RuntimeIdentifier = ""
        Electron          = $false
        UnpackedDir       = ""
        Launcher          = ""
    }
}

if (-not $Platforms.ContainsKey($Platform)) {
    throw "Unsupported platform '$Platform'. Supported platforms: $($Platforms.Keys -join ', ')."
}
$PlatformInfo = $Platforms[$Platform]

# Locate the platform project by its suffix: Alife.Client.App.<Platform>
$PlatformProjectName = "Alife.Client.App.$Platform"
$ClientProject = Join-Path $ClientRoot "$PlatformProjectName\$PlatformProjectName.csproj"
if (-not (Test-Path -LiteralPath $ClientProject)) {
    $existingProjects = @(Get-ChildItem -LiteralPath $ClientRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "Alife.Client.App.*" } |
        ForEach-Object { $_.Name.Substring("Alife.Client.App.".Length) })
    $existingText = "none"
    if ($existingProjects.Count -gt 0) {
        $existingText = $existingProjects -join ", "
    }
    throw "Platform project not found: $ClientProject (platforms that have a project: $existingText)."
}

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

function Find-PackageDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [Parameter(Mandatory = $true)]
        [string]$LeafName
    )

    # Preferred layout first, then fall back to a search (RID-qualified publish layouts).
    $preferred = Join-Path $Root $RelativePath
    if (Test-Path -LiteralPath $preferred) {
        return $preferred
    }

    $found = Get-ChildItem -LiteralPath $Root -Recurse -Directory -Filter $LeafName -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if ($found) {
        return $found
    }

    return ""
}

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Publish Client" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Platform:     $Platform ($PlatformProjectName)"
Write-Host "[Alife] Distribution: $OutputDir"
Write-Host ""

Write-Host "[1/2] Cleaning distribution directory..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $ClientTarget) {
    Remove-Item -LiteralPath $ClientTarget -Recurse -Force
}
New-Item -ItemType Directory -Path $ClientTarget -Force | Out-Null
Write-Host "  Cleaned: $ClientTarget" -ForegroundColor Green
Write-Host ""

if ($PlatformInfo.Electron) {
    Write-Host "[2/2] Packaging $ProductName with Electron ($Platform)..." -ForegroundColor Yellow
} else {
    Write-Host "[2/2] Publishing $ProductName for $Platform..." -ForegroundColor Yellow
}

if (Test-Path -LiteralPath $ElectronStagingRoot) {
    Remove-Item -LiteralPath $ElectronStagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ElectronStagingRoot -Force | Out-Null

$ClientBuildOutput = Join-Path $ElectronStagingRoot $PlatformProjectName
$PublishArguments = @("-c", "Release")
if ($PlatformInfo.RuntimeIdentifier) {
    $PublishArguments += @("-r", $PlatformInfo.RuntimeIdentifier)
}
$PublishArguments += @("-p:OutputPath=$ClientBuildOutput\", "-nologo", "--verbosity", "minimal")
Invoke-DotnetPublish -Project $ClientProject -Arguments $PublishArguments

if ($PlatformInfo.Electron) {
    $ElectronPackage = Find-PackageDirectory -Root $ClientBuildOutput -RelativePath "publish\$($PlatformInfo.UnpackedDir)" -LeafName $PlatformInfo.UnpackedDir
    if (-not $ElectronPackage) {
        throw "Electron package directory '$($PlatformInfo.UnpackedDir)' was not found under $ClientBuildOutput (platform: $Platform)."
    }

    $ElectronLauncher = Join-Path $ElectronPackage $PlatformInfo.Launcher
    if (-not (Test-Path -LiteralPath $ElectronLauncher)) {
        throw "Electron package was not created: '$ElectronLauncher' is missing (platform: $Platform)."
    }

    Get-ChildItem -LiteralPath $ElectronPackage -Force | Copy-Item -Destination $ClientTarget -Recurse -Force

    # Safe mode launcher (Windows only).
    $safeModeScript = Join-Path $ClientTarget "$ProductName.exe (DisableGPU).cmd"
    @"
@echo off
chcp 65001 >nul
$ProductName.exe --no-sandbox --disable-gpu
pause
"@ | Set-Content -Path $safeModeScript -Encoding ASCII
} else {
    $PublishOutput = Find-PackageDirectory -Root $ClientBuildOutput -RelativePath "publish" -LeafName "publish"
    if (-not $PublishOutput) {
        throw "Publish output directory was not found under $ClientBuildOutput (platform: $Platform)."
    }

    Get-ChildItem -LiteralPath $PublishOutput -Force | Copy-Item -Destination $ClientTarget -Recurse -Force
}

Write-Host "  Package: $ClientTarget" -ForegroundColor Green
Write-Host ""

Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Client publish complete!" -ForegroundColor Green
Write-Host "  Platform: $Platform"
Write-Host "  Output:   $ClientTarget"
Write-Host "===================================================" -ForegroundColor Green
