# verify.ps1 — 작업 후 검증 게이트 (버전 → 비밀 → restore → build → 테스트 2종 → 보고서)
# 사용:
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\verify.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\verify.ps1 -DotnetExe .\work\dotnet8\dotnet.exe
#   ... -SkipTests   (빌드까지만)
# 종료 코드: 0 = GATE PASS, 1 = GATE FAIL, 2 = 환경 오류
# 결과: harness\reports\verify-<yyyyMMdd-HHmmss>.md, harness\reports\last-verify.json

[CmdletBinding()]
param(
    [string]$DotnetExe = "",
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SkipSecrets,
    [switch]$SkipVersions
)

$ErrorActionPreference = "Continue"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repo
& git rev-parse --is-inside-work-tree | Out-Null
if ($LASTEXITCODE -ne 0) { exit 2 }
$reportsDir = Join-Path $repo "harness\reports"
New-Item -ItemType Directory -Force -ErrorAction Stop -Path $reportsDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# ---- 설정: 실제 경로와 다르면 Codex가 여기만 고친다 ----
$Solution = "AMS2KRLeague.sln"
$TestProjects = @(
    @{ Name = "Client";   Path = "tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj" },
    @{ Name = "Activity"; Path = "tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj" }
)
# 테스트 출력에서 "통과/전체" 숫자를 뽑는 정규식 (그룹1=통과, 그룹2=전체). 실제 출력 형식에 맞춰 조정.
$TestSummaryRegex = '^RESULT:\s+(\d+) passed, (?<failed>\d+) failed, (\d+) total(?: \(\d+ ms\))?\s*$'
# --------------------------------------------------------

