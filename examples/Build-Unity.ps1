<#
.SYNOPSIS
建置指定 Unity 範例的 Windows 或 WebGL Player。
.DESCRIPTION
分類：範例建置。所有命令從 gamenetkit 根目錄執行。
前提：有效 Unity 授權、釘選 Editor 與目標平台模組。
行為與產物：寫入所選專案的 Builds 與根目錄 artifacts。
.EXAMPLE
./examples/Build-Unity.ps1 -Project Minimal -Target Windows
#>
param([ValidateSet('Rumblebound','Minimal','SharedWorld','Interpolation','RumbleboundExample','Prediction','NetworkRecovery','ObjectsActions','Replay','Interest','Codec','History')][string]$Project='RumbleboundExample',[ValidateSet('Windows','Web')][string]$Target='Windows',[ValidateRange(30,3600)][int]$TimeoutSeconds=600,[string]$UnityEditor='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../tools/internal/Toolchain.ps1"
$root=$script:GameNetKitRoot
if ($Project -eq 'Rumblebound') { $Project = 'RumbleboundExample' }
if ($Project -eq 'RumbleboundExample') {
    $projectPath=Join-Path $root 'examples/rumblebound/client'
    $buildEntry='Rumblebound.Example.Editor.ExampleBuild'
    $artifactName='example'
    $successMarker='RF_EXAMPLE_BUILD PASS'
} elseif ($Project -in @('Prediction','NetworkRecovery','ObjectsActions','Replay','Interest','Codec','History')) {
    $lessons=@{Prediction=@('prediction','PredictionLesson');NetworkRecovery=@('network-recovery','NetworkRecovery');ObjectsActions=@('objects-actions','ObjectsActions');Replay=@('replay','ReplayLesson');Interest=@('interest','InterestLesson');Codec=@('codec','CodecLesson');History=@('history-lagcomp','HistoryLesson')}
    $entry=$lessons[$Project]
    $projectPath=Join-Path $root ('examples/'+$entry[0]+'/client')
    $buildEntry='Realtime.Examples.'+$entry[1]+'.Editor.ExampleBuild'
    $artifactName=$entry[0]
    $successMarker='RF_'+$Project.ToUpperInvariant()+'_BUILD PASS'
} elseif ($Project -eq 'SharedWorld') {
    $projectPath=Join-Path $root 'examples/shared-world/client'
    $buildEntry='Realtime.Examples.SharedWorld.Editor.ExampleBuild'
    $artifactName='shared-world'
    $successMarker='RF_SHARED_WORLD_BUILD PASS'
} elseif ($Project -eq 'Interpolation') {
    $projectPath=Join-Path $root 'examples/interpolation/client'
    $buildEntry='Realtime.Examples.InterpolationLesson.Editor.ExampleBuild'
    $artifactName='interpolation'
    $successMarker='RF_INTERPOLATION_BUILD PASS'
} else {
    $projectPath=Join-Path $root 'examples/minimal-unity'
    $buildEntry='MinimalBuild'
    $artifactName='minimal'
    $successMarker='RF_MINIMAL_BUILD PASS'
}
New-Item -ItemType Directory -Force (Join-Path $root 'artifacts') | Out-Null
$targetName=if ($Target -eq 'Web') {'WebGL'} else {'Win64'}
$log=Join-Path $root "artifacts/$artifactName-$Target.log"
$unity=Resolve-GameNetKitUnityEditor -Path $UnityEditor -ProjectPath $projectPath
$process=Start-Process -FilePath $unity -ArgumentList @('-batchmode','-nographics','-quit','-buildTarget',$targetName,'-projectPath',"`"$projectPath`"",'-executeMethod',"$buildEntry.$Target",'-logFile',"`"$log`"") -WindowStyle Hidden -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -ge $deadline) { throw "$Project build timed out: $log" }
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $log) -or -not (Select-String -LiteralPath $log -SimpleMatch $successMarker -Quiet)) {
        throw "$Project build failed: $log"
    }
    Write-Output "$Project $Target build completed: $log"
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
