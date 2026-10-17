#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife Client for a platform.
.DESCRIPTION
    Resolves the platform project by convention, publishes it with dotnet publish
    and packages the distribution into "$OutputDir\Alife.Client".
    The distribution folder is always "Alife.Client" no matter what the project
    folder is called. The launcher executable keeps the project's own name
    (Windows -> "Alife.Client.Windows.exe").
.PARAMETER Platform
    Platform name. The project is looked up as
    "sources\Alife.Client\Alife.Client.<Platform>\Alife.Client.<Platform>.csproj",
    falling back to the legacy "Alife.Client.App.<Platform>" folder, for example
    Windows -> Alife.Client.Windows.
    Packaging strategy per platform:
      Windows -> self-contained dotnet publish (net10.0-windows), RID win-x64.
      Android -> dotnet publish with the Android build parameters.
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

    [string]$OutputDir = "",

    # Android 打包需要 JDK 与 NDK clang（编 sdk-launcher），默认读环境变量，可显式覆盖。
    [string]$JavaSdkDirectory = $env:JAVA_HOME,

    [string]$AlifeAndroidClang = $env:ALIFE_ANDROID_CLANG
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "sources"
$ClientRoot = Join-Path $Src "Alife.Client"
$StagingRoot = Join-Path $Root ".build-validation\Publish-Client"

# The distribution folder is always "Alife.Client"; the launcher keeps the
# platform project's assembly name (see the platform csproj).
$ProductName = "Alife.Client"

# Packaging strategy per platform.
$Platforms = @{
    "Windows" = @{
        RuntimeIdentifier = "win-x64"
        Launcher          = "Alife.Client.Windows.exe"
    }
    "Android" = @{
        RuntimeIdentifier = ""
        Launcher          = ""
    }
}

if (-not $Platforms.ContainsKey($Platform)) {
    throw "Unsupported platform '$Platform'. Supported platforms: $($Platforms.Keys -join ', ')."
}
$PlatformInfo = $Platforms[$Platform]

# Locate the platform host project by convention. It was renamed from
# "Alife.Client.App.<Platform>" to "Alife.Client.<Platform>", so try the
# current name first and only then fall back to the legacy one.
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

Write-Host "[2/2] Publishing $ProductName for $Platform..." -ForegroundColor Yellow

if (Test-Path -LiteralPath $StagingRoot) {
    Remove-Item -LiteralPath $StagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null

$ClientBuildOutput = Join-Path $StagingRoot $PlatformProjectName
$PublishArguments = @("-c", "Release")
if ($PlatformInfo.RuntimeIdentifier) {
    $PublishArguments += @("-r", $PlatformInfo.RuntimeIdentifier)
}
$PublishArguments += @("-p:OutputPath=$ClientBuildOutput\", "-nologo", "--verbosity", "minimal")
if ($JavaSdkDirectory) {
    $PublishArguments += @("-p:JavaSdkDirectory=$JavaSdkDirectory")
}
if ($AlifeAndroidClang) {
    $PublishArguments += @("-p:AlifeAndroidClang=$AlifeAndroidClang")
}
Invoke-DotnetPublish -Project $ClientProject -Arguments $PublishArguments

$PublishOutput = Find-PackageDirectory -Root $ClientBuildOutput -RelativePath "publish" -LeafName "publish"
if (-not $PublishOutput) {
    throw "Publish output directory was not found under $ClientBuildOutput (platform: $Platform)."
}

if ($PlatformInfo.Launcher) {
    $Launcher = Join-Path $PublishOutput $PlatformInfo.Launcher
    if (-not (Test-Path -LiteralPath $Launcher)) {
        throw "Publish output is missing the launcher '$($PlatformInfo.Launcher)' (platform: $Platform)."
    }
}

Get-ChildItem -LiteralPath $PublishOutput -Force | Copy-Item -Destination $ClientTarget -Recurse -Force

Write-Host "  Package: $ClientTarget" -ForegroundColor Green
Write-Host ""

Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Client publish complete!" -ForegroundColor Green
Write-Host "  Platform: $Platform"
Write-Host "  Output:   $ClientTarget"
Write-Host "===================================================" -ForegroundColor Green
