<#
.SYNOPSIS
驗證完整 Go API 與正式 C# 流程、SDK、重播。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 與 .NET SDK。
行為與產物：預設建立隔離本機 API；-Origin 可改為指定現有服務。
.EXAMPLE
./examples/rumblebound/scripts/Test-Battle.ps1
#>
param([ValidateRange(1024,65535)][int]$Port=18186,[string]$Origin='',[string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$lesson=Split-Path -Parent $PSScriptRoot;$root=[IO.Path]::GetFullPath((Join-Path $lesson '../..'))
$out=Join-Path $root '.local/rumblebound/battle-tests';New-Item -ItemType Directory -Force $out|Out-Null
$playback=Join-Path $out 'fixture.playback.json'
$previousFixture=$env:RF_PLAYBACK_FIXTURE
Push-Location (Join-Path $lesson 'game')
try {
    $env:RF_PLAYBACK_FIXTURE=$playback
    & $Go test -count=1 -run '^TestPlaybackExportVerifiedFullWorldAndMapIdentity$' ./sim
    if($LASTEXITCODE -ne 0){throw 'Playback integration fixture generation failed'}
} finally { $env:RF_PLAYBACK_FIXTURE=$previousFixture; Pop-Location }
if($Origin){
    $uri=[uri]$Origin
    if($uri.Scheme -notin @('http','https') -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.UserInfo){throw 'Origin must be an HTTP(S) origin without a path or credentials'}
    $Origin=$uri.GetLeftPart([UriPartial]::Authority)
}else{
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port);try{$probe.Start()}finally{$probe.Stop()}
$backend=Join-Path $out 'battle-api.exe'
Push-Location (Join-Path $lesson 'server')
try{& $Go build -buildvcs=false -o $backend ./cmd/battle;if($LASTEXITCODE -ne 0){throw 'Battle API build failed'}}finally{Pop-Location}
}
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*'|Sort-Object {[version]$_.Name.Split('-')[0]} -Descending|Select-Object -First 1
$pack=Get-ChildItem (Join-Path $sdkRoot 'packs/Microsoft.NETCore.App.Ref') -Directory -Filter '8.*'|Sort-Object {[version]$_.Name} -Descending|Select-Object -First 1
$refs=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll'|ForEach-Object {'-r:"'+$_.FullName+'"'})
$sources=@(Get-ChildItem (Join-Path $lesson 'tests/battle') -Filter '*.cs'|ForEach-Object FullName)
$sources+=Join-Path $lesson 'browser-host/HttpGameApi.cs'
foreach($folder in @('examples/rumblebound/game/unity/Runtime','examples/rumblebound/client/Assets/Scripts/Battle')+@('core','transport','interpolation','prediction','correction','clock','replication','actions','codec','diagnostics','simulation','playback'|ForEach-Object{"packages/unity/$_/Runtime"})){$sources+=@(Get-ChildItem (Join-Path $root $folder) -Filter '*.cs'|ForEach-Object FullName)}
$arguments=@('/nologo','/nostdlib','/target:exe','/langversion:latest',('-out:"'+(Join-Path $out 'BattleTests.dll')+'"'))+$refs+@($sources|ForEach-Object{'"'+$_+'"'})
$response=Join-Path $out 'compile.rsp';$arguments|Set-Content -LiteralPath $response -Encoding utf8
& $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response";if($LASTEXITCODE -ne 0){throw 'Battle client test compilation failed'}
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}'|Set-Content -LiteralPath (Join-Path $out 'BattleTests.runtimeconfig.json') -Encoding utf8
# Generate the real Go replay fixture from the current game instead of relying on the removed sample's installed recording.
$fixtureRoot=Join-Path $out ('fixtures/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $fixtureRoot | Out-Null
$playback=Join-Path $fixtureRoot 'demo.playback.json'
Push-Location (Join-Path $lesson 'game')
try{
    $recordingJson=& $Go run -buildvcs=false ./cmd/replaydemo -dir (Join-Path $fixtureRoot 'replays') -seconds 3
    if($LASTEXITCODE -ne 0){throw 'Replay fixture recording failed'}
    $recording=($recordingJson -join "`n")|ConvertFrom-Json
    & $Go run -buildvcs=false ./cmd/replay -file $recording.file -export $playback | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Replay fixture export failed'}
}finally{Pop-Location}
$server=$null
try{
    if(-not $Origin){
    $server=Start-Process -FilePath $backend -ArgumentList @('-http',"127.0.0.1:$Port") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'server.stdout.log') -RedirectStandardError (Join-Path $out 'server.stderr.log')
    $ready=$false;for($i=0;$i -lt 40;$i++){if($server.HasExited){throw 'Battle API exited'};try{$null=Invoke-WebRequest "http://127.0.0.1:$Port/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}}
    if(-not $ready){throw 'Battle API startup timed out'}
    $Origin="http://127.0.0.1:$Port"
    }
    & $DotNet (Join-Path $out 'BattleTests.dll') $Origin $playback;if($LASTEXITCODE -ne 0){throw 'Battle native integration failed'}
}finally{if($server -and -not $server.HasExited){Stop-Process -Id $server.Id}}
