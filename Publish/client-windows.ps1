#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife Client for Windows and emits an Inno Setup installer.
.DESCRIPTION
    Publishes Alife.Client (Windows, self-contained, win-x64) to
    "$OutputDir\Alife.Client", then compiles an Inno Setup installer
    that detects .NET SDK 10.0 and installs it online when missing.
.PARAMETER OutputDir
    Distribution root. The client is emitted to "$OutputDir\Alife.Client"
    and the installer is written to "$OutputDir".
.EXAMPLE
    .\client-windows.ps1
.EXAMPLE
    .\client-windows.ps1 -OutputDir "C:\Releases\Alife"
#>

param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$Src = Join-Path $Root "sources"
$ClientRoot = Join-Path $Src "Alife.Client"
$StagingRoot = Join-Path $Root ".build-validation\Publish-Client"

$ProductName = "Alife.Client"

$Platform = "Windows"
$PlatformInfo = @{
    RuntimeIdentifier = "win-x64"
    Launcher          = "Alife.Client.Windows.exe"
}

$ClientProject = ""
$PlatformProjectName = ""
foreach ($candidate in @("Alife.Client.$Platform", "Alife.Client.App.$Platform")) {
    $candidateProject = Join-Path $ClientRoot "$candidate\$candidate.csproj"
    if (Test-Path -LiteralPath $candidateProject) {
        $ClientProject = $candidateProject
        $PlatformProjectName = $candidate
        break
    }
}
if (-not $ClientProject) {
    $existingProjects = @(Get-ChildItem -LiteralPath $ClientRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "Alife.Client.*" } |
        ForEach-Object { $_.Name })
    $existingText = "none"
    if ($existingProjects.Count -gt 0) {
        $existingText = $existingProjects -join ", "
    }
    throw "Platform project not found for '$Platform' under $ClientRoot (project folders present: $existingText)."
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
Write-Host "[Alife] Publish Client (Windows)" -ForegroundColor Cyan
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

Write-Host "[2/2] Publishing $ProductName for $Platform..." -ForegroundColor Yellow

if (Test-Path -LiteralPath $StagingRoot) {
    Remove-Item -LiteralPath $StagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null

$ClientBuildOutput = Join-Path $StagingRoot $PlatformProjectName
$PublishArguments = @("-c", "Release", "-r", $PlatformInfo.RuntimeIdentifier)
$PublishArguments += @("-p:OutputPath=$ClientBuildOutput\", "-nologo", "--verbosity", "minimal")
Invoke-DotnetPublish -Project $ClientProject -Arguments $PublishArguments

$PublishOutput = Find-PackageDirectory -Root $ClientBuildOutput -RelativePath "publish" -LeafName "publish"
if (-not $PublishOutput) {
    throw "Publish output directory was not found under $ClientBuildOutput (platform: $Platform)."
}

$Launcher = Join-Path $PublishOutput $PlatformInfo.Launcher
if (-not (Test-Path -LiteralPath $Launcher)) {
    throw "Publish output is missing the launcher '$($PlatformInfo.Launcher)' (platform: $Platform)."
}

Get-ChildItem -LiteralPath $PublishOutput -Force | Copy-Item -Destination $ClientTarget -Recurse -Force

Write-Host "  Package: $ClientTarget" -ForegroundColor Green
Write-Host ""

Write-Host "[3/3] Compiling Inno Setup installer..." -ForegroundColor Yellow

$issFile = Join-Path $PSScriptRoot "client-windows-setup.iss"
if (-not (Test-Path -LiteralPath $issFile)) {
    throw "Inno Setup script not found: $issFile"
}

$csprojFile = $ClientProject
$versionLine = Select-String -Path $csprojFile -Pattern '<Version>(.*?)</Version>' | Select-Object -First 1
if (-not $versionLine) {
    throw "Cannot read <Version> from $csprojFile"
}
$appVersion = $versionLine.Matches[0].Groups[1].Value

$isccCandidates = @(
    "C:\Program Files\Inno Setup 7\iscc.exe",
    "C:\Program Files (x86)\Inno Setup 7\iscc.exe",
    "C:\Program Files\Inno Setup 6\iscc.exe",
    "C:\Program Files (x86)\Inno Setup 6\iscc.exe"
)

$iscc = $null
$isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($isccCommand) {
    $iscc = $isccCommand.Source
}
if (-not $iscc) {
    foreach ($uninstallRoot in @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    )) {
        Get-ChildItem $uninstallRoot -ErrorAction SilentlyContinue | ForEach-Object {
            $entry = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            if (-not $iscc -and $entry.DisplayName -like "Inno Setup*" -and $entry.InstallLocation) {
                $candidate = Join-Path $entry.InstallLocation "ISCC.exe"
                if (Test-Path -LiteralPath $candidate) { $iscc = $candidate }
            }
        }
    }
}
if (-not $iscc) {
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $iscc) {
    throw "Inno Setup not found. Install Inno Setup 7 or add iscc.exe to PATH."
}

Write-Host "  Inno Setup found: $iscc" -ForegroundColor Green
Write-Host "  App version:       $appVersion" -ForegroundColor Green

$installerPath = Join-Path $OutputDir "Alife.Client.Windows.exe"
if (Test-Path -LiteralPath $installerPath) { Remove-Item -LiteralPath $installerPath -Force }

& $iscc $issFile "--define=MyAppVersion=$appVersion" "--define=MyOutputDir=$OutputDir" "--define=MySourceDir=$ClientTarget"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) {
    throw "Inno Setup failed to produce $installerPath (exit code $LASTEXITCODE)."
}
$installerSize = (Get-Item -LiteralPath $installerPath).Length / 1MB
Write-Host "  Installer: $installerPath ($([math]::Round($installerSize, 2)) MB)" -ForegroundColor Green

Write-Host ""
Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Client publish complete!" -ForegroundColor Green
Write-Host "  Platform: $Platform"
Write-Host "  Output:   $ClientTarget"
Write-Host "  Installer: $installerPath"
Write-Host "===================================================" -ForegroundColor Green
