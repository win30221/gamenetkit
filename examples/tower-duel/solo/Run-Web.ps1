<# Run the standalone WebGL test site with an independent original game backend. #>
param([ValidatePattern('^127\.0\.0\.1:\d+$')][string]$Listen='127.0.0.1:18113')
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
$output=Join-Path $example 'Builds/SoloWeb'
$web=Join-Path $output 'WebGL'
if(-not(Test-Path -LiteralPath (Join-Path $web 'solo-build.txt'))){throw 'Build solo/Build.ps1 -Target WebGL first'}
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
foreach($build in @(@{Path=(Join-Path $example 'server');Name='tower-duel-api.exe'},@{Path=(Join-Path $PSScriptRoot 'backend');Name='tower-duel-solo.exe'})){
    Push-Location $build.Path
    try{& $Go build -o (Join-Path $output $build.Name) .;if($LASTEXITCODE -ne 0){throw "Build failed: $($build.Name)"}}finally{Pop-Location}
}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$server=$null
function Quote-WebArgument([string]$value){'"'+$value.Replace('"','\"')+'"'}
try {
    $server=Start-Process -FilePath (Join-Path $output 'tower-duel-api.exe') -WindowStyle Hidden -PassThru -ArgumentList @('-http',"127.0.0.1:$port",
        '-map',(Quote-WebArgument (Join-Path $example 'client/Assets/Resources/TowerDuel/map.json')),'-rules',(Quote-WebArgument (Join-Path $example 'client/Assets/Resources/TowerDuel/rules.json')),
        '-transport-runtime',(Quote-WebArgument (Join-Path $repo 'packages/unity/transport/Browser/transport.js')))
    $ready=$false
    for($i=0;$i -lt 30;$i++){
        if($server.HasExited){throw 'Solo game backend exited'}
        try{if((Invoke-WebRequest "http://127.0.0.1:$port/healthz" -TimeoutSec 1).StatusCode -eq 200){$ready=$true;break}}catch{}
        Start-Sleep -Milliseconds 100
    }
    if(-not $ready){throw 'Solo game backend did not become ready'}
    Write-Output "Standalone solo web: http://$Listen/"
    & (Join-Path $output 'tower-duel-solo.exe') -server "http://127.0.0.1:$port" -http $Listen -origin "http://$Listen" -web $web
    if($LASTEXITCODE -ne 0){throw 'Solo web service stopped with an error'}
} finally {if($server){if(-not $server.HasExited){Stop-Process -Id $server.Id -ErrorAction SilentlyContinue};$server.Dispose()}}
