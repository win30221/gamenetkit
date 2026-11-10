<#
.SYNOPSIS
啟動完整遊戲的本機 API 與診斷主機。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 與 .NET SDK；Unity 模式需先建置對應 Player。
行為與產物：服務保持執行至 Ctrl+C；-BuildOnly 僅編譯本機主機與 API。
.EXAMPLE
./examples/rumblebound/scripts/Run.ps1 -BuildOnly
#>
param(
    [string]$Http='127.0.0.1:18093',
    [ValidateRange(1024,65535)][int]$BackendPort=18179,
    [switch]$BuildOnly,
    [switch]$Unity,
    [switch]$WebTransport,
    [ValidateRange(1024,65535)][int]$WebTransportPort=18493,
    [string]$ReplayDirectory='',
    [ValidatePattern('^[A-Za-z0-9-]+$')][string]$BuildTag='run',
    [string]$DotNet=''
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$lesson=Split-Path -Parent $PSScriptRoot
$framework=[IO.Path]::GetFullPath((Join-Path $lesson '../..'))
$out=Join-Path $framework ".local/rumblebound/battle-$BuildTag"
New-Item -ItemType Directory -Force $out | Out-Null
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name.Split('-')[0]} -Descending | Select-Object -First 1
$refs=@()
foreach($packName in @('Microsoft.NETCore.App.Ref','Microsoft.AspNetCore.App.Ref')) {
    $pack=Get-ChildItem (Join-Path $sdkRoot "packs/$packName") -Directory -Filter '8.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
    if(-not $pack){throw 'Use -DotNet with a .NET 8 SDK including ASP.NET Core'}
    $refs+=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:'+$_.FullName})
}
$sources=@(Get-ChildItem (Join-Path $lesson 'browser-host') -Filter '*.cs'|ForEach-Object FullName)
$sources+=Join-Path $framework 'examples/browser-common/UnityRoutes.cs'
foreach($folder in @('examples/rumblebound/game/unity/Runtime','examples/rumblebound/client/Assets/Scripts/Battle')+@('core','transport','interpolation','prediction','correction','clock','replication','actions','codec','diagnostics','simulation','playback'|ForEach-Object{"packages/unity/$_/Runtime"})) {
    $sources+=@(Get-ChildItem (Join-Path $framework $folder) -Filter '*.cs'|ForEach-Object FullName)
}
$savedDotnetHome=$env:DOTNET_CLI_HOME
$server=$null
$player=$null
$webHost=$null
$serverStdout=$null;$serverStderr=$null;$copyStdout=$null;$copyStderr=$null
try {
    $env:DOTNET_CLI_HOME=Join-Path $out 'dotnet-home'
    $compilerArgs=@('/nologo','/nostdlib','/target:exe','/langversion:latest',('-out:"'+(Join-Path $out 'RumbleboundHost.dll')+'"'))
    $compilerArgs+=@($refs | ForEach-Object {'-r:"'+$_.Substring(3)+'"'})
    $compilerArgs+=@($sources | ForEach-Object {'"'+$_+'"'})
    $responseFile=Join-Path $out 'compile.rsp'
    $compilerArgs | Set-Content -LiteralPath $responseFile -Encoding utf8
    & $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$responseFile"
    if($LASTEXITCODE -ne 0){throw 'Rumblebound host compilation failed'}
    '{"runtimeOptions":{"tfm":"net8.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.0"},{"name":"Microsoft.AspNetCore.App","version":"8.0.0"}],"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $out 'RumbleboundHost.runtimeconfig.json') -Encoding utf8
    $backend=Join-Path $out 'battle-api.exe'
    $world=Join-Path $lesson 'server'
    Push-Location $world
    try {& $Go build -buildvcs=false -o $backend ./cmd/battle;if($LASTEXITCODE -ne 0){throw 'World build failed'}} finally {Pop-Location}
    if($BuildOnly){return}
    $helper='off'
    if($WebTransport){$helper=& "$framework/tools/Build-NativeTransport.ps1";if($LASTEXITCODE -ne 0){throw 'WebTransport helper build failed'}}
    $uri=[uri]("http://"+$Http)
    if(-not $uri.IsLoopback -or $uri.Port -eq $BackendPort){throw 'Use a loopback HTTP address and a different backend port'}
    if($WebTransport -and $WebTransportPort -in @($uri.Port,$BackendPort)){throw 'WebTransport port must differ from web and API ports'}
    foreach($port in (@($uri.Port,$BackendPort)+@($WebTransportPort|Where-Object {$WebTransport}))) {
        $probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
        try{$probe.Start()} finally{$probe.Stop()}
    }
    $serverArguments=@('-http',"127.0.0.1:$BackendPort",'-origins',($uri.GetLeftPart([UriPartial]::Authority)),'-stop-on-stdin')
    if($WebTransport){$serverArguments+=@('-wt',"127.0.0.1:$WebTransportPort",'-cert-dir',(Join-Path $out 'tls'))}
    if($ReplayDirectory){$journal=[IO.Path]::GetFullPath((Join-Path $framework $ReplayDirectory));$serverArguments+=@('-replay-dir',$journal)}
    $start=[Diagnostics.ProcessStartInfo]::new($backend);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$world
    $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in $serverArguments){$start.ArgumentList.Add($argument)}
    $server=[Diagnostics.Process]::new();$server.StartInfo=$start;$null=$server.Start()
    $serverStdout=[IO.FileStream]::new((Join-Path $out 'world.stdout.log'),[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read);$serverStderr=[IO.FileStream]::new((Join-Path $out 'world.stderr.log'),[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $copyStdout=$server.StandardOutput.BaseStream.CopyToAsync($serverStdout);$copyStderr=$server.StandardError.BaseStream.CopyToAsync($serverStderr)
    $ready=$false
    for($i=0;$i -lt 40;$i++) {
        if($server.HasExited){throw "World server exited; see $out/world.stderr.log"}
        try{$null=Invoke-WebRequest "http://127.0.0.1:$BackendPort/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}
    }
    if(-not $ready){throw 'World server did not become ready'}
    if($Unity){
        $playerPath=Join-Path $lesson 'client/Builds/Windows/RumbleboundExample.exe'
        if(-not(Test-Path -LiteralPath $playerPath)){throw 'Build the Unity player first: ./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Windows'}
        $stamp=Join-Path (Split-Path -Parent $playerPath) 'rf-battle-build.txt'
        if(-not(Test-Path -LiteralPath $stamp) -or (Get-Content -LiteralPath $stamp -Raw).Trim() -ne 'Rumblebound.Example.Battle v1'){throw 'This player predates the battle scene. Rebuild RumbleboundExample before using -Unity.'}
        $webHost=Start-Process -FilePath $DotNet -ArgumentList @(('"'+(Join-Path $out 'RumbleboundHost.dll')+'"'),$uri.AbsoluteUri,"http://127.0.0.1:$BackendPort",('"'+(Join-Path $lesson 'web')+'"'),('"'+$helper+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'web.stdout.log') -RedirectStandardError (Join-Path $out 'web.stderr.log')
        $ready=$false;for($i=0;$i -lt 40;$i++){if($webHost.HasExited){throw 'Web host exited before Unity launch'};try{$null=Invoke-WebRequest ($uri.AbsoluteUri.TrimEnd('/')+'/healthz') -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}}
        if(-not $ready){throw 'Web host did not become ready'}
        $playerArguments=@('-rf-server',$uri.GetLeftPart([UriPartial]::Authority))
        if($WebTransport){$playerArguments+=@('-rf-native-transport',('"'+$helper+'"'))}
        $player=Start-Process -FilePath $playerPath -ArgumentList $playerArguments -PassThru
        while(-not $webHost.WaitForExit(1000)){if($server.HasExited){throw 'Battle API exited'};if($player.HasExited){break}}
        return
    }
    & $DotNet (Join-Path $out 'RumbleboundHost.dll') $uri.AbsoluteUri "http://127.0.0.1:$BackendPort" (Join-Path $lesson 'web') $helper
    if($LASTEXITCODE -ne 0){throw 'Rumblebound host exited with an error'}
} finally {
    if($player -and -not $player.HasExited){Stop-Process -Id $player.Id}
    if($webHost -and -not $webHost.HasExited){Stop-Process -Id $webHost.Id}
    if($server -and -not $server.HasExited){try{$server.StandardInput.WriteLine('stop');$server.StandardInput.Close()}catch{};if(-not $server.WaitForExit(7000)){Stop-Process -Id $server.Id}}
    if($copyStdout){$null=$copyStdout.GetAwaiter().GetResult()};if($copyStderr){$null=$copyStderr.GetAwaiter().GetResult()};if($serverStdout){$serverStdout.Dispose()};if($serverStderr){$serverStderr.Dispose()}
    $env:DOTNET_CLI_HOME=$savedDotnetHome
}
