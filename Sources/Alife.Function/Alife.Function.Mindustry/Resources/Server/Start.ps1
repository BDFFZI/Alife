param(
    [Parameter(Mandatory)][string]$ServerJar,
    [ValidateRange(1,65535)][int]$Port = 18768,
    [ValidateRange(1,65535)][int]$GamePort = 6567,
    [ValidateRange(1,255)][int]$Team,
    [ValidatePattern('^[\p{L}\p{N}_-]+$')][string]$Map = 'Triad'
)

$ErrorActionPreference = 'Stop'
if ($Port -eq $GamePort) { throw 'Bridge and game ports must be different.' }
foreach ($candidate in $Port, $GamePort) {
    if (Get-NetTCPConnection -State Listen -LocalPort $candidate -ErrorAction SilentlyContinue) {
        throw "TCP port $candidate is occupied. Pass -Port or -GamePort to select a free port."
    }
}
if (Get-NetUDPEndpoint -LocalPort $GamePort -ErrorAction SilentlyContinue) {
    throw "UDP port $GamePort is occupied. Pass -GamePort to select a free port."
}
$game = Get-Item -LiteralPath $ServerJar
$java = Get-Command java -CommandType Application | Select-Object -First 1
$bridge = Get-Item -LiteralPath (Join-Path $PSScriptRoot 'alife-mindustry.jar')
$mods = Join-Path $game.DirectoryName 'config/mods'
New-Item -ItemType Directory -Path $mods -Force | Out-Null
Copy-Item -LiteralPath $bridge.FullName -Destination $mods -Force

$tokenFile = Join-Path $game.DirectoryName 'config/alife-mindustry-token.txt'
if (Test-Path -LiteralPath $tokenFile) {
    $token = (Get-Content -LiteralPath $tokenFile -Raw).Trim()
} else {
    $bytes = New-Object byte[] 24
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    $token = [BitConverter]::ToString($bytes).Replace('-', '')
    Set-Content -LiteralPath $tokenFile -Value $token -Encoding ascii -NoNewline
}
if ($token -notmatch '^[A-Za-z0-9_-]{24,128}$') { throw "Invalid token file: $tokenFile" }
$propertiesFile = Join-Path $game.DirectoryName 'config/alife-mindustry.properties'
$configuredTeam = if ($PSBoundParameters.ContainsKey('Team')) { $Team } else { 0 }
Set-Content -LiteralPath $propertiesFile `
    -Value "# Alife Mindustry bridge. This file is managed by the Alife module.`nport=$Port`ntoken=$token`nteam=$configuredTeam" `
    -Encoding ascii -NoNewline

$mode = if ($PSBoundParameters.ContainsKey('Team')) { 'pvp' } else { 'survival' }
$oldToken = $env:ALIFE_MINDUSTRY_TOKEN
$oldPort = $env:ALIFE_MINDUSTRY_PORT
$oldTeam = $env:ALIFE_MINDUSTRY_TEAM
Push-Location -LiteralPath $game.DirectoryName
try {
    $env:ALIFE_MINDUSTRY_TOKEN = $token
    $env:ALIFE_MINDUSTRY_PORT = "$Port"
    $env:ALIFE_MINDUSTRY_TEAM = if ($mode -eq 'pvp') { "$Team" } else { $null }
    Write-Host "Alife module port: $Port"
    Write-Host "Mindustry local game address: 127.0.0.1:${GamePort}"
    Write-Host "Copy the token from: $tokenFile"
    & $java.Source -jar $game.FullName "config port $GamePort,host $Map $mode"
    if ($LASTEXITCODE -ne 0) { throw "Mindustry server exited with code $LASTEXITCODE." }
} finally {
    Pop-Location
    $env:ALIFE_MINDUSTRY_TOKEN = $oldToken
    $env:ALIFE_MINDUSTRY_PORT = $oldPort
    $env:ALIFE_MINDUSTRY_TEAM = $oldTeam
}
