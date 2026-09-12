param([Parameter(Mandatory=$true)][string]$DotnetExe,[string]$Name='gate-a',[int]$Seconds=30)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$output=Join-Path (Join-Path $root '..\reports\renderer-matrix') $Name
if(Test-Path -LiteralPath $output){throw "Output already exists; preserve previous evidence: $output"}
New-Item -ItemType Directory -Force $output|Out-Null
$capture=Start-Process powershell -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "'+$root+'\capture.ps1" -Output "'+$output+'\frames" -StopFile "'+$output+'\stop"')
try {
  $deadline=(Get-Date).AddSeconds(15)
  while(!(Test-Path -LiteralPath "$output\frames-ready")) {if((Get-Date) -gt $deadline){throw 'ETW not ready'};Start-Sleep -Milliseconds 200}
  $runs=if($Name -eq 'gate-b'){@('avante-N','avante-P','all-N','all-P')}else{@('avante-A','avante-B','avante-C','avante-D','all-D','all-C','all-B','all-A')}
  foreach($run in $runs) {
    $scenario,$mode=$run.Split('-')
    $assembly=if($mode -eq 'N'){Join-Path $root 'control-bin\matrix.dll'}else{Join-Path $root 'bin\Release\net8.0-windows\matrix.dll'}
    $p=Start-Process $DotnetExe -WindowStyle Hidden -ArgumentList ('"'+$assembly+'" '+$mode+' '+$scenario+' '+$Seconds+' "'+$output+'\visual"') -RedirectStandardOutput "$output\$run.jsonl" -RedirectStandardError "$output\$run.err" -PassThru
    $null=$p.Handle;$samples=@()
    while(!$p.HasExited) {
      $rows=Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine | Where-Object {$_.Name -like ('pid_'+$p.Id+'_*')}
      $samples+=@{utc=(Get-Date).ToUniversalTime().ToString('o');gpu=@($rows|Select-Object Name,UtilizationPercentage)}
      Start-Sleep -Seconds 2
    }
    $p.Refresh();$samples|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 "$output\$run-gpu.json"
    Write-Output "$run exit=$($p.ExitCode)"
    if($p.ExitCode -ne 0){throw "Harness failure: $run"}
  }
} finally {Set-Content -LiteralPath "$output\stop" -Value 'complete';if(!$capture.HasExited){$null=$capture.WaitForExit(15000)}}
