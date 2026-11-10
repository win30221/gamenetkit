<#
.SYNOPSIS
啟動物件與動作確認教學的本機服務。
.DESCRIPTION
分類：物件與動作確認。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。
行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。
.EXAMPLE
./examples/objects-actions/scripts/Run.ps1
#>
param(
    [string]$Http='127.0.0.1:18097',
    [ValidateRange(1024,65535)][int]$BackendPort=18129,
    [switch]$BuildOnly,
    [switch]$Unity,
    [ValidatePattern('^[A-Za-z0-9-]+$')][string]$BuildTag='run',
    [string]$DotNet=''
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$lesson=Split-Path -Parent $PSScriptRoot
$framework=[IO.Path]::GetFullPath((Join-Path $lesson '../..'))
$out=Join-Path $framework ".local/objects-actions/$BuildTag"
New-Item -ItemType Directory -Force $out | Out-Null
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name.Split('-')[0]} -Descending | Select-Object -First 1
$refs=@()
foreach($packName in @('Microsoft.NETCore.App.Ref','Microsoft.AspNetCore.App.Ref')) {
    $pack=Get-ChildItem (Join-Path $sdkRoot "packs/$packName") -Directory -Filter '8.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
    if(-not $pack){throw 'Use -DotNet with a .NET 8 SDK including ASP.NET Core'}
    $refs+=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:'+$_.FullName})
}
$sources=@((Join-Path $lesson 'browser-host/Program.cs'),(Join-Path $framework 'examples/browser-common/UnityRoutes.cs'))
foreach($folder in @((Join-Path $lesson 'client/Assets/Scripts'),(Join-Path $framework 'packages/unity/core/Runtime'),(Join-Path $framework 'packages/unity/transport/Runtime'),(Join-Path $framework 'packages/unity/prediction/Runtime'),(Join-Path $framework 'packages/unity/correction/Runtime'),(Join-Path $framework 'packages/unity/interpolation/Runtime'),(Join-Path $framework 'packages/unity/simulation/Runtime'),(Join-Path $framework 'packages/unity/replication/Runtime'),(Join-Path $framework 'packages/unity/actions/Runtime'))) {
    $sources+=@(Get-ChildItem $folder -Filter '*.cs' | ForEach-Object FullName)
}
$savedDotnetHome=$env:DOTNET_CLI_HOME
$server=$null
$player=$null
try {
    $env:DOTNET_CLI_HOME=Join-Path $out 'dotnet-home'
    $compilerArgs=@('/nologo','/nostdlib','/target:exe','/langversion:latest',('-out:"'+(Join-Path $out 'ObjectsHost.dll')+'"'))
    $compilerArgs+=@($refs | ForEach-Object {'-r:"'+$_.Substring(3)+'"'})
    $compilerArgs+=@($sources | ForEach-Object {'"'+$_+'"'})
    $responseFile=Join-Path $out 'compile.rsp'
    $compilerArgs | Set-Content -LiteralPath $responseFile -Encoding utf8
    & $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$responseFile"
    if($LASTEXITCODE -ne 0){throw 'Objects host compilation failed'}
    '{"runtimeOptions":{"tfm":"net8.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.0"},{"name":"Microsoft.AspNetCore.App","version":"8.0.0"}],"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $out 'ObjectsHost.runtimeconfig.json') -Encoding utf8
    $backend=Join-Path $out 'objects-world.exe'
    $world=Join-Path $lesson 'server'
    Push-Location $world
    try {& $Go build -buildvcs=false -o $backend .;if($LASTEXITCODE -ne 0){throw 'World build failed'}} finally {Pop-Location}
    if($BuildOnly){return}
    $uri=[uri]("http://"+$Http)
    if(-not $uri.IsLoopback -or $uri.Port -eq $BackendPort){throw 'Use a loopback HTTP address and a different backend port'}
    foreach($port in @($uri.Port,$BackendPort)) {
        $probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
        try{$probe.Start()} finally{$probe.Stop()}
    }
    $server=Start-Process -FilePath $backend -ArgumentList @('-http',"127.0.0.1:$BackendPort") -WorkingDirectory $world -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'world.stdout.log') -RedirectStandardError (Join-Path $out 'world.stderr.log')
    $ready=$false
    for($i=0;$i -lt 40;$i++) {
        if($server.HasExited){throw "World server exited; see $out/world.stderr.log"}
        try{$null=Invoke-WebRequest "http://127.0.0.1:$BackendPort/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}
    }
    if(-not $ready){throw 'World server did not become ready'}
    if($Unity){
        $playerPath=Join-Path $lesson 'client/Builds/Windows/ObjectsActionsDemo.exe'
        if(-not(Test-Path -LiteralPath $playerPath)){throw 'Build the Unity player first: ./examples/Build-Unity.ps1 -Project ObjectsActions -Target Windows'}
        $player=Start-Process -FilePath $playerPath -ArgumentList @('-rf-server',"ws://127.0.0.1:$BackendPort/ws") -PassThru
    }
    & $DotNet (Join-Path $out 'ObjectsHost.dll') $uri.AbsoluteUri "http://127.0.0.1:$BackendPort" (Join-Path $lesson 'web')
    if($LASTEXITCODE -ne 0){throw 'Objects host exited with an error'}
} finally {
    if($player -and -not $player.HasExited){Stop-Process -Id $player.Id}
    if($server -and -not $server.HasExited){Stop-Process -Id $server.Id}
    $env:DOTNET_CLI_HOME=$savedDotnetHome
}
