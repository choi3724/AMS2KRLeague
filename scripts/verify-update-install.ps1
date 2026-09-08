param(
    [Parameter(Mandatory = $true)][string]$BaselineZip,
    [Parameter(Mandatory = $true)][string]$BaselineSha256,
    [string]$BaselineVersion = '0.4.0',
    [string]$Version = '0.4.3'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proofRoot = Join-Path $repo ('work/update-install-proof-' + [guid]::NewGuid().ToString('N'))
$resolvedProof = [IO.Path]::GetFullPath($proofRoot)
if (-not $resolvedProof.StartsWith([IO.Path]::GetFullPath((Join-Path $repo 'work')) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid proof path' }
$gameBefore = @(Get-Process -Name AMS2,AMS2AVX -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Id=$_.Id; StartTicks=$_.StartTime.ToUniversalTime().Ticks } })
if ((Get-FileHash -LiteralPath $BaselineZip -Algorithm SHA256).Hash -ine $BaselineSha256) { throw 'Baseline ZIP hash mismatch' }
$installed = Join-Path $proofRoot 'portable'
$attempt = Join-Path $proofRoot 'attempt'
New-Item -ItemType Directory -Path $attempt -Force | Out-Null
Expand-Archive -LiteralPath $BaselineZip -DestinationPath $installed
$exe = Join-Path $installed 'AMS2LeagueClient.exe'
$before = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if ($before -ne $BaselineVersion) { throw "Expected actual public v$BaselineVersion baseline, received $before" }
$sentinel = Join-Path $installed 'user-preservation-check.txt'
[IO.File]::WriteAllText($sentinel, 'preserve-user-file')
$installer = Join-Path $attempt 'Setup.exe'
Copy-Item -LiteralPath (Join-Path $repo "artifacts/AMS2-League-Overlay-$Version-Setup.exe") -Destination $installer
$helper = Join-Path $attempt 'ApplyUpdate.ps1'
[IO.File]::WriteAllText($helper, [IO.File]::ReadAllText((Join-Path $repo 'src/AMS2LeagueClient/Runtime/ApplyUpdate.ps1')), [Text.UTF8Encoding]::new($true))
$parent = Start-Process -FilePath $exe -ArgumentList ('--capture-all "' + (Join-Path $proofRoot 'before-capture') + '" --log-dir "' + (Join-Path $proofRoot 'before-logs') + '"') -WindowStyle Hidden -PassThru
$resultPath = Join-Path $proofRoot 'result.json'
$restartCapture = Join-Path $proofRoot 'after-capture'
$settings = @{
    ParentId = $parent.Id
    ParentStartTicks = $parent.StartTime.ToUniversalTime().Ticks.ToString()
    Installer = $installer
    Sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    Size = (Get-Item -LiteralPath $installer).Length
    Version = $Version
    InstallDirectory = $installed
    Executable = $exe
    RestartArguments = '--capture-all "' + $restartCapture + '" --log-dir "' + (Join-Path $proofRoot 'after-logs') + '"'
    ResultPath = $resultPath
} | ConvertTo-Json
$settingsPath = Join-Path $attempt 'update.json'
[IO.File]::WriteAllText($settingsPath, $settings, [Text.UTF8Encoding]::new($true))
$worker = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') -ArgumentList ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $helper + '" -SettingsPath "' + $settingsPath + '"') -WindowStyle Hidden -PassThru
if (-not $worker.WaitForExit(120000)) { throw "Helper did not finish; inspect $proofRoot" }
$result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($worker.ExitCode -ne 0 -or -not $result.success) { throw ($result | ConvertTo-Json -Compress) }
$deadline = [DateTime]::UtcNow.AddSeconds(60)
while (-not (Test-Path -LiteralPath (Join-Path $restartCapture 'capture-manifest.txt'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw "Updated application did not complete capture: $proofRoot" }
    Start-Sleep -Milliseconds 200
}
$after = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if ($after -ne $Version) { throw 'Updated executable version mismatch' }
if ([IO.File]::ReadAllText($sentinel) -ne 'preserve-user-file') { throw 'User file changed' }
if (Test-Path -LiteralPath (Join-Path $installed 'unins000.exe')) { throw 'Portable update created an uninstaller' }
$logs = Get-ChildItem -LiteralPath (Join-Path $proofRoot 'after-logs') -File | Get-Content
if ($logs -match 'EXCEPTION') { throw 'Updated application logged an exception' }
$gamePreserved = @($gameBefore | Where-Object { $running=Get-Process -Id $_.Id -ErrorAction SilentlyContinue; $null -ne $running -and $running.StartTime.ToUniversalTime().Ticks -eq $_.StartTicks }).Count -eq $gameBefore.Count
$summary = [ordered]@{ gameRunningDuringInstall = ($gameBefore.Count -gt 0); gameProcessesPreserved = $gamePreserved; result = 'PASS'; from = $before; to = $after; helperExit = $worker.ExitCode; portablePreserved = $true; userFilePreserved = $true; restartedCaptureFiles = @(Get-ChildItem -LiteralPath $restartCapture -Filter '*.png').Count; proofDirectory = $proofRoot }
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $proofRoot 'summary.json') -Encoding UTF8
$summary | ConvertTo-Json
