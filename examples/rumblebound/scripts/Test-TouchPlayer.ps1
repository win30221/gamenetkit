<#
.SYNOPSIS
驗證實際 Player 的多指控制流程。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：已建置的 Windows Player 與可用遊戲服務。
行為與產物：預設連本機 18093，可用 -Origin 指定站台；不代表實體手機驗收。
.EXAMPLE
./examples/rumblebound/scripts/Test-TouchPlayer.ps1
#>
#requires -Version 7.0
param([string]$Origin='http://127.0.0.1:18093',[int]$Width=844,[int]$Height=390)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$root=$script:GameNetKitRoot
$player=Join-Path $root 'examples/rumblebound/client/Builds/Windows/RumbleboundExample.exe'
$log=Join-Path $root 'artifacts/touch-player.log'
if(-not(Test-Path -LiteralPath $player)){throw 'Build RumbleboundExample Windows first'}
$uri=[uri]$Origin
if($uri.Scheme -notin @('http','https') -or $uri.UserInfo -or $uri.Query -or $uri.AbsolutePath -ne '/'){throw 'Use an HTTP(S) origin without paths or credentials'}
$process=Start-Process -FilePath $player -ArgumentList @('-batchmode','-nographics','-screen-width',[string]$Width,'-screen-height',[string]$Height,'-rf-touch-controls','-rf-touch-smoke','-rf-server',$uri.GetLeftPart([UriPartial]::Authority),'-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
try{
    $deadline=[DateTime]::UtcNow.AddSeconds(130)
    while(-not $process.WaitForExit(1000)){if([DateTime]::UtcNow -gt $deadline){throw 'Touch Player test timeout'}}
    Select-String -LiteralPath $log -Pattern 'RF_TOUCH_PLAYER STEP|RF_TOUCH_PLAYER PASS|RF_TOUCH_PLAYER FAIL' | ForEach-Object Line
    if($process.ExitCode -ne 0 -or -not(Select-String -LiteralPath $log -SimpleMatch 'RF_TOUCH_PLAYER PASS' -Quiet)){throw "Touch Player verification failed: $log"}
}finally{if(-not $process.HasExited){Stop-Process -Id $process.Id}}
