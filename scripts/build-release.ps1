param(
    [string]$DotnetExecutable = 'dotnet',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$publishRoot = Join-Path $artifactsRoot ("AMS2KRLeague-v$Version-win-x64")
$zipPath = "$publishRoot.zip"

if (Test-Path -LiteralPath $publishRoot) {
    throw "Release directory already exists: $publishRoot"
}
if (Test-Path -LiteralPath $zipPath) {
    throw "Release ZIP already exists: $zipPath"
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
& $DotnetExecutable publish (Join-Path $projectRoot 'src\AMS2LeagueClient\AMS2LeagueClient.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath
Write-Output "Release: $zipPath"
Write-Output "SHA256: $($hash.Hash)"
