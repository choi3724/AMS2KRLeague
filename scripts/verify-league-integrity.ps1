param(
    [Parameter(Mandatory=$true)][string]$DotnetExecutable,
    [Parameter(Mandatory=$true)][string]$ServerRoot,
    [Parameter(Mandatory=$true)][string]$PhpExecutable,
    [Parameter(Mandatory=$true)][string]$PhpExtensionDirectory,
    [Parameter(Mandatory=$true)][string]$ActualRaceEvidence,
    [string]$ActualQualifyingPayload,
    [string]$EvidenceDirectory = 'work/league-integrity-verification',
    [switch]$OfflineRestore
)
# Read raw evidence only. Never start the product, send its queue, or access production.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
foreach ($file in @($DotnetExecutable,$PhpExecutable,$ActualRaceEvidence)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing input: $file" }
}
if (!(Test-Path -LiteralPath (Join-Path $ServerRoot 'tests/overlay_witness_contract.php'))) {
    throw 'The matching server contract test is required.'
}
$evidence = [IO.Path]::GetFullPath((Join-Path $repo $EvidenceDirectory))
if (!$evidence.StartsWith($repo + '\',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceDirectory must be inside this repository.'
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$buildRoot = Join-Path $evidence 'build'
$buildArgs = @('build','AMS2KRLeague.sln','-c','Release','--artifacts-path',$buildRoot,'--ignore-failed-sources')
if ($OfflineRestore) {
    Write-Warning 'Cached dependency build only. NuGet vulnerability audit is NOT RUN.'
    $buildArgs += '-p:NuGetAudit=false'
}
& $DotnetExecutable @buildArgs 2>&1 | Tee-Object -FilePath (Join-Path $evidence 'build.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$failed = @()
foreach ($suite in @('AMS2LeagueActivity.Tests','AMS2LeagueClient.Tests')) {
    & $DotnetExecutable (Join-Path $buildRoot "bin/$suite/release/$suite.dll") 2>&1 | Tee-Object -FilePath (Join-Path $evidence "$suite.log")
    if ($LASTEXITCODE -ne 0) { $failed += $suite }
}
$actualOutput = Join-Path $evidence 'actual-race'
& $DotnetExecutable (Join-Path $buildRoot 'bin/AMS2LeagueActivity.Tests/release/AMS2LeagueActivity.Tests.dll') --league-evidence $ActualRaceEvidence $actualOutput 2>&1 | Tee-Object -FilePath (Join-Path $evidence 'actual-race.log')
if ($LASTEXITCODE -ne 0) { $failed += 'actual-evidence-replay' }
$phpArgs = @('-d',"extension_dir=$PhpExtensionDirectory",'-d','extension=pdo_sqlite','-d','extension=openssl','-d','extension=sodium')
Push-Location $ServerRoot
try {
    foreach ($file in Get-ChildItem app,tests -Recurse -File -Filter *.php) {
        & $PhpExecutable -l $file.FullName
        if ($LASTEXITCODE -ne 0) { $failed += "lint:$($file.Name)" }
    }
    $suites = @('final_race_result','result_source_repair','witness_reconciliation','safety_car_wire',
        'qualifying_witness_recovery','qualifying_approval_order','portal','telemetry',
        'event_clock_alignment','publication_cleanup','race_phase_clock','roster_only_results',
        'replay_sparse_state','classification_identity','race_settings','witness_retry',
        'gzip_requests','gzip_runtime','source_presence','catalog_schedule','game_information',
        'numeric_display_names','web06','web10_review','result_grid','consolidated_race')
    foreach ($suite in $suites) {
        & $PhpExecutable @phpArgs "tests/$suite.php" 2>&1 | Tee-Object -FilePath (Join-Path $evidence "php-$suite.log")
        if ($LASTEXITCODE -ne 0) { $failed += "php:$suite" }
    }
    $wireInputs = @((Join-Path $actualOutput 'replayed-witness.json'))
    if ($ActualQualifyingPayload) { $wireInputs += $ActualQualifyingPayload }
    & $PhpExecutable @phpArgs tests/overlay_witness_contract.php @wireInputs 2>&1 | Tee-Object -FilePath (Join-Path $evidence 'cross-language-contract.log')
    if ($LASTEXITCODE -ne 0) { $failed += 'cross-language-contract' }
} finally { Pop-Location }
Write-Output "Scoped league gate failures: $($failed -join ', ')"
Write-Output 'Not a production/full legacy-web release gate. See handoff for run.php/web03.php failures.'
if ($failed.Count -gt 0) { exit 1 }
exit 0
