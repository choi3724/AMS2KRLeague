param([string]$Output,[string]$StopFile)
$ErrorActionPreference='Stop'
$name='AMS2_RenderMatrix_'+(Get-Date -Format 'yyyyMMddHHmmss')
$providers=Join-Path $PSScriptRoot 'providers.txt'
& logman create trace $name -o "$Output.etl" -pf $providers -bs 1024 -nb 16 64 -ets *> "$Output-etw.log"
if($LASTEXITCODE -ne 0){throw 'ETW start failed'}
try {
  Set-Content -LiteralPath "$Output-ready" -Value $name
  $deadline=(Get-Date).AddMinutes(12)
  while(!(Test-Path -LiteralPath $StopFile) -and (Get-Date) -lt $deadline){Start-Sleep -Milliseconds 500}
} finally {& logman stop $name -ets *>> "$Output-etw.log"}
