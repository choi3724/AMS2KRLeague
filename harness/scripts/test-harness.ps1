# Regression checks for harness parsing and exception scope. No product/test edits.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tokens=$null; $parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify.ps1'),[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count) { throw 'verify.ps1 syntax error' }
$regexAssignment=$ast.Find({param($a) $a -is [Management.Automation.Language.AssignmentStatementAst] -and $a.Left.Extent.Text -eq '$TestSummaryRegex'},$true)
$regex=$regexAssignment.Right.Expression.Value
$testIf=$ast.Find({param($a) $a -is [Management.Automation.Language.IfStatementAst] -and $a.Clauses[0].Item1.Extent.Text.StartsWith('$code -eq 0 -and $total')},$true)
$condition=[scriptblock]::Create($testIf.Clauses[0].Item1.Extent.Text)
$cases=@(
    @('RESULT: 135 passed, 0 failed, 135 total',0,$true),
    @('RESULT: 110 passed, 0 failed, 110 total (123 ms)',0,$true),
    @('RESULT: 134 passed, 1 failed, 135 total',1,$false),
    @('RESULT: 134 passed, 1 failed, 135 total',0,$false),
    @('RESULT: 135 passed, 0 failed, 135 total',1,$false),
    @('unrecognized output',0,$false),
    @('RESULT: 0 passed, 0 failed, 0 total',0,$false),
    @('RESULT: 135 passed, 1 failed, 135 total',0,$false)
)
$count=0
foreach($case in $cases){
    $passed=-1; $total=-1; $failed=-1; $code=$case[1]
    if($case[0] -match $regex){$passed=[int]$Matches[1];$total=[int]$Matches[2];$failed=[int]$Matches['failed']}
    if((& $condition) -ne $case[2]){throw 'Test summary acceptance regression'}
    $count++
}
Write-Host "SUMMARY_CHECK: PASS ($count cases)"

$fixture=Join-Path $repo ('harness\reports\checker-probe-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'harness\scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-secrets.ps1') -Destination (Join-Path $fixture 'harness\scripts\check-secrets.ps1')
& git init --quiet $fixture
if($LASTEXITCODE -ne 0){throw 'Probe git init failed'}
# Invented detection fixtures, never credentials. Keep values out of console output.
$examplePath='E:'+'\fixture-example'
$fakeToken='ghp_'+('A'*24)
$file=Join-Path $fixture 'example.txt'
$allow=Join-Path $fixture 'harness\scripts\secret-allowlist.txt'
@{file='example.txt';label='dev drive path E:';value=$examplePath} | ConvertTo-Json -Compress | Set-Content $allow -Encoding UTF8
Set-Content $file ($examplePath+' '+$fakeToken) -Encoding UTF8
# Ignore the checker source in the fixture so only the two deliberate matches count.
Set-Content (Join-Path $fixture '.gitignore') 'harness/scripts/' -Encoding UTF8
$out=& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'harness\scripts\check-secrets.ps1') 2>&1
$code=$LASTEXITCODE
if($code -ne 1 -or ($out -join "`n") -notmatch 'hits=1' -or ($out -join "`n").Contains($fakeToken)){throw 'Scoped allowlist/redaction/untracked probe failed'}
Write-Host 'SECRET_BLOCK_CHECK: PASS (exit=1 hits=1 redacted=True untracked=True)'
Set-Content $file $examplePath -Encoding UTF8
$out=& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'harness\scripts\check-secrets.ps1') 2>&1
$code=$LASTEXITCODE
if($code -ne 0 -or ($out -join "`n") -notmatch 'hits=0'){throw 'Reviewed exception probe failed'}
Write-Host 'SECRET_ALLOW_CHECK: PASS (exit=0 hits=0)'
Write-Host 'HARNESS_SELF_CHECK: PASS (10 cases)'
