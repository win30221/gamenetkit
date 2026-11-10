<#
.SYNOPSIS
驗證遊戲 journal 並匯出離線播放 JSON。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 與已完成的 journal。
行為與產物：建立新的輸出檔；既有目的檔會被拒絕。
.EXAMPLE
./examples/rumblebound/scripts/Export-Playback.ps1 -File artifacts/my-match.replay.jsonl -Output artifacts/my-match.playback.json
#>
param([Parameter(Mandatory)][string]$File,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot
$recording=(Resolve-Path -LiteralPath $File).Path
$destination=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path -Parent $destination)|Out-Null
Push-Location (Join-Path $root 'examples/rumblebound/game')
try{
 & $Go run -buildvcs=false ./cmd/replay -file $recording -export $destination
 if($LASTEXITCODE -ne 0){throw 'Playback export failed; inspect the verification result'}
 Write-Output "Unity playback file: $destination"
}finally{Pop-Location}
