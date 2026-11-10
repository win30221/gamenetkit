<#
.SYNOPSIS
編譯原生桌面 WebTransport helper。
.DESCRIPTION
分類：SDK 建置。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go 工具鏈。
行為與產物：寫入 native/webtransport/Builds；可指定目標 OS／架構。
.EXAMPLE
./tools/Build-NativeTransport.ps1 -OS windows -Arch amd64
#>
param([ValidateSet('windows','linux','darwin')][string]$OS='windows',[ValidateSet('amd64','arm64')][string]$Arch='amd64')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/internal/Go-Env.ps1"
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root "native/webtransport/Builds/$OS-$Arch"
New-Item -ItemType Directory -Force $out | Out-Null
$name=if($OS -eq 'windows'){'realtime-webtransport.exe'}else{'realtime-webtransport'}
$savedOS=$env:GOOS;$savedArch=$env:GOARCH;$savedCGO=$env:CGO_ENABLED
Push-Location (Join-Path $root 'native/webtransport')
try{
    $env:GOOS=$OS;$env:GOARCH=$Arch;$env:CGO_ENABLED='0'
    & $Go build -buildvcs=false -trimpath -o (Join-Path $out $name) .
    if($LASTEXITCODE -ne 0){throw 'Native WebTransport helper build failed'}
    Write-Output (Join-Path $out $name)
}finally{Pop-Location;$env:GOOS=$savedOS;$env:GOARCH=$savedArch;$env:CGO_ENABLED=$savedCGO}
