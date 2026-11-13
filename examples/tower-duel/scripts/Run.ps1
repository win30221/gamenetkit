<# Start the authoritative server. Add -Clients 2 to open a local human-playable pair. #>
param([string]$Listen='127.0.0.1:18110', [ValidateRange(0,2)][int]$Clients=0, [switch]$Web)
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
$build=Join-Path $example 'Builds'
New-Item -ItemType Directory -Path $build -Force | Out-Null
Push-Location (Join-Path $example 'server')
try {
    & $Go build -o (Join-Path $build 'tower-duel-server.exe') .
    if($LASTEXITCODE -ne 0){throw 'Server build failed'}
    if($Clients -gt 0){
        $player=Join-Path $example 'client/Builds/Windows/TowerDuel.exe'
        if(-not(Test-Path -LiteralPath $player)){throw 'Build the Unity Player first: scripts/Build.ps1'}
        $server='http://'+($Listen -replace '^0\.0\.0\.0:','127.0.0.1:')
        for($i=0;$i -lt $Clients;$i++){
            # The caller explicitly requested interactive game windows.
            Start-Process -FilePath $player -ArgumentList @('-td-server',$server,'-screen-fullscreen','0') | Out-Null
        }
    }
    $arguments=@('-http',$Listen)
    if($Web){$webRoot=Join-Path $example 'client/Builds/WebGL';if(-not(Test-Path -LiteralPath (Join-Path $webRoot 'index.html'))){throw 'Build WebGL first: scripts/Build.ps1 -Target WebGL'};$arguments+=@('-web',$webRoot)}
    & (Join-Path $build 'tower-duel-server.exe') @arguments
    if($LASTEXITCODE -ne 0){throw 'Server exited with an error'}
} finally {Pop-Location}
