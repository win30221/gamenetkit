<#
.SYNOPSIS
驗證共用世界的加入／離開、玩家輸入與客戶端隔離。
.DESCRIPTION
分類：共用世界。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go、.NET 8 SDK 與 Node.js。
行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。
.EXAMPLE
./examples/shared-world/scripts/Test-SharedWorld.ps1
#>
param([ValidateRange(1024,65535)][int]$Port=18096,[switch]$NativeClient,[string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$exampleRoot=Split-Path -Parent $PSScriptRoot
$frameworkRoot=[IO.Path]::GetFullPath((Join-Path $exampleRoot '../..'))
$work=Join-Path $frameworkRoot '.local/shared-world-teaching'
$artifacts=Join-Path $frameworkRoot 'artifacts'
New-Item -ItemType Directory -Force $work,$artifacts | Out-Null
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
try {$probe.Start()} finally {$probe.Stop()}
$server=$null
$savedDotnetHome=$env:DOTNET_CLI_HOME
Push-Location (Join-Path $exampleRoot 'server')
try {
    $binary=Join-Path $work 'shared-world-test.exe'
    & $Go build -buildvcs=false -o $binary .
    if ($LASTEXITCODE -ne 0) {throw 'Example server build failed'}
    $server=Start-Process -FilePath $binary -ArgumentList @('-http',"127.0.0.1:$Port") -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $artifacts 'shared-world-teaching-server.stdout.log') -RedirectStandardError (Join-Path $artifacts 'shared-world-teaching-server.stderr.log')
    $ready=$false
    for ($i=0;$i -lt 40;$i++) {
        if ($server.HasExited) {throw 'Example server exited before ready'}
        try {$null=Invoke-WebRequest "http://127.0.0.1:$Port/healthz" -TimeoutSec 1;$ready=$true;break} catch {Start-Sleep -Milliseconds 100}
    }
    if (-not $ready) {throw 'Example server did not become ready'}
    & node "$PSScriptRoot/Smoke-SharedWorld.mjs" --server "http://127.0.0.1:$Port" | Tee-Object -FilePath (Join-Path $artifacts 'shared-world-teaching-node.log')
    if ($LASTEXITCODE -ne 0) {throw 'Shared world Node smoke failed'}
    if ($NativeClient) {
        if (-not (Test-Path -LiteralPath $DotNet)) {throw 'Pass -DotNet with a .NET 8 SDK executable'}
        $env:DOTNET_CLI_HOME=Join-Path $work 'dotnet-home'
        $nativeOut=Join-Path $work 'native-client'
        # All harness sources use the BCL. Invoke Roslyn against the installed
        # reference pack directly, without NuGet or machine-wide restore config.
        $sdkRoot=Split-Path -Parent $DotNet
        $sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object Name -Descending | Select-Object -First 1
        $pack=Get-ChildItem (Join-Path $sdkRoot 'packs/Microsoft.NETCore.App.Ref') -Directory -Filter '8.*' | Sort-Object Name -Descending | Select-Object -First 1
        if (-not $sdk -or -not $pack) {throw 'The native harness requires an installed .NET 8 SDK and reference pack'}
        New-Item -ItemType Directory -Force $nativeOut | Out-Null
        $refs=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:'+$_.FullName})
        $sources=@((Join-Path $exampleRoot 'tests/native-client/Program.cs'))
        foreach ($folder in @((Join-Path $exampleRoot 'client/Assets/Scripts'),(Join-Path $frameworkRoot 'packages/unity/core/Runtime'),(Join-Path $frameworkRoot 'packages/unity/transport/Runtime'))) {
            $sources+=@(Get-ChildItem -LiteralPath $folder -Filter '*.cs' | ForEach-Object FullName)
        }
        & $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') /nologo /nostdlib /target:exe /langversion:latest @refs "-out:$nativeOut/NativeClient.dll" @sources
        if ($LASTEXITCODE -ne 0) {throw 'Native client harness compilation failed'}
        '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}' | Set-Content -Encoding utf8 (Join-Path $nativeOut 'NativeClient.runtimeconfig.json')
        & $DotNet (Join-Path $nativeOut 'NativeClient.dll') "http://127.0.0.1:$Port" | Tee-Object -FilePath (Join-Path $artifacts 'shared-world-teaching-native.log')
        if ($LASTEXITCODE -ne 0) {throw 'Native C# session smoke failed'}
    }
} finally {
    if ($server -and -not $server.HasExited) {Stop-Process -Id $server.Id}
    $env:DOTNET_CLI_HOME=$savedDotnetHome
    Pop-Location
}
