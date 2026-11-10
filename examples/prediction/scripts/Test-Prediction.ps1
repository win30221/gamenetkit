<#
.SYNOPSIS
驗證輸入預測、權威校正與網頁控制接線。
.DESCRIPTION
分類：預測與校正。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go、.NET 8 SDK 與 Node.js。
行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。
.EXAMPLE
./examples/prediction/scripts/Test-Prediction.ps1
#>
param([ValidateRange(1024,65535)][int]$Port=18112,[ValidateRange(1024,65535)][int]$WebPort=18113,[string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
& "$PSScriptRoot/Run.ps1" -BuildOnly -BuildTag tests -DotNet $DotNet
$lesson=Split-Path -Parent $PSScriptRoot
$framework=[IO.Path]::GetFullPath((Join-Path $lesson '../..'))
$out=Join-Path $framework '.local/prediction/tests'
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object Name -Descending | Select-Object -First 1
$pack=Get-ChildItem (Join-Path $sdkRoot 'packs/Microsoft.NETCore.App.Ref') -Directory -Filter '8.*' | Sort-Object Name -Descending | Select-Object -First 1
$refs=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:'+$_.FullName})
$sources=@((Join-Path $lesson 'tests/Program.cs'))
foreach($folder in @((Join-Path $lesson 'client/Assets/Scripts'),(Join-Path $framework 'packages/unity/core/Runtime'),(Join-Path $framework 'packages/unity/transport/Runtime'),(Join-Path $framework 'packages/unity/prediction/Runtime'),(Join-Path $framework 'packages/unity/correction/Runtime'),(Join-Path $framework 'packages/unity/interpolation/Runtime'),(Join-Path $framework 'packages/unity/simulation/Runtime'))) {
    $sources+=@(Get-ChildItem $folder -Filter '*.cs' | ForEach-Object FullName)
}
& $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') /nologo /nostdlib /target:exe /langversion:latest @refs "-out:$out/PredictionTests.dll" @sources
if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $out 'PredictionTests.runtimeconfig.json') -Encoding utf8
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
try{$probe.Start()}finally{$probe.Stop()}
$server=$null
$hostProcess=$null
try {
    $server=Start-Process -FilePath (Join-Path $out 'prediction-world.exe') -ArgumentList @('-http',"127.0.0.1:$Port") -WorkingDirectory (Join-Path $framework 'examples/prediction/server') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'test-world.stdout.log') -RedirectStandardError (Join-Path $out 'test-world.stderr.log')
    $ready=$false
    for($i=0;$i -lt 40;$i++) {
        if($server.HasExited){throw 'Test world exited'}
        try{$null=Invoke-WebRequest "http://127.0.0.1:$Port/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}
    }
    if(-not $ready){throw 'Test world did not become ready'}
    & $DotNet (Join-Path $out 'PredictionTests.dll') "http://127.0.0.1:$Port"
    if($LASTEXITCODE -ne 0){throw 'Prediction tests failed'}
    if($Port -eq $WebPort){throw 'World and web ports must differ'}
    $probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$WebPort)
    try{$probe.Start()}finally{$probe.Stop()}
    $hostProcess=Start-Process -FilePath $DotNet -ArgumentList @(('"'+(Join-Path $out 'PredictionHost.dll')+'"'),"http://127.0.0.1:$WebPort","http://127.0.0.1:$Port",('"'+(Join-Path $lesson 'web')+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'test-host.stdout.log') -RedirectStandardError (Join-Path $out 'test-host.stderr.log')
    $ready=$false
    for($i=0;$i -lt 40;$i++) {
        if($hostProcess.HasExited){throw 'Test browser host exited'}
        try{$null=Invoke-WebRequest "http://127.0.0.1:$WebPort/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}
    }
    if(-not $ready){throw 'Test browser host did not become ready'}
    & node (Join-Path $framework 'examples/browser-common/Test-UnityRoutes.mjs') --server "http://127.0.0.1:$WebPort"
    if($LASTEXITCODE -ne 0){throw 'Unity raw transport routes failed'}
    & node (Join-Path $lesson 'tests/browser-source.mjs') --server "http://127.0.0.1:$WebPort"
    if($LASTEXITCODE -ne 0){throw 'Browser-source integration failed'}
} finally {
    if($hostProcess -and -not $hostProcess.HasExited){Stop-Process -Id $hostProcess.Id}
    if($server -and -not $server.HasExited){Stop-Process -Id $server.Id}
}
