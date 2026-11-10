<#
.SYNOPSIS
驗證七個教學範例的雙 Windows Player 與 Go 服務。
.DESCRIPTION
分類：跨範例驗證。所有命令從 gamenetkit 根目錄執行。
前提：先建置所選範例的 Windows Player；需要釘選 Go。
行為與產物：為每個範例建立隔離服務與兩個 Player，日誌存 artifacts。
.EXAMPLE
./examples/tests/Test-UnityPlayers.ps1 -Projects Prediction
#>
param([ValidateSet('Prediction','NetworkRecovery','ObjectsActions','Replay','Interest','Codec','History')][string[]]$Projects=@('Prediction','NetworkRecovery','ObjectsActions','Replay','Interest','Codec','History'),[ValidateRange(1024,65520)][int]$FirstPort=18210)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot
$catalog=@{Prediction=@('prediction','Prediction');NetworkRecovery=@('network-recovery','NetworkRecovery');ObjectsActions=@('objects-actions','ObjectsActions');Replay=@('replay','Replay');Interest=@('interest','Interest');Codec=@('codec','Codec');History=@('history-lagcomp','History')}
$port=$FirstPort
foreach($name in $Projects){
    $entry=$catalog[$name];$folder=Join-Path $root ('examples/'+$entry[0]);$playerPath=Join-Path $folder ('client/Builds/Windows/'+$entry[1]+'Demo.exe')
    if(-not(Test-Path -LiteralPath $playerPath)){throw "Build $name Windows first"}
    $out=Join-Path $root ('.local/lesson-players/'+$entry[0]);New-Item -ItemType Directory -Force $out|Out-Null
    $binary=Join-Path $out 'server.exe';$server=$null;$clients=@();$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port);try{$probe.Start()}finally{$probe.Stop()}
    Push-Location (Join-Path $folder 'server')
    try{& $Go build -buildvcs=false -o $binary .;if($LASTEXITCODE -ne 0){throw 'Lesson server build failed'}}finally{Pop-Location}
    try{
        $server=Start-Process -FilePath $binary -ArgumentList @('-http',"127.0.0.1:$port") -WorkingDirectory (Join-Path $folder 'server') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'server.stdout.log') -RedirectStandardError (Join-Path $out 'server.stderr.log')
        $ready=$false;for($i=0;$i -lt 40;$i++){if($server.HasExited){throw 'Lesson server exited'};try{$null=Invoke-WebRequest "http://127.0.0.1:$port/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}};if(-not $ready){throw 'Lesson server timeout'}
        foreach($index in 1,2){$log=Join-Path $root ("artifacts/player-$($entry[0])-$index.log");$client=Start-Process -FilePath $playerPath -ArgumentList @('-batchmode','-nographics','-rf-lesson-smoke','-rf-server',"ws://127.0.0.1:$port/ws",'-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru;$clients+=@{Process=$client;Log=$log}}
        foreach($client in $clients){if(-not $client.Process.WaitForExit(30000)){throw "Lesson Player timeout: $($client.Log)"};if($client.Process.ExitCode -ne 0 -or -not(Select-String -LiteralPath $client.Log -SimpleMatch 'RF_LESSON_PLAYER PASS' -Quiet) -or (Select-String -LiteralPath $client.Log -Pattern 'Exception:|RF_LESSON_PLAYER FAIL' -Quiet)){throw "Lesson Player failed: $($client.Log)"};Select-String -LiteralPath $client.Log -SimpleMatch 'RF_LESSON_PLAYER PASS'|ForEach-Object Line}
    }finally{foreach($client in $clients){if(-not $client.Process.HasExited){Stop-Process -Id $client.Process.Id}};if($server -and -not $server.HasExited){Stop-Process -Id $server.Id}}
    $port++
}
