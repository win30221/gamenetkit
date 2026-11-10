<#
.SYNOPSIS
執行 14 個 Go 模組 test／vet，以及傳輸、共用網頁與 Shared World 的 Node 測試。
.DESCRIPTION
分類：框架驗證。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 與 Node.js 22 以上；首次還原需要網路。
行為與產物：執行 SDK 與各範例 Go 測試，失敗時中止；不啟動 Unity Editor。
.EXAMPLE
./tests/scripts/Test-GoAndBrowser.ps1
#>
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot
node --test "$root/packages/unity/transport/Browser/Tests/*.test.mjs"
if ($LASTEXITCODE -ne 0) {throw 'Independent transport SDK tests failed'}
node --test "$root/examples/browser-common/*.test.mjs"
if ($LASTEXITCODE -ne 0) {throw 'Shared browser view tests failed'}
node --test "$root/examples/shared-world/scripts/*.test.mjs"
if ($LASTEXITCODE -ne 0) {throw 'Shared-world teaching state tests failed'}
foreach ($folder in @('server','examples/rumblebound/game','native/webtransport','examples/minimal-server','examples/shared-world/server','examples/rumblebound/server','examples/modules/server','examples/prediction/server','examples/network-recovery/server','examples/objects-actions/server','examples/replay/server','examples/interest/server','examples/codec/server','examples/history-lagcomp/server')) {
    Push-Location (Join-Path $root $folder)
    try {
        # Keep Go discovery out of Unity's transient Temp/Library directories.
        $packages = @('./...')
        & $Go test -count=1 @packages
        if ($LASTEXITCODE -ne 0) {throw "Go tests failed in $folder"}
        & $Go vet @packages
        if ($LASTEXITCODE -ne 0) {throw "Go vet failed in $folder"}
    } finally {Pop-Location}
}
