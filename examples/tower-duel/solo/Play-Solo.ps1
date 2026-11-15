<# Portable test launcher. Owns a normal backend, a separate bot companion and one Player. #>
param([ValidateSet('practice','sandbox')][string]$Mode='practice',[string]$VerifyOutput='')
$ErrorActionPreference='Stop'
$bundle=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$player=Join-Path $bundle 'TowerDuel.exe'
$backend=Join-Path $PSScriptRoot 'tower-duel-server.exe'
$companion=Join-Path $PSScriptRoot 'tower-duel-solo.exe'
foreach($file in @($player,$backend,$companion,(Join-Path $PSScriptRoot 'map.json'),(Join-Path $PSScriptRoot 'rules.json'))){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing solo build file: $file. Run solo/Build.ps1 again."}
}
$sessionRoot=Join-Path ([IO.Path]::GetTempPath()) ('TowerDuel-Solo/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
$readyFile=Join-Path $sessionRoot 'companion.txt'
$server=$null;$helper=$null;$game=$null
function Quote-NativeArgument([string]$Value){'"'+$Value.Replace('"','\"')+'"'}
try {
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
    $url="http://127.0.0.1:$port"
    $arguments=@('-http',"127.0.0.1:$port",'-map',(Quote-NativeArgument (Join-Path $PSScriptRoot 'map.json')),
        '-rules',(Quote-NativeArgument (Join-Path $PSScriptRoot 'rules.json')),
        '-transport-runtime',(Quote-NativeArgument (Join-Path $PSScriptRoot 'transport.js')))
    $server=Start-Process -FilePath $backend -ArgumentList $arguments -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $sessionRoot 'server.log') -RedirectStandardError (Join-Path $sessionRoot 'server-error.log')
    $deadline=[DateTime]::UtcNow.AddSeconds(15);$healthy=$false
    while([DateTime]::UtcNow -lt $deadline){
        if($server.HasExited){throw "Local backend exited. See $sessionRoot/server-error.log"}
        try {if((Invoke-WebRequest -Uri ($url+'/healthz') -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200){$healthy=$true;break}}catch{}
        Start-Sleep -Milliseconds 100
    }
    if(-not $healthy -or $server.HasExited){throw "Local backend did not become ready. See $sessionRoot/server-error.log"}
    $helper=Start-Process -FilePath $companion -WindowStyle Hidden -PassThru -ArgumentList @('-server',$url,'-ready-file',(Quote-NativeArgument $readyFile)) `
        -RedirectStandardOutput (Join-Path $sessionRoot 'companion.log') -RedirectStandardError (Join-Path $sessionRoot 'companion-error.log')
    $deadline=[DateTime]::UtcNow.AddSeconds(15);$api=''
    while([DateTime]::UtcNow -lt $deadline){
        if($helper.HasExited){throw "Solo companion exited. See $sessionRoot/companion-error.log"}
        if(Test-Path -LiteralPath $readyFile){
            $api=(Get-Content -LiteralPath $readyFile -Raw).Trim()
            if($api -match '^http://127\.0\.0\.1:\d+$'){
                try {if((Invoke-WebRequest -Uri ($api+'/solo/config') -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200){break}}catch{}
            }
            $api=''
        }
        Start-Sleep -Milliseconds 100
    }
    if(-not $api){throw "Solo companion did not become ready. See $sessionRoot/companion-error.log"}
    Write-Output "Tower Duel $Mode - original backend: $url; optional companion: $api"
    Write-Output "Close the game to stop both local processes. Logs: $sessionRoot"
    $arguments=@('-td-server',$url,'-td-solo-api',$api,'-td-solo-mode',$Mode,'-screen-fullscreen','0',
        '-logFile',(Quote-NativeArgument (Join-Path $sessionRoot 'player.log')))
    if($VerifyOutput){$arguments+=@('-td-solo-report',(Quote-NativeArgument ([IO.Path]::GetFullPath($VerifyOutput))))}
    # The caller requested an interactive game; automated checks keep it hidden.
    $window=if($VerifyOutput){'Hidden'}else{'Normal'}
    $game=Start-Process -FilePath $player -WorkingDirectory $bundle -PassThru -WindowStyle $window -ArgumentList $arguments
    $verifyDeadline=[DateTime]::UtcNow.AddSeconds(120)
    while(-not $game.WaitForExit(500)){
        if($server.HasExited -or $helper.HasExited){throw "A local test process stopped. See $sessionRoot"}
        if($VerifyOutput -and [DateTime]::UtcNow -gt $verifyDeadline){throw "Native verification timed out. See $sessionRoot/player.log"}
    }
    if($game.ExitCode -ne 0){throw "Game exited with code $($game.ExitCode). See $sessionRoot/player.log"}
} finally {
    foreach($process in @($game,$helper,$server)){
        if($null -ne $process){if(-not $process.HasExited){Stop-Process -Id $process.Id -ErrorAction SilentlyContinue};$process.Dispose()}
    }
}
