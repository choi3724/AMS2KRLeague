param([Parameter(Mandatory = $true)][string]$SettingsPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($SettingsPath))
$settings = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$updateLock = [Threading.Mutex]::new($false, 'Local\AMS2KRLeague.AutoUpdate')
$ownsLock = $false
$parentExited = $false
$success = $false
$restarted = $false
$message = '업데이트: 설치하지 못했습니다. 기존 설치 파일을 다시 실행해 주세요.'
$installerStream = $null
$canDeleteInstaller = $false
try {
    try { $ownsLock = $updateLock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsLock = $true }
    if (-not $ownsLock) { exit 2 }
    $installRoot = [IO.Path]::GetFullPath($settings.InstallDirectory).TrimEnd('\')
    $executable = [IO.Path]::GetFullPath($settings.Executable)
    $installer = [IO.Path]::GetFullPath($settings.Installer)
    if ($executable -ine (Join-Path $installRoot 'AMS2LeagueClient.exe') -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw '실행 파일 경로가 올바르지 않습니다.' }
    if (-not $installer.StartsWith($taskDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '설치 파일이 업데이트 폴더 밖에 있습니다.' }
    $canDeleteInstaller = $true
    if ($settings.Sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw '설치 파일 검증 정보가 올바르지 않습니다.' }
    # Hold a read-only handle across execution so verified bytes cannot be replaced.
    $installerStream = [IO.File]::Open($installer, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $actualHash = [BitConverter]::ToString($hasher.ComputeHash($installerStream)).Replace('-', '') } finally { $hasher.Dispose() }
    if ($installerStream.Length -ne [long]$settings.Size -or $actualHash -ine $settings.Sha256) { throw '설치 파일 검증에 실패했습니다.' }
    $parent = Get-Process -Id $settings.ParentId -ErrorAction SilentlyContinue
    if ($null -eq $parent -or $parent.StartTime.ToUniversalTime().Ticks -ne [long]$settings.ParentStartTicks) { throw '업데이트를 요청한 프로그램을 확인할 수 없습니다.' }
    [IO.File]::WriteAllText((Join-Path $taskDirectory 'ready'), 'ready')
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not $parent.HasExited) {
        if (Test-Path -LiteralPath (Join-Path $taskDirectory 'cancel')) { throw '프로그램이 설치를 연기했습니다.' }
        if ([DateTime]::UtcNow -ge $deadline) { throw '프로그램 종료 대기 시간이 초과되었습니다.' }
        Start-Sleep -Milliseconds 200
        $parent.Refresh()
    }
    $parentExited = $true
    # Only another instance using these installation files can block replacement.
    # The game and overlays installed in other directories remain untouched.
    foreach ($other in @(Get-Process -Name AMS2LeagueClient -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($other.Path) -or [IO.Path]::GetFullPath($other.Path) -ieq $executable) {
            throw '같은 설치 폴더의 다른 오버레이가 실행되어 설치를 연기했습니다.'
        }
    }
    $arguments = '/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /NOCLOSEAPPLICATIONS /NOFORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /DIR="' + $installRoot + '" /LOG="' + (Join-Path $taskDirectory 'install.log') + '"'
    if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'unins000.exe'))) { $arguments += ' /PORTABLE=1 /NOICONS' }
    $setup = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($setup.ExitCode -eq 3010) { throw '설치 후 Windows 재시작이 필요합니다.' }
    if ($setup.ExitCode -ne 0) { throw ('설치 프로그램 오류: ' + $setup.ExitCode) }
    $installed = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
    if ($installed -cne $settings.Version) { throw '설치된 버전이 요청한 버전과 다릅니다.' }
    $success = $true
    $message = '업데이트: ' + $settings.Version + ' 설치 완료'
} catch {
    $message = '업데이트: 설치 실패 또는 연기 · 업데이트 로그를 확인해 주세요. 6시간 후 다시 확인합니다.'
    try { [IO.File]::WriteAllText((Join-Path $taskDirectory 'failure.log'), $_.Exception.ToString(), [Text.UTF8Encoding]::new($false)) }
    catch { Write-Warning 'Failed to write update diagnostic log.' }
} finally {
    if ($null -ne $installerStream) { $installerStream.Dispose() }
    if ($ownsLock) {
        try {
            $result = @{ success = $success; message = $message; version = $settings.Version; atUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json
            try { [IO.File]::WriteAllText($settings.ResultPath, $result, [Text.UTF8Encoding]::new($false)) }
            catch { Write-Warning 'Failed to write initial update result; restart will still be attempted.' }
            if ($parentExited -and (Test-Path -LiteralPath $settings.Executable)) {
                try {
                $restart = [Diagnostics.ProcessStartInfo]::new()
                $restart.FileName = $settings.Executable
                $restart.WorkingDirectory = $settings.InstallDirectory
                $restart.Arguments = $settings.RestartArguments
                $restart.UseShellExecute = $false
                $restart.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
                $started = [Diagnostics.Process]::Start($restart)
                if ($null -eq $started) { throw '프로그램을 다시 실행하지 못했습니다.' }
                try {
                    if ($started.WaitForExit(2000)) { throw ('프로그램이 재실행 직후 종료되었습니다: ' + $started.ExitCode) }
                    $restarted = $true
                    if ($success) { $message = '업데이트: ' + $settings.Version + ' 설치 완료 · 자동 재실행 완료' }
                } finally { $started.Dispose() }
                } catch {
                    $success = $false
                    $message = '업데이트: 자동 재실행 실패 · 프로그램을 직접 실행해 주세요.'
                    try { [IO.File]::WriteAllText((Join-Path $taskDirectory 'restart-failure.log'), $_.Exception.ToString(), [Text.UTF8Encoding]::new($false)) }
                    catch { Write-Warning 'Failed to write restart diagnostic log.' }
                }
            }
            $result = @{ success = $success; restarted = $restarted; message = $message; version = $settings.Version; atUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json
            [IO.File]::WriteAllText($settings.ResultPath, $result, [Text.UTF8Encoding]::new($false))
            if ($canDeleteInstaller -and (Test-Path -LiteralPath $settings.Installer)) { Remove-Item -LiteralPath $settings.Installer -Force }
        } finally { $updateLock.ReleaseMutex() }
    }
    $updateLock.Dispose()
}
if ($success) { exit 0 } else { exit 1 }
