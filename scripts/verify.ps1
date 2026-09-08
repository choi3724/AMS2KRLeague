param([string]$DotnetExecutable = 'dotnet', [string]$EvidenceDirectory = 'work/validation-0.4.1', [switch]$CaptureLayouts)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'work/dotnet-home'
$parseTokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile((Join-Path (Get-Location) 'src/AMS2LeagueClient/Runtime/ApplyUpdate.ps1'), [ref]$parseTokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -gt 0) { $parseErrors; exit 1 }
& $DotnetExecutable build AMS2KRLeague.sln -c Release --ignore-failed-sources 2>&1 | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'build.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$clientArgs = @('run', '--project', 'tests/AMS2LeagueClient.Tests', '-c', 'Release', '--no-build')
if ($CaptureLayouts) { $clientArgs += @('--', '--capture-layout', (Join-Path $EvidenceDirectory 'after')) }
& $DotnetExecutable @clientArgs 2>&1 | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'client.log')
$clientExit = $LASTEXITCODE
& $DotnetExecutable run --project tests/AMS2LeagueActivity.Tests -c Release --no-build 2>&1 | Tee-Object -FilePath (Join-Path $EvidenceDirectory 'activity.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
exit $clientExit
