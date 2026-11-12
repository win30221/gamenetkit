<#
.SYNOPSIS
驗證正式 JavaScript、靜態資源與本機遊戲 API。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：Node.js、Go、.NET SDK 與已建置的 Rumblebound WebGL。
行為與產物：DOM／Canvas 使用測試替身；可選 -WebTransport，並非人工瀏覽器畫面驗收。
.EXAMPLE
./examples/rumblebound/scripts/Test-Browser.ps1 -WebTransport
#>
param([ValidateRange(1024,65535)][int]$Port=18188,[ValidateRange(1024,65535)][int]$WebPort=18187,[switch]$WebTransport,[ValidateRange(1024,65535)][int]$WebTransportPort=18494,[string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
node --test "$PSScriptRoot/../tests/battle/replay-picker.mjs" "$PSScriptRoot/../tests/battle/network-settings.mjs" "$PSScriptRoot/../tests/battle/room-input.mjs"
if($LASTEXITCODE -ne 0){throw 'Replay file picker bridge tests failed'}
& "$PSScriptRoot/Run.ps1" -BuildOnly -BuildTag browser-tests -DotNet $DotNet
$lesson=Split-Path -Parent $PSScriptRoot;$root=[IO.Path]::GetFullPath((Join-Path $lesson '../..'));$out=Join-Path $root '.local/rumblebound/battle-browser-tests'
foreach($value in @($Port,$WebPort)){$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$value);try{$probe.Start()}finally{$probe.Stop()}}
if($Port -eq $WebPort){throw 'Backend and web ports must differ'}
$server=$null;$hostProcess=$null
$previousGoogleClientID=$env:RB_GOOGLE_CLIENT_ID
$env:RB_GOOGLE_CLIENT_ID='fixture.apps.googleusercontent.com'
$helper='off';if($WebTransport){$helper=& "$root/tools/Build-NativeTransport.ps1"}
try{
    $serverArguments=@('-http',"127.0.0.1:$Port",'-origins',"http://127.0.0.1:$WebPort")
    if($WebTransport){$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$WebTransportPort);try{$probe.Start()}finally{$probe.Stop()};$serverArguments+=@('-wt',"127.0.0.1:$WebTransportPort",'-cert-dir',('"'+(Join-Path $out 'tls')+'"'))}
    $server=Start-Process -FilePath (Join-Path $out 'battle-api.exe') -ArgumentList $serverArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'test-server.stdout.log') -RedirectStandardError (Join-Path $out 'test-server.stderr.log')
    $hostProcess=Start-Process -FilePath $DotNet -ArgumentList @(('"'+(Join-Path $out 'RumbleboundHost.dll')+'"'),"http://127.0.0.1:$WebPort","http://127.0.0.1:$Port",('"'+(Join-Path $lesson 'web')+'"'),('"'+$helper+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'test-host.stdout.log') -RedirectStandardError (Join-Path $out 'test-host.stderr.log')
    foreach($processAndPort in @(@($server,$Port),@($hostProcess,$WebPort))){$ready=$false;for($i=0;$i -lt 50;$i++){if($processAndPort[0].HasExited){throw 'Battle test service exited'};try{$null=Invoke-WebRequest "http://127.0.0.1:$($processAndPort[1])/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}};if(-not $ready){throw 'Battle service did not become ready'}}
    $nodeArguments=@((Join-Path $lesson 'tests/battle/browser-source.mjs'),'--server',"http://127.0.0.1:$WebPort");if($WebTransport){$nodeArguments+='--webtransport'}
    node @nodeArguments
    if($LASTEXITCODE -ne 0){throw 'Battle browser source integration failed'}
    # Exercise the missing-build entry with an isolated empty project, without
    # renaming or changing an existing Unity build or claiming its renderer ran.
    Stop-Process -Id $hostProcess.Id;$hostProcess.WaitForExit()
    $missingWeb=Join-Path $out 'missing-unity/web';New-Item -ItemType Directory -Force $missingWeb | Out-Null
    $hostProcess=Start-Process -FilePath $DotNet -ArgumentList @(('"'+(Join-Path $out 'RumbleboundHost.dll')+'"'),"http://127.0.0.1:$WebPort","http://127.0.0.1:$Port",('"'+$missingWeb+'"'),'off') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'test-missing-unity.stdout.log') -RedirectStandardError (Join-Path $out 'test-missing-unity.stderr.log')
    $ready=$false;for($i=0;$i -lt 50;$i++){if($hostProcess.HasExited){throw 'Missing Unity test host exited'};try{$null=Invoke-WebRequest "http://127.0.0.1:$WebPort/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}}
    if(-not $ready){throw 'Missing Unity test host did not become ready'}
    node (Join-Path $lesson 'tests/battle/missing-unity-source.mjs') --server "http://127.0.0.1:$WebPort"
    if($LASTEXITCODE -ne 0){throw 'Missing Unity entry integration failed'}
}finally{if($hostProcess -and -not $hostProcess.HasExited){Stop-Process -Id $hostProcess.Id};if($server -and -not $server.HasExited){Stop-Process -Id $server.Id};$env:RB_GOOGLE_CLIENT_ID=$previousGoogleClientID}
