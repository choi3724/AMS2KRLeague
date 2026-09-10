# Git tracked and non-ignored untracked text files; never print matched values.
[CmdletBinding()]
param(
    [string]$Allowlist = '',
    [switch]$IncludeDocs
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo
if (-not $Allowlist) { $Allowlist = Join-Path $PSScriptRoot 'secret-allowlist.txt' }

# Each exception is a JSON object: file, label, value (exact matched text).
# File and label must match as well; an allowed path cannot hide a token on the same line.
$allow = @()
if (Test-Path -LiteralPath $Allowlist) {
    foreach ($line in Get-Content -LiteralPath $Allowlist -Encoding UTF8) {
        if ($line.Trim() -and -not $line.Trim().StartsWith('#')) {
            $entry = $line | ConvertFrom-Json
            if (-not $entry.file -or -not $entry.label -or -not $entry.value) { throw 'Invalid allowlist entry' }
            $allow += $entry
        }
    }
}
$patterns = [ordered]@{
    'GitHub token' = 'gh[pousr]_[A-Za-z0-9]{20,}'
    'Bearer literal' = 'Bearer\s+[A-Za-z0-9\-_\.]{16,}'
    'password assignment' = '(?i)(password|passwd|pwd)\s*[:=]\s*["''][^"'']{4,}["'']'
    'token assignment' = '(?i)\b(token|secret|apikey|api_key)\s*[:=]\s*["''][A-Za-z0-9\-_\.]{16,}["'']'
    'FTP URL with creds' = '(?i)ftp://[^/\s:]+:[^@\s]+@'
    'dev drive path E:' = '(?i)E:\\[^\s"''<>`|]+'
    'user profile path' = '(?i)C:\\Users\\[^\\\s"''<>`|]+'
    'private key block' = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
}
$binaryExt = @('.png','.jpg','.jpeg','.gif','.ico','.ttf','.otf','.woff','.woff2','.dll','.exe','.pdb','.zip','.gz','.a2ct','.bin','.snk','.pfx')
$files = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { Write-Host 'SECRET CHECK: ERROR (git ls-files)'; exit 2 }
$hits = 0; $accepted = 0; $scanned = 0
foreach ($f in ($files | Sort-Object -Unique)) {
    $full = Join-Path $repo $f
    if ($binaryExt -contains [IO.Path]::GetExtension($f).ToLowerInvariant()) { continue }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    # The allowlist is reviewed configuration, not a second copy of source content.
    if ([IO.Path]::GetFullPath($full) -eq [IO.Path]::GetFullPath($Allowlist)) { continue }
    $scanned++
    $lines = @(Get-Content -LiteralPath $full -Encoding UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($label in $patterns.Keys) {
            foreach ($match in [regex]::Matches($lines[$i], $patterns[$label])) {
                $allowed = $false
                foreach ($a in $allow) {
                    if ($a.file -ceq $f -and $a.label -ceq $label -and $a.value -ceq $match.Value) { $allowed = $true; break }
                }
                if ($allowed) { $accepted++; continue }
                $hits++
                Write-Host ('  [HIT] {0}:{1}  <{2}>  [value redacted]' -f $f, ($i+1), $label)
            }
        }
    }
}
Write-Host "scanned=$scanned allowed=$accepted hits=$hits"
if ($hits -gt 0) { Write-Host "SECRET CHECK: FAIL ($hits hit)"; exit 1 }
Write-Host 'SECRET CHECK: PASS'
exit 0
