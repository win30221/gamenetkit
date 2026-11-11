<#
.SYNOPSIS
Run native typed Protobuf compatibility and real Go/C# WebSocket integration tests.
.DESCRIPTION
Uses the pinned official Google.Protobuf runtime, bundled .NET 8 SDK and Go toolchain.
Builds a local demo server, starts it hidden on an isolated test port, and stops only that process.
.EXAMPLE
./tests/scripts/Test-Protobuf.ps1
#>
[CmdletBinding()]
param([string]$DotNet='',[ValidateRange(1024,65535)][int]$Port=18103)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
$root=$script:GameNetKitRoot
$out=Join-Path $root '.local/protobuf/tests'
New-Item -ItemType Directory -Force $out | Out-Null
. "$PSScriptRoot/../../tools/internal/Go-Env.ps1"
. "$PSScriptRoot/../../tools/internal/Protobuf.ps1"
$runtime=Join-Path $root 'packages/unity/protobuf-runtime/Runtime/Google.Protobuf.dll'
if(-not(Test-Path -LiteralPath $runtime)){& "$PSScriptRoot/../../tools/Restore-Protobuf.ps1"}
Assert-ProtobufHash $runtime $script:ProtobufLock.csharp.dllSha256
$server=Join-Path $out 'protobuf-server.exe'
$compatibility=Join-Path $out 'protobuf-compatibility.exe'
Push-Location (Join-Path $root 'examples/protobuf/server')
try{
    & $Go build -buildvcs=false -o $server .
    if($LASTEXITCODE -ne 0){throw 'Protobuf example server build failed'}
    & $Go build -buildvcs=false -o $compatibility ./cmd/compatibility
    if($LASTEXITCODE -ne 0){throw 'Go compatibility fixture build failed'}
}finally{Pop-Location}
$goFixture=Join-Path $out 'go-compatibility.bin'
$csharpFixture=Join-Path $out 'csharp-compatibility.bin'
& $compatibility -write $goFixture
if($LASTEXITCODE -ne 0){throw 'Go compatibility fixture write failed'}
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name.Split('-')[0]} -Descending | Select-Object -First 1
if(-not $sdk){throw 'A .NET 8 SDK is required'}
$refs=@()
foreach($name in @('Microsoft.NETCore.App.Ref','Microsoft.AspNetCore.App.Ref')){
    $pack=Get-ChildItem (Join-Path $sdkRoot "packs/$name") -Directory -Filter '8.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
    $refs+=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:"'+$_.FullName+'"'})
}
$refs+='-r:"'+$runtime+'"'
$sources=@((Join-Path $root 'tests/protobuf/Program.cs'),(Join-Path $root 'examples/protobuf/client/Assets/CompatibilityCheck.cs'))
foreach($module in @('transport','protobuf')){$sources+=@(Get-ChildItem (Join-Path $root "packages/unity/$module/Runtime") -Filter '*.cs' | ForEach-Object FullName)}
$sources+=@(Get-ChildItem (Join-Path $root 'examples/protobuf/unity/Runtime/Generated') -Filter '*.cs' | ForEach-Object FullName)
$assembly=Join-Path $out 'ProtobufTests.dll'
$arguments=@('/nologo','/nostdlib','/langversion:latest','/target:exe',('-out:"'+$assembly+'"'))+$refs+@($sources | ForEach-Object {'"'+$_+'"'})
$response=Join-Path $out 'compile.rsp';$arguments | Set-Content -LiteralPath $response -Encoding utf8
& $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response"
if($LASTEXITCODE -ne 0){throw 'Protobuf test compilation failed'}
Copy-Item -LiteralPath $runtime -Destination (Join-Path $out 'Google.Protobuf.dll') -Force
'{"runtimeOptions":{"tfm":"net8.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.0"},{"name":"Microsoft.AspNetCore.App","version":"8.0.0"}],"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $out 'ProtobufTests.runtimeconfig.json') -Encoding utf8
& $DotNet $assembly $server $Port $goFixture $csharpFixture
if($LASTEXITCODE -ne 0){throw 'Native Protobuf integration tests failed'}
& $compatibility -read $csharpFixture
if($LASTEXITCODE -ne 0){throw 'C# -> Go compatibility semantics failed'}
Write-Host 'PROTOBUF_CROSS_LANGUAGE PASS Go <-> C# optional zero presence, repeated uint64 above 2^53, and oneof uint64 max.'
