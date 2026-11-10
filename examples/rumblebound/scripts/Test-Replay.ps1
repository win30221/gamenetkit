<#
.SYNOPSIS
驗證 SDK／遊戲 journal、真實錄製、離線重建與篡改拒絕。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go。
行為與產物：建立 artifacts/replays 與驗證報告，會用測試副本驗證預期失敗。
.EXAMPLE
./examples/rumblebound/scripts/Test-Replay.ps1 -Seconds 3
#>
param([ValidateRange(3,30)][int]$Seconds=5)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot
New-Item -ItemType Directory -Force (Join-Path $root 'artifacts')|Out-Null
$log=Join-Path $root 'artifacts/replay-tests.log'
Push-Location (Join-Path $root 'server')
try{& $Go test ./replay -count=1 -v 2>&1|Tee-Object -FilePath $log;if($LASTEXITCODE -ne 0){throw 'Replay SDK tests failed'}}finally{Pop-Location}
Push-Location (Join-Path $root 'examples/rumblebound/game')
try{& $Go test ./sim ./service -run TestReplay -count=1 -v 2>&1|Tee-Object -FilePath $log -Append;if($LASTEXITCODE -ne 0){throw 'Game replay tests failed'}}finally{Pop-Location}
Push-Location (Join-Path $root 'examples/rumblebound/game')
try{
 $out=Join-Path $root 'artifacts/replay-example.json'
 & $Go run -buildvcs=false ./cmd/replaydemo -dir (Join-Path $root 'artifacts/replays') -seconds $Seconds | Tee-Object -FilePath $out
 if($LASTEXITCODE -ne 0){throw 'Real queued-engine replay example failed'}
 $example=Get-Content -Raw -LiteralPath $out | ConvertFrom-Json
 # A separate process must reconstruct the recording without the live world.
 & $Go run -buildvcs=false ./cmd/replay -file $example.file | Tee-Object -FilePath (Join-Path $root 'artifacts/replay-verify.json')
 if($LASTEXITCODE -ne 0){throw 'Offline replay verification failed'}
 # Alter one input in a copy and require a failed CLI exit plus a localized
 # state divergence, rather than merely displaying a successful playback.
 $lines=[IO.File]::ReadAllLines($example.file)
 $edited=$false
 for($i=0;$i -lt $lines.Length;$i++){
  $row=$lines[$i]|ConvertFrom-Json
  if($row.type -eq 'event' -and $row.entry.kind -eq 'input'){$row.entry.payload.input.X=0;$lines[$i]=$row|ConvertTo-Json -Compress -Depth 20;$edited=$true;break}
 }
 if(-not $edited){throw 'Example recording had no input'}
 $modified=Join-Path $root 'artifacts/replay-modified-input.jsonl'
 [IO.File]::WriteAllLines($modified,$lines,[Text.UTF8Encoding]::new($false))
 $failure=Join-Path $root 'artifacts/replay-divergence.json'
 & $Go run -buildvcs=false ./cmd/replay -file $modified 2>$null | Tee-Object -FilePath $failure
 if($LASTEXITCODE -eq 0){throw 'Modified input unexpectedly passed verification'}
 $difference=Get-Content -Raw -LiteralPath $failure|ConvertFrom-Json
 if($difference.status -ne 'FAIL' -or $null -eq $difference.divergence -or $difference.divergence.kind -ne 'input'){throw 'Modified input did not identify the first state divergence'}
 'RF_REPLAY PASS: SDK bounds/integrity, game replay tests, live-engine recording, separate-process offline verification and intentional input divergence' | Tee-Object -FilePath $log -Append
 $global:LASTEXITCODE=0
}finally{Pop-Location}
