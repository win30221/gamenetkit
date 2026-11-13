$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $example '../../tools/internal/Go-Env.ps1')
Push-Location (Join-Path $example 'server')
try {
    & $Go test ./...
    if($LASTEXITCODE -ne 0){throw 'Server tests failed'}
    & $Go vet ./...
    if($LASTEXITCODE -ne 0){throw 'go vet failed'}
} finally {Pop-Location}
