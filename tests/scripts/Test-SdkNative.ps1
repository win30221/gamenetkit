param([string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
$root=$script:GameNetKitRoot
$out=Join-Path $root '.local/sdk-native'
New-Item -ItemType Directory -Force $out | Out-Null
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name.Split('-')[0]} -Descending | Select-Object -First 1
$pack=Get-ChildItem (Join-Path $sdkRoot 'packs/Microsoft.NETCore.App.Ref') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$refs=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:"'+$_.FullName+'"'})
$sources=@((Join-Path $root 'tests/sdk-native/Program.cs'))
$sources+=@(Get-ChildItem (Join-Path $root 'tests/sdk-unity/Assets/Editor') -Filter '*.cs' | ForEach-Object FullName)
foreach($module in @('core','transport','interpolation','prediction','correction','clock','replication','actions','codec','diagnostics','simulation','playback')) {
    $sources+=@(Get-ChildItem (Join-Path $root "packages/unity/$module/Runtime") -Filter '*.cs' | ForEach-Object FullName)
}
$arguments=@('/nologo','/nostdlib','/langversion:latest','/target:exe',('-out:"'+(Join-Path $out 'SdkTests.dll')+'"'))+$refs+@($sources | ForEach-Object {'"'+$_+'"'})
$response=Join-Path $out 'compile.rsp'
$arguments | Set-Content -LiteralPath $response -Encoding utf8
& $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response"
if($LASTEXITCODE -ne 0){throw 'Native SDK test compilation failed'}
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $out 'SdkTests.runtimeconfig.json') -Encoding utf8
Push-Location (Join-Path $root 'tests/sdk-unity')
try {
    & $DotNet (Join-Path $out 'SdkTests.dll')
    if($LASTEXITCODE -ne 0){throw 'Native SDK contract tests failed'}
} finally { Pop-Location }
