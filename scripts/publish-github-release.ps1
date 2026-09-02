param(
    [string]$Repository = 'choi3724/AMS2KRLeague',
    [string]$Version = '',
    [string]$NotesPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$props = [xml](Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'Directory.Build.props'))
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$props.Project.PropertyGroup.InformationalVersion
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$') {
    throw "Invalid release version: $Version"
}
if ([string]::IsNullOrWhiteSpace($NotesPath)) {
    $NotesPath = Join-Path $projectRoot 'release/RELEASE_NOTES_KO.md'
}
$tag = "v$Version"
$artifacts = Join-Path $projectRoot 'artifacts'
$assets = @(
    (Join-Path $artifacts "AMS2-League-Overlay-$Version-Setup.exe"),
    (Join-Path $artifacts "AMS2-League-Overlay-$Version-win-x64.zip"),
    (Join-Path $artifacts "SHA256SUMS-$Version.txt"),
    (Join-Path $artifacts "release-manifest-$Version.json")
)
foreach ($path in @($NotesPath) + $assets) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release file is missing: $path"
    }
}

$status = & git -C $projectRoot status --porcelain=v1
if ($LASTEXITCODE -ne 0 -or $status) {
    throw 'Release publishing requires a clean worktree.'
}
$head = (& git -C $projectRoot rev-parse HEAD).Trim()
$tagCommit = (& git -C $projectRoot rev-list -n 1 $tag 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $head) {
    throw "Tag $tag must exist at HEAD before publishing."
}

& gh release create $tag `
    --repo $Repository `
    --title "AMS2 League Overlay $tag" `
    --notes-file $NotesPath `
    --verify-tag `
    --latest `
    @assets
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub Release creation failed.'
}

$latestTag = (& gh api "repos/$Repository/releases/latest" --jq '.tag_name').Trim()
if ($LASTEXITCODE -ne 0 -or $latestTag -ne $tag) {
    throw "GitHub latest verification failed: expected $tag, received $latestTag"
}
Write-Output "LATEST_RELEASE=$tag"
