<#
.SYNOPSIS
在隔離來源樹驗證只保留 core／transport 的完整遊戲。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 與 .NET SDK。
行為與產物：建立不含十個選用套件的隔離樹，執行真實本機服務與 C# 流程；不移動正式來源。
.EXAMPLE
./examples/rumblebound/scripts/Test-BattleRemoval.ps1
#>
param([ValidateRange(1024,65535)][int]$Port=18189,[string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot
$out=Join-Path $root ('.local/battle-removal/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $out|Out-Null
$names=@('interpolation','prediction','correction','diagnostics','simulation','clock','replication','actions','codec','playback')
$sources=@()
foreach($folder in @('packages/unity/core/Runtime','packages/unity/transport/Runtime','examples/rumblebound/game/unity/Runtime','examples/rumblebound/client/Assets/Scripts/Battle')){
    $destination=Join-Path $out $folder;New-Item -ItemType Directory -Force $destination|Out-Null
    foreach($file in Get-ChildItem (Join-Path $root $folder) -Filter '*.cs'){
        $target=Join-Path $destination $file.Name;$code=[IO.File]::ReadAllText($file.FullName)
        if($file.Name -eq 'BattleNetcodeModules.cs'){foreach($name in $names){$code=[regex]::Replace($code,"(?ms)^\s*// BEGIN OPTIONAL $name\r?\n.*?^\s*// END OPTIONAL $name[^\r\n]*\r?\n?",'')}}
        [IO.File]::WriteAllText($target,$code);$sources+=$target
    }
}
# This tree is compiled directly: it contains no optional package, assembly,
# manifest reference, copied DLL, Unity cache, or source from those packages.
foreach($name in $names){if(Test-Path -LiteralPath (Join-Path $out "packages/unity/$name")){throw 'Optional source leaked into removal tree'}}
$sources+=@((Join-Path $root 'examples/rumblebound/tests/removal/Program.cs'),(Join-Path $root 'examples/rumblebound/tests/battle/FlowTests.cs'),(Join-Path $root 'examples/rumblebound/browser-host/HttpGameApi.cs'))
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*'|Sort-Object {[version]$_.Name.Split('-')[0]} -Descending|Select-Object -First 1
$pack=Get-ChildItem (Join-Path $sdkRoot 'packs/Microsoft.NETCore.App.Ref') -Directory -Filter '8.*'|Sort-Object {[version]$_.Name} -Descending|Select-Object -First 1
$refs=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll'|ForEach-Object {'-r:"'+$_.FullName+'"'})
$arguments=@('/nologo','/nostdlib','/target:exe','/langversion:latest',('-out:"'+(Join-Path $out 'RemovalTests.dll')+'"'))+$refs+@($sources|ForEach-Object{'"'+$_+'"'})
$response=Join-Path $out 'compile.rsp';$arguments|Set-Content -LiteralPath $response -Encoding utf8
& $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response";if($LASTEXITCODE -ne 0){throw 'Battle core-only compilation failed'}
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}'|Set-Content -LiteralPath (Join-Path $out 'RemovalTests.runtimeconfig.json') -Encoding utf8
$backend=Join-Path $out 'battle-api.exe'
Push-Location (Join-Path $root 'examples/rumblebound/server')
try{& $Go build -buildvcs=false -o $backend ./cmd/battle;if($LASTEXITCODE -ne 0){throw 'Battle API build failed'}}finally{Pop-Location}
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port);try{$probe.Start()}finally{$probe.Stop()}
$server=$null
try{
    $server=Start-Process -FilePath $backend -ArgumentList @('-http',"127.0.0.1:$Port") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'server.stdout.log') -RedirectStandardError (Join-Path $out 'server.stderr.log')
    $ready=$false;for($i=0;$i -lt 40;$i++){if($server.HasExited){throw 'Removal API exited'};try{$null=Invoke-WebRequest "http://127.0.0.1:$Port/healthz" -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}}
    if(-not $ready){throw 'Removal API startup timed out'}
    & $DotNet (Join-Path $out 'RemovalTests.dll') "http://127.0.0.1:$Port";if($LASTEXITCODE -ne 0){throw 'Battle core-only runtime failed'}
}finally{if($server -and -not $server.HasExited){Stop-Process -Id $server.Id}}
