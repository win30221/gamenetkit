<#
.SYNOPSIS
用真正雙 Windows Player 驗證完整遊戲與 journal。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：先建置 Rumblebound Windows；需要 Go 與 .NET SDK。
行為與產物：建立隔離服務與 Player；預設驗證 WS，可用 -WebTransport 驗證 WT。
.EXAMPLE
./examples/rumblebound/scripts/Test-BattlePlayer.ps1
#>
param([ValidateRange(1024,65535)][int]$Port=18230,[ValidateRange(1024,65535)][int]$WebPort=18231,[switch]$WebTransport,[ValidateRange(1024,65535)][int]$WebTransportPort=18495,[switch]$Render,[string]$PlayerPath='',[string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot;$lesson=Join-Path $root 'examples/rumblebound'
$playerPath=if($PlayerPath){[IO.Path]::GetFullPath($PlayerPath)}else{Join-Path $lesson 'client/Builds/Windows/RumbleboundExample.exe'}
if(-not(Test-Path -LiteralPath $playerPath)){throw 'Build RumbleboundExample Windows first'}
$managed=Join-Path (Split-Path -Parent $playerPath) (([IO.Path]::GetFileNameWithoutExtension($playerPath))+'_Data/Managed')
$installed=@(Get-ChildItem -LiteralPath $managed -Filter 'Realtime.*.dll'|ForEach-Object Name)
$variant=if($installed.Count -eq 2 -and $installed -contains 'Realtime.Core.dll' -and $installed -contains 'Realtime.Transport.dll'){'core'}elseif($installed.Count -eq 12){'full'}else{'partial'}
& "$lesson/scripts/Run.ps1" -BuildOnly -BuildTag player-tests -DotNet $DotNet
$out=Join-Path $root '.local/rumblebound/battle-player-tests';$run=Join-Path $out ([Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Force $run|Out-Null
$journals=Join-Path $run 'replays';$room=Join-Path $run 'room-code.txt';$helper='off'
$renderDirectory=Join-Path $run 'rendered';if($Render){New-Item -ItemType Directory -Force $renderDirectory|Out-Null}
if($WebTransport){$helper=& "$PSScriptRoot/../../../tools/Build-NativeTransport.ps1"}
foreach($number in @($Port,$WebPort)+@($WebTransportPort|Where-Object {$WebTransport})){$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$number);try{$probe.Start()}finally{$probe.Stop()}}
$server=$null;$web=$null;$clients=@();$stdout=$null;$stderr=$null;$copyOut=$null;$copyError=$null
try{
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $out 'battle-api.exe'));$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $arguments=@('-http',"127.0.0.1:$Port",'-origins',"http://127.0.0.1:$WebPort",'-replay-dir',$journals,'-stop-on-stdin')
    if($WebTransport){$arguments+=@('-wt',"127.0.0.1:$WebTransportPort",'-cert-dir',(Join-Path $run 'tls'))}
    foreach($value in $arguments){$start.ArgumentList.Add($value)};$server=[Diagnostics.Process]::new();$server.StartInfo=$start;$null=$server.Start()
    $stdout=[IO.FileStream]::new((Join-Path $run 'server.stdout.log'),[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read);$stderr=[IO.FileStream]::new((Join-Path $run 'server.stderr.log'),[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $copyOut=$server.StandardOutput.BaseStream.CopyToAsync($stdout);$copyError=$server.StandardError.BaseStream.CopyToAsync($stderr)
    $web=Start-Process -FilePath $DotNet -ArgumentList @(('"'+(Join-Path $out 'RumbleboundHost.dll')+'"'),"http://127.0.0.1:$WebPort","http://127.0.0.1:$Port",('"'+(Join-Path $lesson 'web')+'"'),('"'+$helper+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'web.stdout.log') -RedirectStandardError (Join-Path $run 'web.stderr.log')
    foreach($number in @($Port,$WebPort)){$ready=$false;for($i=0;$i -lt 40;$i++){if($server.HasExited -or $web.HasExited){throw 'Player test service exited'};try{$null=Invoke-WebRequest "http://127.0.0.1:$number/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}};if(-not $ready){throw 'Player test service timeout'}}
    foreach($role in @('host','peer')){
        $log=Join-Path $root ("artifacts/battle-player-$role.log");$arguments=@('-batchmode','-nographics','-rf-battle-smoke','-rf-battle-role',$role,'-rf-room-file',('"'+$room+'"'),'-rf-server',"http://127.0.0.1:$WebPort",'-logFile',('"'+$log+'"'))
        if($Render){$arguments=@($arguments|Where-Object {$_ -notin @('-batchmode','-nographics')});$arguments+=@('-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-rf-render-directory',('"'+$renderDirectory+'"'))}
        if($WebTransport){$arguments+=@('-rf-native-transport',('"'+$helper+'"'),'-rf-use-webtransport')}
        $client=Start-Process -FilePath $playerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru;$clients+=@{Process=$client;Log=$log}
    }
    $deadline=[DateTime]::UtcNow.AddSeconds(150)
    while(@($clients|Where-Object {-not $_.Process.HasExited}).Count -gt 0){if([DateTime]::UtcNow -ge $deadline){throw 'Battle Player scenario timeout'};if(@($clients|Where-Object {$_.Process.HasExited -and $_.Process.ExitCode -ne 0}).Count -gt 0){throw 'Battle Player failed; inspect artifacts/battle-player-host.log and peer.log'};Start-Sleep -Milliseconds 200}
    foreach($client in $clients){if($client.Process.ExitCode -ne 0 -or -not(Select-String -LiteralPath $client.Log -SimpleMatch 'RF_BATTLE_PLAYER PASS' -Quiet) -or (Select-String -LiteralPath $client.Log -Pattern 'Exception:|RF_BATTLE_PLAYER FAIL' -Quiet)){throw "Battle Player failed: $($client.Log)"};Select-String -LiteralPath $client.Log -SimpleMatch 'RF_BATTLE_PLAYER PASS'|ForEach-Object Line}
    if($Render){
        $images=@(Get-ChildItem -LiteralPath $renderDirectory -Filter '*.png');if($images.Count -lt 4){throw 'Expected battle/practice/result camera render artifacts'}
        $destination=Join-Path $root $(if($variant -eq 'full'){'artifacts/battle-render'}else{"artifacts/battle-$variant-render"});New-Item -ItemType Directory -Force $destination|Out-Null
        foreach($file in $images){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name)}
        @{variant=$variant;player=$playerPath;assemblies=$installed;images=@($images.Name);timeUTC=[DateTime]::UtcNow.ToString('O');scope='offscreen 3D only; no UI/input claim'}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
        Write-Output "BATTLE_RENDER PASS $($images.Count) actual engine camera PNGs: $destination (offscreen 3D; UI/manual input remain separate)"
    }
}finally{
    foreach($client in $clients){if(-not $client.Process.HasExited){Stop-Process -Id $client.Process.Id}};if($web -and -not $web.HasExited){Stop-Process -Id $web.Id}
    if($server -and -not $server.HasExited){try{$server.StandardInput.WriteLine('stop');$server.StandardInput.Close()}catch{};if(-not $server.WaitForExit(7000)){Stop-Process -Id $server.Id}}
    if($copyOut){$null=$copyOut.GetAwaiter().GetResult()};if($copyError){$null=$copyError.GetAwaiter().GetResult()};if($stdout){$stdout.Dispose()};if($stderr){$stderr.Dispose()}
}
if($server.ExitCode -ne 0){throw 'API did not complete graceful shutdown'}
$files=@(Get-ChildItem -LiteralPath $journals -Filter '*.replay.jsonl')
if($files.Count -lt 2 -or @(Get-ChildItem -LiteralPath $journals -Filter '*.partial').Count -gt 0){throw 'Launcher shutdown did not finalize journals'}
Push-Location (Join-Path $root 'examples/rumblebound/game')
try{foreach($file in $files){& $Go run -buildvcs=false ./cmd/replay -file $file.FullName;if($LASTEXITCODE -ne 0){throw 'Native Player server journal replay verification failed'}}}finally{Pop-Location}
Write-Output 'BATTLE_PLAYER_AND_JOURNALS PASS actual Unity runtime; private match/result, practice damage/death/respawn, SDK recovery, recording and installed playback, graceful parent-stdin shutdown and verified server journals'
