<#
.SYNOPSIS
選用釘選 Go 並設定目前 PowerShell 的 Go 快取環境。
.DESCRIPTION
分類：內部支援。所有命令從 gamenetkit 根目錄執行。
前提：由 Go 啟動、建置與測試腳本匯入。
行為與產物：設定目前程序的環境變數與 $Go；不寫入系統環境。
.EXAMPLE
. ./tools/internal/Go-Env.ps1
#>
. "$PSScriptRoot/Toolchain.ps1"
$cacheRoot = if ($env:GAMENETKIT_CACHE) { [IO.Path]::GetFullPath($env:GAMENETKIT_CACHE) } else { Join-Path $script:GameNetKitRoot '.cache' }
# A dedicated cache override wins; otherwise honor standard Go variables. Never borrow a parent repository's cache.
if ($env:GAMENETKIT_CACHE -or -not $env:GOPATH) { $env:GOPATH = Join-Path $cacheRoot 'gopath' }
if ($env:GAMENETKIT_CACHE -or -not $env:GOMODCACHE) { $env:GOMODCACHE = Join-Path $cacheRoot 'gomod' }
if ($env:GAMENETKIT_CACHE -or -not $env:GOCACHE) { $env:GOCACHE = Join-Path $cacheRoot 'gobuild' }
$script:Go = Resolve-GameNetKitGo
