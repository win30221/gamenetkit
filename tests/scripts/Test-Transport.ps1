<#
.SYNOPSIS
驗證真實 WS／HTTP3 傳輸與 JavaScript browser bridge。
.DESCRIPTION
分類：框架驗證。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go、.NET 8 SDK 與 Node.js。
行為與產物：建立本機測試服務與 helper 子程序；完成後清理自己啟動的程序。
.EXAMPLE
./tests/scripts/Test-Transport.ps1
#>
param([string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
$root=$script:GameNetKitRoot
$out=Join-Path $root '.local/transport-platform/tests'
New-Item -ItemType Directory -Force $out | Out-Null
. "$PSScriptRoot/../../tools/internal/Go-Env.ps1"
$helper=& "$PSScriptRoot/../../tools/Build-NativeTransport.ps1"
$fixture=Join-Path $out 'webtransport-fixture.exe'
Push-Location (Join-Path $root 'native/webtransport')
try{& $Go build -buildvcs=false -o $fixture ./testfixture;if($LASTEXITCODE -ne 0){throw 'WebTransport fixture build failed'}}finally{Pop-Location}
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name.Split('-')[0]} -Descending | Select-Object -First 1
$refs=@()
foreach($name in @('Microsoft.NETCore.App.Ref','Microsoft.AspNetCore.App.Ref')){
    $pack=Get-ChildItem (Join-Path $sdkRoot "packs/$name") -Directory -Filter '8.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
    $refs+=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:"'+$_.FullName+'"'})
}
$sources=@((Join-Path $root 'tests/transport/Program.cs'))
foreach($module in @('core','simulation','transport')){$sources+=@(Get-ChildItem (Join-Path $root "packages/unity/$module/Runtime") -Filter '*.cs' | ForEach-Object FullName)}
$arguments=@('/nologo','/nostdlib','/langversion:latest','/target:exe',('-out:"'+(Join-Path $out 'TransportTests.dll')+'"'))+$refs+@($sources | ForEach-Object {'"'+$_+'"'})
$response=Join-Path $out 'compile.rsp';$arguments | Set-Content -LiteralPath $response -Encoding utf8
& $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response"
if($LASTEXITCODE -ne 0){throw 'Transport test compilation failed'}
'{"runtimeOptions":{"tfm":"net8.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.0"},{"name":"Microsoft.AspNetCore.App","version":"8.0.0"}],"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $out 'TransportTests.runtimeconfig.json') -Encoding utf8
& $DotNet (Join-Path $out 'TransportTests.dll') $helper $fixture
if($LASTEXITCODE -ne 0){throw 'Native transport tests failed'}
node --test "$root/packages/unity/transport/Browser/Tests/*.test.mjs"
if($LASTEXITCODE -ne 0){throw 'SDK browser transport/bridge tests failed'}
