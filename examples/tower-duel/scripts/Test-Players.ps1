<# End-to-end test: a real server and two development Windows Players. #>
param([int]$TimeoutSeconds=800, [string]$Output='', [ValidateSet(0,1)][int]$AttackerSeat=0, [ValidateRange(1,3)][int]$Rounds=1, [switch]$ExerciseRecovery, [switch]$WeakNetwork, [switch]$Capture, [switch]$TerrainAudit)
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
if(-not $Output){$Output=Join-Path $repo 'artifacts/tower-duel/player-test'}
$Output=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$player=Join-Path $example 'client/Builds/Windows/TowerDuel.exe'
if(-not(Test-Path -LiteralPath $player)){throw 'Build the development Player first'}
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
Push-Location (Join-Path $example 'server')
try {& $Go build -o ../Builds/tower-duel-server.exe .; if($LASTEXITCODE -ne 0){throw 'Server build failed'}; if($WeakNetwork){& $Go build -o ../Builds/weaknetproxy.exe ./cmd/weaknetproxy;if($LASTEXITCODE -ne 0){throw 'Proxy build failed'}}} finally {Pop-Location}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$serverURL="http://127.0.0.1:$port"
$processes=[Collections.Generic.List[Diagnostics.Process]]::new()
function Quote-Argument([string]$Value){'"'+$Value.Replace('"','\"')+'"'}
function Read-Report([string]$Path){
    if(Test-Path -LiteralPath $Path){try{return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json}catch{return $null}}
    return $null
}
function Launch-Player([string]$Mode,[string]$Role,[string]$Room,[string]$Report){
    $arguments=@('-screen-fullscreen','0','-screen-width','720','-screen-height','1000','-td-server',$serverURL,'-td-auto',$Mode,'-td-role',$Role,'-td-rounds',$Rounds,'-td-report',(Quote-Argument $Report),'-logFile',(Quote-Argument (Join-Path $Output "$Role.log")))
    if($ExerciseRecovery -and $Role -eq 'attacker'){$arguments+=@('-td-recover','1')}
    if($Capture){$arguments+=@('-td-capture','1')}
    if($TerrainAudit){$arguments+=@('-td-terrain-audit','1')}
    if($Room){$arguments+=@('-td-room',$Room)}
    $p=Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $processes.Add($p)
}
try {
    # Unique report names avoid treating results from a previous run as fresh evidence.
    $run=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $aPath=Join-Path $Output "$run-creator.json";$bPath=Join-Path $Output "$run-joiner.json"
    $creatorRole=if($AttackerSeat -eq 0){'attacker'}else{'defender'}
    $joinerRole=if($AttackerSeat -eq 0){'defender'}else{'attacker'}
    $server=Start-Process -FilePath (Join-Path $example 'Builds/tower-duel-server.exe') -ArgumentList @('-http',"127.0.0.1:$port",'-map',(Quote-Argument (Join-Path $example 'client/Assets/Resources/TowerDuel/map.json')),'-rules',(Quote-Argument (Join-Path $example 'client/Assets/Resources/TowerDuel/rules.json')),'-transport-runtime',(Quote-Argument (Join-Path $repo 'packages/unity/transport/Browser/transport.js'))) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Output 'server.log') -RedirectStandardError (Join-Path $Output 'server-error.log')
    $processes.Add($server)
    $ready=$false
    for($i=0;$i -lt 30;$i++){try{if((Invoke-WebRequest "$serverURL/healthz" -TimeoutSec 1).StatusCode -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
    if(-not $ready){throw 'Server health check failed'}
    if($WeakNetwork){
        $proxyListener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$proxyListener.Start();$proxyPort=$proxyListener.LocalEndpoint.Port;$proxyListener.Stop()
        $proxy=Start-Process -FilePath (Join-Path $example 'Builds/weaknetproxy.exe') -ArgumentList @('-http',"127.0.0.1:$proxyPort",'-upstream',$serverURL,'-delay','120','-jitter','80') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Output 'proxy.log') -RedirectStandardError (Join-Path $Output 'proxy-error.log')
        $processes.Add($proxy);$serverURL="http://127.0.0.1:$proxyPort"
        $proxyReady=$false;for($i=0;$i -lt 30;$i++){try{if((Invoke-WebRequest "$serverURL/healthz" -TimeoutSec 1).StatusCode -eq 200){$proxyReady=$true;break}}catch{};Start-Sleep -Milliseconds 200}
        if(-not $proxyReady){throw 'Proxy health check failed'}
    }
    Launch-Player 'create' $creatorRole '' $aPath
    $deadline=[DateTime]::UtcNow.AddSeconds(50);$room=''
    while([DateTime]::UtcNow -lt $deadline){$a=Read-Report $aPath;if($a -and $a.room){$room=$a.room;break};Start-Sleep -Milliseconds 500}
    if(-not $room){throw 'First Player did not create a room; inspect the Player logs'}
    Launch-Player 'join' $joinerRole $room $bPath
    Write-Output "Two real Players: room $room, $serverURL"
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds);$nextProgress=[DateTime]::UtcNow
    while([DateTime]::UtcNow -lt $deadline){
        $a=Read-Report $aPath;$b=Read-Report $bPath
        if($a -and $b -and $a.ended -and $b.ended){break}
        if([DateTime]::UtcNow -ge $nextProgress){Write-Output "phase=$($a.phase) tick=$($a.tick) accepted=$($a.accepted) units=$($a.unitCount) crossed=$($a.crossed) towerDamage=$($a.damagedTower)";$nextProgress=[DateTime]::UtcNow.AddSeconds(15)}
        foreach($p in $processes){if($p.HasExited){throw "Test process exited: $($p.Id)"}}
        Start-Sleep -Seconds 1
    }
    if(-not($a -and $b -and $a.ended -and $b.ended)){throw 'Players did not finish the match before timeout'}
    if($a.match -ne $b.match -or $a.seat -ne 0 -or $b.seat -ne 1 -or $a.winner -ne $AttackerSeat -or $b.winner -ne $AttackerSeat -or $a.reason -ne $b.reason -or $a.completedRounds -ne $Rounds -or $b.completedRounds -ne $Rounds){throw 'Players disagree about the match or result'}
    $attacker=@($a,$b)[$AttackerSeat];$defender=@($a,$b)[1-$AttackerSeat]
    foreach($report in @($a,$b)){if($report.mapID -ne 'wall-gates-mud-1' -or -not $report.environment2D -or -not $report.ownBuildingsAway -or $report.mudSyncErrors -ne 0){throw 'New environment, facing or authoritative terrain synchronization failed'}}
    if($TerrainAudit){foreach($report in @($a,$b)){if(-not $report.mudObserved -or $report.mudMoveSamples -lt 3){throw 'Missing moving mud evidence in the real Player'}}}
    if($attacker.resultTitle -ne '勝利！' -or $defender.resultTitle -ne '再接再厲'){throw 'UI result labels do not match authoritative winner'}
    if($attacker.accepted -lt 8 -or $defender.accepted -ne 8 -or -not($a.crossed -and $b.crossed -and $a.damagedTower -and $b.damagedTower -and $a.damagedUnit -and $b.damagedUnit) -or $a.unitsRemoved -lt 1 -or $b.unitsRemoved -lt 1 -or $a.frames -lt 120 -or $b.frames -lt 120 -or $a.uiHeight -lt 500 -or $b.uiHeight -lt 500){throw 'Missing UI, deployment, combat, death or live-frame evidence'}
    foreach($report in @($a,$b)){if($report.cardsUsed -ne 255 -or $report.models -ne 63 -or $report.walkKinds -ne 63 -or $report.attackKinds -ne 63 -or $report.spellsSeen -ne 3 -or $report.walkPose -lt 10 -or $report.attackPose -lt 10 -or $report.deathPose -lt 3 -or $report.arrows -lt 1 -or -not $report.tacticalContentVisible -or $report.previewCards -ne 255 -or -not $report.previewsCleared){throw 'Missing eight-card UI/preview, six-unit animation, spell, death, projectile or combat-view framing evidence'}}
    foreach($report in @($a,$b)){if(-not $report.numericHealth -or -not $report.fractionalEnergy){throw 'Missing numeric health or fractional energy evidence'}}
    $expectedSprites=0;$catalog=Get-Content -LiteralPath (Join-Path $example 'client/Assets/Resources/TowerDuel/rules.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    for($i=0;$i -lt $catalog.cards.Count;$i++){if(Test-Path -LiteralPath (Join-Path $example ('client/Assets/Resources/TowerDuel/B3/Prefabs/'+$catalog.cards[$i].id+'.prefab'))){$expectedSprites=$expectedSprites -bor (1 -shl $i)}}
    foreach($report in @($a,$b)){if($report.sprites -ne $expectedSprites -or $report.b3Cards -ne $expectedSprites -or $report.b3Frames -ne $expectedSprites -or $report.spriteShadows -ne $expectedSprites -or -not $report.shadowsLowerLeft -or $report.visibleTeamDiscs -ne 0){throw 'B3 sprites, matching cards, frames, lower-left shadows or hidden team discs failed in the real Player'}}
    if([Math]::Abs($a.shadowX-$b.shadowX) -gt .02 -or [Math]::Abs($a.shadowY-$b.shadowY) -gt .02){throw 'The two players saw different screen-space shadow directions'}
    if(-not($a.fullEnergyReminder -or $b.fullEnergyReminder)){throw 'No static full-energy reminder was observed'}
    if($ExerciseRecovery -and $attacker.resumes -lt 1){throw 'No successful recovery was observed'}
    foreach($tower in $a.towers){$other=$b.towers | Where-Object id -eq $tower.id;if($other.hp -ne $tower.hp){throw "Tower $($tower.id) differs"}}
    $summary=[ordered]@{passed=$true;room=$room;match=$a.match;winner=$a.winner;reason=$a.reason;rounds=@($a.completedRounds,$b.completedRounds);resumes=@($a.resumes,$b.resumes);accepted=@($a.accepted,$b.accepted);unitsRemoved=@($a.unitsRemoved,$b.unitsRemoved);frames=@($a.frames,$b.frames);cardsUsed=@($a.cardsUsed,$b.cardsUsed);models=@($a.models,$b.models);walkKinds=@($a.walkKinds,$b.walkKinds);attackKinds=@($a.attackKinds,$b.attackKinds);spells=@($a.spellsSeen,$b.spellsSeen);frameP95MS=@($a.frameP95MS,$b.frameP95MS);snapshotBytes=@($a.snapshotBytes,$b.snapshotBytes);reports=@($aPath,$bPath)}
    $summary.wholeFieldVisible=@($a.wholeFieldVisible,$b.wholeFieldVisible)
    $summary.tacticalContentVisible=@($a.tacticalContentVisible,$b.tacticalContentVisible)
    $summary.numericHealth=@($a.numericHealth,$b.numericHealth);$summary.fractionalEnergy=@($a.fractionalEnergy,$b.fractionalEnergy);$summary.fullEnergyReminder=@($a.fullEnergyReminder,$b.fullEnergyReminder)
    $summary.previewCards=@($a.previewCards,$b.previewCards);$summary.previewsCleared=@($a.previewsCleared,$b.previewsCleared)
    $summary.networkProfile=if($WeakNetwork){'Message-level: one-way 120 +/-80 ms; drop every third deploy and fifth ack; duplicate every fourth deploy'}else{'Direct local connection'}
    $summary.mapID=@($a.mapID,$b.mapID);$summary.environment2D=@($a.environment2D,$b.environment2D)
    $summary.ownBuildingsAway=@($a.ownBuildingsAway,$b.ownBuildingsAway);$summary.mudObserved=@($a.mudObserved,$b.mudObserved)
    $summary.mudSyncErrors=@($a.mudSyncErrors,$b.mudSyncErrors);$summary.mudMoveSamples=@($a.mudMoveSamples,$b.mudMoveSamples)
    $summary.mudMoveRatio=@(($a.mudMoveRatioTotal/[Math]::Max(1,$a.mudMoveSamples)),($b.mudMoveRatioTotal/[Math]::Max(1,$b.mudMoveSamples)))
    $summary.b3Sprites=@($a.sprites,$b.sprites);$summary.b3Cards=@($a.b3Cards,$b.b3Cards);$summary.b3Frames=@($a.b3Frames,$b.b3Frames)
    $summary.spriteShadows=@($a.spriteShadows,$b.spriteShadows);$summary.shadowsLowerLeft=@($a.shadowsLowerLeft,$b.shadowsLowerLeft)
    $summary.visibleTeamDiscs=@($a.visibleTeamDiscs,$b.visibleTeamDiscs)
    $summary.shadowDirections=@(@($a.shadowX,$a.shadowY),@($b.shadowX,$b.shadowY))
    if($WeakNetwork){$summary.proxyStats=Invoke-RestMethod "$serverURL/__qa/stats";if($summary.proxyStats.dropped -lt 1 -or $summary.proxyStats.duplicated -lt 1){throw 'Proxy did not exercise message loss and duplication'}}
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
    # Allow the end-of-frame screenshot request to complete before closing our Players.
    Start-Sleep -Seconds 2
    $summary | ConvertTo-Json -Depth 5
} finally {
    foreach($p in $processes){if(-not $p.HasExited){Stop-Process -Id $p.Id -ErrorAction SilentlyContinue};$p.Dispose()}
}
