#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife Client for Android and emits an APK.
.DESCRIPTION
    Publishes Alife.Client.Android (self-contained, android-arm64) and
    copies the produced APK to "$OutputDir\Alife.Client.Android.apk".
    Requires Android SDK (ANDROID_SDK_ROOT), JDK (JAVA_HOME),
    and Android NDK (ANDROID_NDK_ROOT) to be available.
.PARAMETER OutputDir
    Distribution root. The APK is emitted to "$OutputDir\Alife.Client.Android.apk".
.PARAMETER JavaSdkDirectory
    Path to the JDK. Defaults to the JAVA_HOME environment variable.
.PARAMETER AlifeAndroidClang
    Path to the NDK clang executable. Defaults to auto-detection from the NDK.
.EXAMPLE
    .\client-android.ps1
.EXAMPLE
    .\client-android.ps1 -OutputDir "C:\Releases\Alife"
#>

param(
    [string]$OutputDir = "",

    # Android 打包需要 JDK 与 NDK clang（编 sdk-launcher），默认读环境变量，可显式覆盖。
    [string]$JavaSdkDirectory = $env:JAVA_HOME,

    [string]$AlifeAndroidClang = $env:ALIFE_ANDROID_CLANG
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$Src = Join-Path $Root "sources"
$ClientRoot = Join-Path $Src "Alife.Client"
$ClientProject = Join-Path $ClientRoot "Alife.Client.Android\Alife.Client.Android.csproj"

if (-not (Test-Path -LiteralPath $ClientProject)) {
    throw "Android project not found: $ClientProject"
}

if (-not $OutputDir) {
    $OutputDir = Join-Path $Root "..\Shared\Alife\Outputs"
}

$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$ApkDest = Join-Path $OutputDir "Alife.Client.Android.apk"
$LegacyTarget = Join-Path $OutputDir "Alife.Client.Android"

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
Write-Host "[Alife] Publish Client (Android)" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Project:      $ClientProject"
Write-Host "[Alife] Distribution: $ApkDest"
if ($JavaSdkDirectory) { Write-Host "[Alife] Java SDK:     $JavaSdkDirectory" -ForegroundColor Green }
if ($AlifeAndroidClang) { Write-Host "[Alife] NDK clang:    $AlifeAndroidClang" -ForegroundColor Green }
Write-Host ""

# 环境快速检查
$missing = @()
if (-not (Test-Path "$env:ANDROID_SDK_ROOT")) { $missing += "ANDROID_SDK_ROOT ($env:ANDROID_SDK_ROOT)" }
if (-not (Test-Path $env:JAVA_HOME)) { $missing += "JAVA_HOME ($env:JAVA_HOME)" }
if ($missing) {
    throw "缺少 Android 构建环境:`n$($missing -join "`n")"
}

Write-Host "[1/2] Cleaning previous APK..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $LegacyTarget) {
    Remove-Item -LiteralPath $LegacyTarget -Recurse -Force
}
if (Test-Path -LiteralPath $ApkDest) {
    Remove-Item -LiteralPath $ApkDest -Force
}
Write-Host "  Cleaned: $ApkDest" -ForegroundColor Green
Write-Host ""

Write-Host "[2/2] Publishing Alife for Android..." -ForegroundColor Yellow

$PublishArguments = @("-c", "Release", "--nologo", "--verbosity", "minimal")
if ($JavaSdkDirectory) {
    $PublishArguments += @("-p:JavaSdkDirectory=$JavaSdkDirectory")
}
if ($AlifeAndroidClang) {
    $PublishArguments += @("-p:AlifeAndroidClang=$AlifeAndroidClang")
}
Invoke-DotnetPublish -Project $ClientProject -Arguments $PublishArguments

# 输出目录由根 Directory.Build.props 统一到 Outputs/Alife.Client.Android 下
$apk1 = "$Root\Outputs\Alife.Client.Android\Release\net10.0-android\android-arm64\com.alife.client-Signed.apk"
$apk2 = "$Root\Outputs\Alife.Client.Android\Release\net10.0-android\android-arm64\com.alife.client.apk"
$ApkPath = $null
if (Test-Path -LiteralPath $apk1) {
    $ApkPath = $apk1
}
elseif (Test-Path -LiteralPath $apk2) {
    $ApkPath = $apk2
}
if (-not $ApkPath) {
    $found = Get-ChildItem -LiteralPath (Join-Path $Root "Outputs\Alife.Client.Android") -Recurse -File -Filter "*.apk" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    if ($found) { $ApkPath = $found }
}
if (-not $ApkPath) {
    throw "APK was not produced under Outputs\Alife.Client.Android."
}

$ApkDir = Split-Path -Parent $ApkDest
if (-not (Test-Path -LiteralPath $ApkDir)) {
    New-Item -ItemType Directory -Path $ApkDir -Force | Out-Null
}
Copy-Item -LiteralPath $ApkPath -Destination $ApkDest -Force

$ApkSize = (Get-Item -LiteralPath $ApkDest).Length / 1MB
Write-Host "  APK: $ApkDest ($([math]::Round($ApkSize, 2)) MB)" -ForegroundColor Green
Write-Host ""

Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Client publish complete!" -ForegroundColor Green
Write-Host "  Platform: Android"
Write-Host "  Output:   $ApkDest"
Write-Host "===================================================" -ForegroundColor Green
