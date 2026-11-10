<#
.SYNOPSIS
在真正 Unity Editor 執行獨立 SDK 演算法測試。
.DESCRIPTION
分類：框架驗證。所有命令從 gamenetkit 根目錄執行。
前提：有效 Unity 授權與釘選 Editor。
行為與產物：批次啟動 tests/sdk-unity，日誌為 artifacts/sdk-tests.log。
.EXAMPLE
./tests/scripts/Test-UnitySdk.ps1
#>
param([string]$UnityEditor='', [ValidateRange(30,3600)][int]$TimeoutSeconds=600)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Toolchain.ps1"
$root=$script:GameNetKitRoot
$project=Join-Path $root 'tests/sdk-unity'
$log=Join-Path $root 'artifacts/sdk-tests.log'
$unity=Resolve-GameNetKitUnityEditor -Path $UnityEditor -ProjectPath $project
New-Item -ItemType Directory -Force (Join-Path $root 'artifacts') | Out-Null
$process=Start-Process -FilePath $unity -ArgumentList @('-batchmode','-nographics','-quit','-projectPath',"`"$project`"",'-executeMethod','SdkTests.Run','-logFile',"`"$log`"") -WindowStyle Hidden -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while(-not $process.WaitForExit(1000)){if([DateTime]::UtcNow -ge $deadline){throw "SDK tests timed out: $log"}}
} finally { if(-not $process.HasExited){Stop-Process -Id $process.Id} }
if($process.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch 'RF_SDK_TESTS PASS' -Quiet)){throw "Independent SDK tests failed: $log"}
Select-String -LiteralPath $log -Pattern 'RF_(SDK_TESTS|NETWORK_TOOLS_SDK|SEVEN_SDK|PLAYBACK_SDK) PASS' | ForEach-Object {$_.Line}
