param(
    [Parameter(Mandatory)][string]$ServerJar,
    [Parameter(Mandatory)][string]$OutputJar
)

$ErrorActionPreference = 'Stop'
$server = (Resolve-Path -LiteralPath $ServerJar).Path
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputJar)
$classes = Join-Path ([System.IO.Path]::GetTempPath()) ("alife-mindustry-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $classes | Out-Null
try {
    $sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter *.java -File | ForEach-Object FullName)
    javac -encoding UTF-8 --release 17 -cp $server -d $classes @sources
    if ($LASTEXITCODE -ne 0) { throw 'Mindustry bridge compilation failed.' }

    jar cf $output -C $PSScriptRoot plugin.json -C $classes alife
    if ($LASTEXITCODE -ne 0) { throw 'Mindustry bridge packaging failed.' }
    Get-Item -LiteralPath $output
}
finally {
    $resolved = (Resolve-Path -LiteralPath $classes).Path
    $tempDirectory = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
    if ([System.IO.Path]::GetDirectoryName($resolved) -eq $tempDirectory -and
        [System.IO.Path]::GetFileName($resolved) -match '^alife-mindustry-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse
    }
}
