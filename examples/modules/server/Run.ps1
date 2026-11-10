<#
.SYNOPSIS
執行選定的 Go SDK 組件教學。
.DESCRIPTION
分類：終端教學。所有命令從 gamenetkit 根目錄執行。
前提：Go 教學需 Go；C# 教學需 .NET SDK。
行為與產物：輸出終端教學結果，不啟動遊戲畫面。
.EXAMPLE
./examples/modules/server/Run.ps1
#>
param([ValidateSet('all','history-lagcomp','interest','codec','replay')][string]$Lesson='all')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
Push-Location $PSScriptRoot
try {
    & $Go run -buildvcs=false . -lesson $Lesson
    if ($LASTEXITCODE -ne 0) {throw "Server lesson failed: $Lesson"}
} finally {Pop-Location}
