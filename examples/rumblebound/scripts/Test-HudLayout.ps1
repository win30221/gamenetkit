<#
.SYNOPSIS
產生桌面或觸控版 HUD 的實際 Player 渲染圖。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：已建置的 Windows Player、圖形環境與可用遊戲服務。
行為與產物：預設連本機 18093；-Origin 可指定站台，輸出 artifacts/hud-review。
.EXAMPLE
./examples/rumblebound/scripts/Test-HudLayout.ps1
#>
#requires -Version 7.0
param([string]$Origin='http://127.0.0.1:18093',[switch]$Touch)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$root=$script:GameNetKitRoot
$player=Join-Path $root 'examples/rumblebound/client/Builds/Windows/RumbleboundExample.exe'
$variant=if($Touch){'touch'}else{'desktop'}
$output=Join-Path $root "artifacts/hud-review/$variant"
New-Item -ItemType Directory -Force $output | Out-Null
$log=Join-Path $root "artifacts/hud-review-$variant.log"
$arguments=@('-screen-fullscreen','0','-screen-width','1000','-screen-height','446','-rf-hud-review','-rf-server',$Origin,'-rf-hud-output',('"'+$output+'"'),'-logFile',('"'+$log+'"'))
if($Touch){$arguments+='-rf-touch-controls'}
$process=Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
try{
    $deadline=[DateTime]::UtcNow.AddSeconds(140)
    while(-not $process.WaitForExit(1000)){if([DateTime]::UtcNow -gt $deadline){throw "HUD review timeout: $log"}}
    Select-String -LiteralPath $log -Pattern 'RF_HUD_REVIEW' | ForEach-Object Line
    if($process.ExitCode -ne 0 -or -not(Select-String -LiteralPath $log -SimpleMatch 'RF_HUD_REVIEW PASS' -Quiet)){Get-Content $log -Tail 20;throw "HUD review failed: $log"}
}finally{if(-not $process.HasExited){Stop-Process -Id $process.Id}}
