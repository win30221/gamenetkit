<#
.SYNOPSIS
依序執行八個 C# 與四個 Go 組件主題。
.DESCRIPTION
分類：終端教學驗證。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 與 .NET 8 SDK。
行為與產物：在終端輸出各主題結果，沒有 Unity 畫面。
.EXAMPLE
./examples/modules/Test.ps1
#>
param([string]$DotNet='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Toolchain.ps1"
$DotNet=Resolve-GameNetKitDotNet -Path $DotNet
$root=$script:GameNetKitRoot
& "$root/examples/modules/client/Run.ps1" -DotNet $DotNet
& "$root/examples/modules/server/Run.ps1"
'RF_LESSONS PASS: 8 client topics and 4 server topics'
