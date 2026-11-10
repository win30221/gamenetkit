<#
.SYNOPSIS
驗證首份有效快照前不送輸入，以及終止狀態後停止送出。
.DESCRIPTION
分類：Minimal 驗證。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 與 .NET 8 SDK。
行為與產物：編譯實際 MinimalClient 與測試替身，使用真實 WebSocket；不代表 Player 渲染。
.EXAMPLE
./examples/minimal-unity/tests/Test-Startup.ps1
#>
param([string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
$root=$script:GameNetKitRoot
$out=Join-Path $root '.local/transport-platform/minimal-startup'
New-Item -ItemType Directory -Force $out | Out-Null
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name.Split('-')[0]} -Descending | Select-Object -First 1
$refs=@()
foreach($name in @('Microsoft.NETCore.App.Ref','Microsoft.AspNetCore.App.Ref')){
    $pack=Get-ChildItem (Join-Path $sdkRoot "packs/$name") -Directory -Filter '8.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
    $refs+=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:"'+$_.FullName+'"'})
}
$sources=@((Join-Path $root 'examples/minimal-unity/tests/startup/Program.cs'),(Join-Path $root 'examples/minimal-unity/Assets/MinimalClient.cs'))
foreach($module in @('core','transport')){$sources+=@(Get-ChildItem (Join-Path $root "packages/unity/$module/Runtime") -Filter '*.cs' | ForEach-Object FullName)}
$arguments=@('/nologo','/nostdlib','/langversion:latest','/target:exe',('-out:"'+(Join-Path $out 'MinimalStartupTests.dll')+'"'))+$refs+@($sources | ForEach-Object {'"'+$_+'"'})
$response=Join-Path $out 'compile.rsp';$arguments | Set-Content -LiteralPath $response -Encoding utf8
& $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response"
if($LASTEXITCODE -ne 0){throw 'Minimal startup test compilation failed'}
'{"runtimeOptions":{"tfm":"net8.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.0"},{"name":"Microsoft.AspNetCore.App","version":"8.0.0"}],"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $out 'MinimalStartupTests.runtimeconfig.json') -Encoding utf8
& $DotNet (Join-Path $out 'MinimalStartupTests.dll')
if($LASTEXITCODE -ne 0){throw 'Minimal startup test failed'}