function Resolve-Dotnet {
    param([string]$Explicit)
    if ($Explicit -and (Test-Path $Explicit)) { return (Resolve-Path $Explicit).Path }
    if ($env:AMS2_DOTNET -and (Test-Path $env:AMS2_DOTNET)) { return $env:AMS2_DOTNET }
    $local = Join-Path $repo "work\dotnet8\dotnet.exe"
    if (Test-Path $local) { return $local }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

$result = [ordered]@{
    timestamp     = (Get-Date).ToString("s")
    repo          = "$repo"
    branch        = (git rev-parse --abbrev-ref HEAD)
    head          = (git rev-parse HEAD)
    dirty         = [bool](git status --short)
    dotnet        = ""
    configuration = $Configuration
    steps         = @()
    gate          = "FAIL"
    duration_s    = 0
}

function Add-Step {
    param([string]$Name, [string]$Status, [string]$Detail = "", [hashtable]$Extra = @{})
    $s = [ordered]@{ name = $Name; status = $Status; detail = $Detail }
    foreach ($k in $Extra.Keys) { $s[$k] = $Extra[$k] }
    $script:result.steps += $s
    $mark = switch ($Status) { "PASS" { "[PASS]" } "FAIL" { "[FAIL]" } "SKIP" { "[SKIP]" } default { "[----]" } }
    Write-Host "$mark $Name $(if ($Detail) { "— $Detail" })"
}

Write-Host "=== VERIFY ($stamp) ===  branch=$($result.branch) head=$($result.head) dirty=$($result.dirty)"

$dotnet = Resolve-Dotnet $DotnetExe
if (-not $dotnet) {
    Add-Step "dotnet" "FAIL" "dotnet을 찾을 수 없음 (-DotnetExe / AMS2_DOTNET / work\dotnet8 / PATH)"
    $result.gate = "FAIL"; $result | ConvertTo-Json -Depth 6 | Set-Content -ErrorAction Stop -Encoding UTF8 (Join-Path $reportsDir "last-verify.json")
    exit 2
}
$sdkVersion = & $dotnet --version
if ($LASTEXITCODE -ne 0) { Add-Step "dotnet" "FAIL" "SDK selection failed"; exit 2 }
$env:DOTNET_CLI_UI_LANGUAGE = "en-US"
$env:DOTNET_CLI_HOME = Join-Path $repo "work\dotnet-home"
$result.dotnet = "$dotnet ($sdkVersion)"
Write-Host "dotnet: $($result.dotnet)"

$anyFail = [bool]($SkipTests -or $SkipSecrets -or $SkipVersions)
if ($anyFail) { Write-Host "Incomplete verification: skipped checks cannot produce GATE: PASS." }

# 1. 버전 일치
if ($SkipVersions) { Add-Step "versions" "SKIP" }
else {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check-versions.ps1") 2>&1
    $code = $LASTEXITCODE
    $detail = ($out | Where-Object { $_ -match '\[FAIL\]|canonical' }) -join "; "
    if ($code -eq 0) { Add-Step "versions" "PASS" $detail } else { Add-Step "versions" "FAIL" $detail; $anyFail = $true }
}

# 2. 비밀 검사
if ($SkipSecrets) { Add-Step "secrets" "SKIP" }
else {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check-secrets.ps1") 2>&1
    $code = $LASTEXITCODE
    $detail = ($out | Where-Object { $_ -match '\[HIT\]|SECRET CHECK' }) -join "; "
    if ($code -eq 0) { Add-Step "secrets" "PASS" } else { Add-Step "secrets" "FAIL" $detail; $anyFail = $true }
}

# 3. restore
$out = & $dotnet restore $Solution 2>&1
$restoreCode = $LASTEXITCODE
$out | Set-Content -ErrorAction Stop -Encoding UTF8 (Join-Path $reportsDir "restore-$stamp.log")
if ($restoreCode -eq 0) { Add-Step "restore" "PASS" } else { Add-Step "restore" "FAIL" (($out | Select-Object -Last 5) -join " | "); $anyFail = $true }

# 4. build (0 warning 요구)
$buildLog = Join-Path $reportsDir "build-$stamp.log"
$out = & $dotnet build $Solution -c $Configuration --no-restore 2>&1
$buildCode = $LASTEXITCODE
$out | Set-Content -ErrorAction Stop -Encoding UTF8 $buildLog
$warnLine = ($out | Where-Object { $_ -match '^\s*(\d+) Warning\(s\)' } | Select-Object -Last 1)
$errLine  = ($out | Where-Object { $_ -match '^\s*(\d+) Error\(s\)' }   | Select-Object -Last 1)
$warnings = if ($warnLine -match '(\d+) Warning') { [int]$Matches[1] } else { -1 }
$errors   = if ($errLine  -match '(\d+) Error')   { [int]$Matches[1] } else { -1 }
$buildDetail = "errors=$errors warnings=$warnings log=$(Split-Path $buildLog -Leaf)"
if ($buildCode -eq 0 -and $errors -eq 0 -and $warnings -eq 0) {
    Add-Step "build" "PASS" $buildDetail @{ warnings = $warnings; errors = $errors }
} elseif ($buildCode -eq 0 -and $warnings -gt 0) {
    Add-Step "build" "FAIL" "$buildDetail (harness/README.md: 0 warnings required)" @{ warnings = $warnings; errors = $errors }
    $anyFail = $true
} else {
    Add-Step "build" "FAIL" $buildDetail @{ warnings = $warnings; errors = $errors }
    $anyFail = $true
}

# 5. 테스트
if ($SkipTests -or $buildCode -ne 0 -or $restoreCode -ne 0) {
    foreach ($t in $TestProjects) { Add-Step "test:$($t.Name)" "SKIP" $(if ($buildCode -ne 0) { "빌드 실패" } else { "-SkipTests" }) }
} else {
    foreach ($t in $TestProjects) {
        $log = Join-Path $reportsDir "test-$($t.Name)-$stamp.log"
        $out = & $dotnet run --project $t.Path -c $Configuration --no-build 2>&1
        $code = $LASTEXITCODE
        $out | Set-Content -ErrorAction Stop -Encoding UTF8 $log
        $summary = ($out | Where-Object { $_ -match $TestSummaryRegex } | Select-Object -Last 1)
        $passed = -1; $total = -1; $failed = -1
        if ($summary -and $summary -match $TestSummaryRegex) { $passed = [int]$Matches[1]; $total = [int]$Matches[2]; $failed = [int]$Matches["failed"] }
        $detail = "exit=$code passed=$passed total=$total failed=$failed log=$(Split-Path $log -Leaf)"
        if ($code -eq 0 -and $total -gt 0 -and $passed -eq $total -and $failed -eq 0) {
            Add-Step "test:$($t.Name)" "PASS" $detail @{ passed = $passed; total = $total; failed = $failed; exit = $code }
        } else {
            Add-Step "test:$($t.Name)" "FAIL" $detail @{ passed = $passed; total = $total; failed = $failed; exit = $code }
            $anyFail = $true
        }
    }
}

$sw.Stop()
$result.duration_s = [math]::Round($sw.Elapsed.TotalSeconds, 1)
$result.gate = if ($anyFail) { "FAIL" } else { "PASS" }

# 보고서
$json = $result | ConvertTo-Json -Depth 6
$json | Set-Content -ErrorAction Stop -Encoding UTF8 (Join-Path $reportsDir "last-verify.json")
$md = @()
$md += "# verify $stamp"
$md += ""
$md += "- GATE: **$($result.gate)**"
$md += "- branch/head: $($result.branch) / $($result.head) (dirty=$($result.dirty))"
$md += "- dotnet: $($result.dotnet)"
$md += "- duration: $($result.duration_s)s"
$md += ""
$md += "| step | status | detail |"
$md += "|---|---|---|"
foreach ($s in $result.steps) { $md += "| $($s.name) | $($s.status) | $($s.detail -replace '\|','/') |" }
$md -join "`n" | Set-Content -ErrorAction Stop -Encoding UTF8 (Join-Path $reportsDir "verify-$stamp.md")

Write-Host ""
Write-Host "=== GATE: $($result.gate) ($($result.duration_s)s) ==="
Write-Host "report: harness\reports\verify-$stamp.md"
if ($anyFail) { exit 1 } else { exit 0 }
