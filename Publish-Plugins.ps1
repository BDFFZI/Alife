#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Alife plugins.
.DESCRIPTION
    Builds all Function plugins, and refreshes source-based plugins for
    client-side builds.
.PARAMETER OutputDir
    Distribution root. Plugins are emitted to "$OutputDir\..\Plugins".
.EXAMPLE
    .\Publish-Plugins.ps1
.EXAMPLE
    .\Publish-Plugins.ps1 -OutputDir "C:\Releases\Alife"
#>

param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "sources"
$PluginBuildRoot = Join-Path $Root ".build-validation\Publish-PluginBuild"

if (-not $OutputDir) {
    $OutputDir = Join-Path $Root "..\Shared\Alife\Outputs"
}

$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$PluginTarget = Join-Path (Split-Path $OutputDir -Parent) "Plugins"

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
Write-Host "[Alife] Publish Plugins" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Plugins: $PluginTarget"
Write-Host ""

Write-Host "[1/2] Building plugin sources..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $PluginBuildRoot) {
    Remove-Item -LiteralPath $PluginBuildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $PluginBuildRoot -Force | Out-Null

$functionDirs = Get-ChildItem (Join-Path $Src "Alife.Function") -Directory |
    Where-Object {
        $_.Name -match '^(Alife\.Function\.|BDFFZI\.)' -and
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

Write-Host "[2/2] Refreshing source-based plugins..." -ForegroundColor Yellow
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
    $generatorDir = Join-Path $generatedDir "Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator"
    if (Test-Path -LiteralPath $generatorDir) {
        Get-ChildItem $generatorDir -Filter "*_razor.g.cs" -Recurse -File |
            ForEach-Object {
                $relativePath = $_.FullName.Substring($generatorDir.Length).TrimStart('\')
                $razorName = $_.Name -replace '_razor\.g\.cs$', ''
                $razorSubDir = Split-Path $relativePath -Parent
                $razorBase = Join-Path $dir.FullName $razorSubDir
                $razorFile = Join-Path $razorBase "$razorName.razor"
                if (Test-Path -LiteralPath $razorFile) {
                    $destFile = Join-Path $target $relativePath
                    $destSubDir = Split-Path $destFile -Parent
                    if (-not (Test-Path -LiteralPath $destSubDir)) {
                        New-Item -ItemType Directory -Path $destSubDir -Force | Out-Null
                    }
                    Copy-Item -LiteralPath $_.FullName -Destination $destFile -Force
                } else {
                    Write-Host "  [skip] $($_.Name)"
                }
            }
    }

    $manifestFile = Join-Path $dir.FullName "manifest.json"
    if (Test-Path -LiteralPath $manifestFile) {
        Copy-Item -LiteralPath $manifestFile -Destination $target -Force
    }

    # 复制插件项目中的 Resources 内容文件夹（网页资源等随插件分发）
    $resourcesDir = Join-Path $dir.FullName "Resources"
    if (Test-Path -LiteralPath $resourcesDir) {
        $resourcesTarget = Join-Path $target "Resources"
        if (Test-Path -LiteralPath $resourcesTarget) {
            Remove-Item -LiteralPath $resourcesTarget -Recurse -Force
        }
        Copy-Item -LiteralPath $resourcesDir -Destination $target -Recurse -Force
        Write-Host "  [resources] $($dir.Name)\Resources" -ForegroundColor Green
    }

    Write-Host "  [done] $($dir.Name)" -ForegroundColor Green
}

Write-Host ""
Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Plugins publish complete!" -ForegroundColor Green
Write-Host "  Plugins: $PluginTarget"
Write-Host "===================================================" -ForegroundColor Green
