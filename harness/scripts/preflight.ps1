# preflight.ps1 — 작업 시작 전 저장소 상태 점검
# 사용: powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\preflight.ps1
# 아무것도 수정하지 않는다. 읽기 전용.

[CmdletBinding()]
param(
    [string]$DotnetExe = ""
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repo

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

Write-Host "=== PREFLIGHT ($(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) ==="
Write-Host "repo: $repo"

# 1. git 상태
$branch = git rev-parse --abbrev-ref HEAD
$head   = git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { exit 2 }
$status = git status --short
Write-Host "branch: $branch  HEAD: $head"
if ($status) {
    Write-Host "WARN: 작업 트리에 미커밋 변경이 있습니다. 다른 에이전트의 작업일 수 있으니 되돌리지 마십시오."
    $status | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host "worktree: clean"
}

# 2. 기준선 태그 존재 확인
foreach ($tag in @("v0.7.0")) {
    if (git tag -l $tag) { Write-Host "baseline tag ${tag}: present" }
    else { Write-Host "WARN: baseline tag $tag 없음 (git fetch --tags 필요할 수 있음)" }
}

# 3. 최신 태그와 Directory.Build.props 버전 비교
$latestTag = git describe --tags --abbrev=0 2>$null
$propsVer = ""
$props = Join-Path $repo "Directory.Build.props"
if (Test-Path $props) {
    $m = [regex]::Match((Get-Content $props -Raw -Encoding UTF8), '<Version>\s*([^<]+?)\s*</Version>')
    if ($m.Success) { $propsVer = $m.Groups[1].Value }
}
Write-Host "latest tag: $latestTag   Directory.Build.props Version: $propsVer"

# 4. dotnet
$dotnet = Resolve-Dotnet $DotnetExe
if ($dotnet) {
    $v = & $dotnet --version
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: SDK selection failed"; exit 2 }
    Write-Host "dotnet: $dotnet ($v)"
} else {
    Write-Host "FAIL: dotnet을 찾을 수 없습니다. -DotnetExe 또는 AMS2_DOTNET 환경변수를 지정하십시오."
    exit 2
}

# 5. 위험 파일이 추적되는지
$tracked = git ls-files
$danger = $tracked | Where-Object { $_ -match '(^|/)\.env($|\.)|\.pfx$|\.snk$|credential|secret' }
if ($danger) {
    Write-Host "WARN: 다음 추적 파일 이름이 비밀/자격증명처럼 보입니다:"
    $danger | ForEach-Object { Write-Host "  $_" }
}

Write-Host "=== PREFLIGHT DONE ==="
exit 0
