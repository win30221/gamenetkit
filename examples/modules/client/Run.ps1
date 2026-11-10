<#
.SYNOPSIS
執行選定的 C# SDK 組件教學。
.DESCRIPTION
分類：終端教學。所有命令從 gamenetkit 根目錄執行。
前提：Go 教學需 Go；C# 教學需 .NET SDK。
行為與產物：輸出終端教學結果，不啟動遊戲畫面。
.EXAMPLE
./examples/modules/client/Run.ps1
#>
param(
    [ValidateSet('all','interpolation','prediction-correction','clock','replication','actions','codec','diagnostics-simulation','playback')][string]$Lesson='all',
    [string]$DotNet=''
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
if (-not (Test-Path -LiteralPath $DotNet -PathType Leaf)) {throw 'Pass -DotNet with an installed .NET 8 SDK dotnet.exe'}
$framework=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sdkRoot=Split-Path -Parent $DotNet
$sdk=Get-ChildItem (Join-Path $sdkRoot 'sdk') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name.Split('-')[0]} -Descending | Select-Object -First 1
$pack=Get-ChildItem (Join-Path $sdkRoot 'packs/Microsoft.NETCore.App.Ref') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
if (-not $sdk -or -not $pack) {throw 'An installed .NET 8 SDK and reference pack are required'}
$lessons=[ordered]@{
    'interpolation'=@('InterpolationLesson','interpolation')
    'prediction-correction'=@('PredictionCorrectionLesson','prediction','correction')
    'clock'=@('ClockLesson','clock')
    'replication'=@('ReplicationLesson','replication')
    'actions'=@('ActionsLesson','actions')
    'codec'=@('CodecLesson','codec')
    'diagnostics-simulation'=@('DiagnosticsSimulationLesson','diagnostics','simulation')
    'playback'=@('PlaybackLesson','playback')
}
$out=Join-Path $framework '.local/module-lessons/client'
New-Item -ItemType Directory -Force $out | Out-Null
$savedDotnetHome=$env:DOTNET_CLI_HOME
try {
    $env:DOTNET_CLI_HOME=Join-Path $out 'dotnet-home'
    $references=@(Get-ChildItem (Join-Path $pack.FullName 'ref/net8.0') -Filter '*.dll' | ForEach-Object {'-r:'+$_.FullName})
    foreach ($name in $lessons.Keys) {
        if ($Lesson -ne 'all' -and $Lesson -ne $name) {continue}
        $entry=$lessons[$name][0]
        $sources=@((Join-Path $PSScriptRoot 'Check.cs'), (Join-Path $PSScriptRoot "$entry.cs"))
        $modules=@('core')+$lessons[$name][1..($lessons[$name].Count-1)]
        foreach ($module in $modules) {
            $sources+=@(Get-ChildItem (Join-Path $framework "packages/unity/$module/Runtime") -Filter '*.cs' | ForEach-Object FullName)
        }
        & $DotNet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') /nologo /nostdlib /target:exe /langversion:latest @references "-out:$out/$name.dll" @sources
        if ($LASTEXITCODE -ne 0) {throw "Compile failed: $name"}
        '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestPatch"}}' | Set-Content -LiteralPath (Join-Path $out "$name.runtimeconfig.json") -Encoding utf8
        & $DotNet (Join-Path $out "$name.dll") (Join-Path $framework 'tests/fixtures/codec.json')
        if ($LASTEXITCODE -ne 0) {throw "Lesson failed: $name"}
        "RF_CLIENT_LESSON PASS $name"
    }
} finally { $env:DOTNET_CLI_HOME=$savedDotnetHome }
