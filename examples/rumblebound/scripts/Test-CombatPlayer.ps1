<#
.SYNOPSIS
驗證實際 Player 的戰鬥、移動與操作。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：先建置 Rumblebound Windows，並準備釘選 Go。
行為與產物：預設建立隔離本機 API；-Review 額外產生 HUD 渲染圖。
.EXAMPLE
./examples/rumblebound/scripts/Test-CombatPlayer.ps1
#>
#requires -Version 7.0
param([string]$Origin='',[ValidateRange(1024,65535)][int]$Port=18189,[switch]$Review)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$root=$script:GameNetKitRoot
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$player=Join-Path $root 'examples/rumblebound/client/Builds/Windows/RumbleboundExample.exe'
$out=Join-Path $root '.local/combat-player';New-Item -ItemType Directory -Force $out|Out-Null
$log=Join-Path $root 'artifacts/combat-player.log'
$server=$null;$process=$null
try{
    if(-not $Origin){
        $probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port);try{$probe.Start()}finally{$probe.Stop()}
        $api=Join-Path $out 'battle-api.exe'
        Push-Location (Join-Path $root 'examples/rumblebound/server')
        try{& $Go build -buildvcs=false -o $api ./cmd/battle;if($LASTEXITCODE -ne 0){throw 'API build failed'}}finally{Pop-Location}
        $server=Start-Process -FilePath $api -ArgumentList @('-http',"127.0.0.1:$Port") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'api.stdout.log') -RedirectStandardError (Join-Path $out 'api.stderr.log')
        $Origin="http://127.0.0.1:$Port"
        $ready=$false;for($i=0;$i -lt 50;$i++){if($server.HasExited){throw 'API exited'};try{$null=Invoke-WebRequest "$Origin/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}}
        if(-not $ready){throw 'API startup timeout'}
    }
    $uri=[uri]$Origin;if($uri.Scheme -notin @('http','https') -or $uri.UserInfo -or $uri.Query -or $uri.AbsolutePath -ne '/'){throw 'Use an HTTP(S) origin without path/credentials'}
    $process=Start-Process -FilePath $player -ArgumentList @('-batchmode','-nographics','-screen-width','989','-screen-height','446','-rf-combat-smoke','-rf-server',$Origin,'-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(180)
    while(-not $process.WaitForExit(1000)){if([DateTime]::UtcNow -gt $deadline){throw 'Combat Player timed out'}}
    Select-String -LiteralPath $log -Pattern 'RF_COMBAT_PLAYER' | ForEach-Object Line
    if($process.ExitCode -ne 0 -or -not(Select-String -LiteralPath $log -SimpleMatch 'RF_COMBAT_PLAYER PASS' -Quiet)){Get-Content $log -Tail 20;throw "Combat Player failed: $log"}
    if($Review){& "$PSScriptRoot/Test-HudLayout.ps1" -Origin $Origin;& "$PSScriptRoot/Test-HudLayout.ps1" -Origin $Origin -Touch}
}finally{
    if($process -and -not $process.HasExited){Stop-Process -Id $process.Id}
    if($server -and -not $server.HasExited){Stop-Process -Id $server.Id}
}
