<# Measure eighty animated actors in two real Windows Players against a test-only server fixture. #>
param([string]$Output='', [ValidateRange(15,90)][int]$Seconds=25)
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
if(-not $Output){$Output=Join-Path $repo 'artifacts/tower-duel/p0/load-80'}
$Output=[IO.Path]::GetFullPath($Output);New-Item -ItemType Directory -Path $Output -Force|Out-Null
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
Push-Location (Join-Path $example 'server')
try{& $Go test -c -o ../Builds/render-load-fixture.exe;if($LASTEXITCODE -ne 0){throw 'Fixture build failed'}}finally{Pop-Location}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$processes=[Collections.Generic.List[Diagnostics.Process]]::new()
$run=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss');$reports=@()
try{
    $fixture=Start-Process -FilePath (Join-Path $example 'Builds/render-load-fixture.exe') -WorkingDirectory (Join-Path $example 'server') -ArgumentList @('-test.run','^TestRenderingLoadFixture$','-test.v','-test.timeout','120s') -Environment @{TD_RENDER_FIXTURE='1';TD_RENDER_ADDR="127.0.0.1:$port"} -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Output 'fixture.log') -RedirectStandardError (Join-Path $Output 'fixture-error.log')
    $processes.Add($fixture);$ready=$false
    for($i=0;$i -lt 30;$i++){try{if((Invoke-WebRequest "http://127.0.0.1:$port/healthz" -TimeoutSec 1).StatusCode -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 200}
    if(-not $ready){throw 'Fixture did not become ready'}
    for($i=0;$i -lt 2;$i++){
        $report=Join-Path $Output "$run-seat$i.json";$reports+=,$report
        $arguments=@('-screen-fullscreen','0','-screen-width','720','-screen-height','1000','-td-server',"http://127.0.0.1:$port",'-td-auto','join','-td-room','LOAD80','-td-role','benchmark','-td-report',('"'+$report+'"'),'-logFile',('"'+(Join-Path $Output "seat$i.log")+'"'))
        $player=Start-Process -FilePath (Join-Path $example 'client/Builds/Windows/TowerDuel.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru;$processes.Add($player)
    }
    $deadline=[DateTime]::UtcNow.AddSeconds($Seconds)
    while([DateTime]::UtcNow -lt $deadline){foreach($process in $processes){if($process.HasExited){throw "Load process exited: $($process.Id)"}};Start-Sleep -Milliseconds 500}
    $states=@($reports|ForEach-Object{Get-Content -LiteralPath $_ -Raw|ConvertFrom-Json})
    foreach($state in $states){if($state.phase -ne 'playing' -or $state.maxUnits -ne 80 -or $state.models -ne 63 -or $state.frameP95MS -le 0 -or $state.snapshotBytes -gt 32768 -or -not $state.wholeFieldVisible){throw 'Missing full-capacity rendering or full-field framing evidence'}}
    $result=[ordered]@{passed=$true;fixture='Test-only eighty actors, damage disabled; real WebSocket/30Hz simulation/15Hz snapshots';seconds=$Seconds;units=@($states.maxUnits);models=@($states.models);frameP95MS=@($states.frameP95MS);snapshotBytes=@($states.snapshotBytes);reports=$reports}
    $result.wholeFieldVisible=@($states.wholeFieldVisible)
    $result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
    $result|ConvertTo-Json -Depth 5
}finally{foreach($process in $processes){if(-not $process.HasExited){Stop-Process -Id $process.Id -ErrorAction SilentlyContinue};$process.Dispose()}}
