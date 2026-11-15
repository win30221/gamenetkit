<# Checks the optional companion against the unchanged real backend; -Native also runs both Players. #>
param([switch]$Native)
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
$output=Join-Path $repo 'artifacts/tower-duel/solo'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$previousServer=$env:TOWER_DUEL_SOLO_SERVER
try {
    $env:TOWER_DUEL_SOLO_SERVER=Join-Path $output 'tower-duel-server.exe'
    Push-Location (Join-Path $example 'server')
    try {& $Go build -o $env:TOWER_DUEL_SOLO_SERVER .;if($LASTEXITCODE -ne 0){throw 'Original backend build failed'}} finally {Pop-Location}
    Push-Location (Join-Path $PSScriptRoot 'backend')
    try {
        $events=& $Go test -count=1 -json ./...
        $events | Set-Content -LiteralPath (Join-Path $output 'backend-tests.jsonl') -Encoding utf8
        if($LASTEXITCODE -ne 0){throw 'Optional companion tests failed'}
        $events | ForEach-Object {ConvertFrom-Json -InputObject $_} | Where-Object {$_.Action -eq 'pass' -and $_.Test -notlike '*/*'} | ForEach-Object {Write-Output "PASS $($_.Test) $($_.Elapsed)s"}
        & $Go vet ./...
        if($LASTEXITCODE -ne 0){throw 'Optional companion vet failed'}
    } finally {Pop-Location}
} finally {$env:TOWER_DUEL_SOLO_SERVER=$previousServer}
if($Native){
    $launcher=Join-Path $example 'Builds/Solo/Solo/Play-Solo.ps1'
    if(-not(Test-Path -LiteralPath $launcher)){throw 'Build solo/Build.ps1 before native verification'}
    $run=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    foreach($mode in @('practice','sandbox')){
        $report=Join-Path $output "$run-$mode.json"
        & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $launcher -Mode $mode -VerifyOutput $report
        if($LASTEXITCODE -ne 0){throw "Native $mode verification failed"}
        $result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if(-not $result.passed -or $result.mode -ne $mode -or $result.accepted -lt 3 -or $result.resumes -lt 1){throw "Native $mode report did not pass"}
        Write-Output "PASS native $mode : $report"
    }
}
