# check-versions.ps1 — 버전 문자열이 모든 곳에서 일치하는지 검사
# 사용: powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\check-versions.ps1
# 종료 코드: 0 = 일치, 1 = 불일치, 2 = 설정/파일 오류

[CmdletBinding()]
param(
    [string]$Config = ""
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repo
if (-not $Config) { $Config = Join-Path $PSScriptRoot "version-config.json" }

if (-not (Test-Path $Config)) { Write-Host "FAIL: config 없음: $Config"; exit 2 }
$cfg = Get-Content $Config -Raw -Encoding UTF8 | ConvertFrom-Json

# canonical
$canonPath = Join-Path $repo $cfg.canonical.file
if (-not (Test-Path $canonPath)) { Write-Host "FAIL: canonical 파일 없음: $($cfg.canonical.file)"; exit 2 }
$m = [regex]::Match((Get-Content $canonPath -Raw -Encoding UTF8), $cfg.canonical.pattern)
if (-not $m.Success) { Write-Host "FAIL: canonical 패턴이 $($cfg.canonical.file)에서 매치되지 않음"; exit 2 }
$canonical = $m.Groups[1].Value
$canonicalPrefix = ($canonical -split '-')[0]
Write-Host "canonical version: $canonical  (source: $($cfg.canonical.file))"

$fail = 0
foreach ($c in $cfg.checks) {
    $path = Join-Path $repo $c.file
    if (-not (Test-Path $path)) {
        if ($c.required) { Write-Host "  [FAIL] $($c.label): 파일 없음 ($($c.file))"; $fail++ }
        else { Write-Host "  [SKIP] $($c.label): 파일 없음 ($($c.file))" }
        continue
    }
    $content = Get-Content $path -Raw -Encoding UTF8
    $mm = [regex]::Match($content, $c.pattern)
    if (-not $mm.Success) {
        if ($c.required) { Write-Host "  [FAIL] $($c.label): 패턴 매치 없음 ($($c.file)) — version-config.json 패턴을 실제 파일에 맞게 조정 필요"; $fail++ }
        else { Write-Host "  [SKIP] $($c.label): 패턴 매치 없음 ($($c.file))" }
        continue
    }
    $found = $mm.Groups[1].Value
    $expected = if ($c.compare -eq "prefix") { $canonicalPrefix } else { $canonical }
    if ($found -eq $expected) {
        Write-Host "  [OK]   $($c.label): $found"
    } else {
        Write-Host "  [FAIL] $($c.label): $found (expected $expected) — $($c.file)"
        $fail++
    }
}

if ($fail -gt 0) { Write-Host "VERSION CHECK: FAIL ($fail mismatch)"; exit 1 }
Write-Host "VERSION CHECK: PASS"
exit 0
