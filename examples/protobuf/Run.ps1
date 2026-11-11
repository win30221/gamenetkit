param([ValidateRange(1024,65535)][int]$Port=18101)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot
$webRoot=Join-Path $PSScriptRoot 'client/Builds/WebGL'
Write-Host "Protobuf example: http://127.0.0.1:$Port/unity/ (build Web first); Ctrl+C stops the server."
& $Go -C (Join-Path $PSScriptRoot 'server') run . -listen "127.0.0.1:$Port" -web-root $webRoot -transport-script (Join-Path $root 'packages/unity/transport/Browser/transport.js')
if ($LASTEXITCODE -ne 0) { throw 'Protobuf example server exited with an error' }
