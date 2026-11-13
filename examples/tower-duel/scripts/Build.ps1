<# Build a Windows development Player or a WebGL release Player. #>
param([string]$UnityCli='', [ValidateSet('Windows','WebGL')][string]$Target='Windows')
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
$project=Join-Path $example 'client'
. (Join-Path $repo 'tools/internal/Toolchain.ps1')
if(-not $UnityCli){$UnityCli=Join-Path $repo '.local/unity-cli/bin/unity.exe'}
if(-not(Test-Path -LiteralPath $UnityCli)){throw 'Install Unity CLI, or use Unity menu: Tower Duel > Build Windows development player'}
Copy-Item -LiteralPath (Join-Path $example 'protocol/map.json') -Destination (Join-Path $project 'Assets/Resources/TowerDuel/map.json')
Copy-Item -LiteralPath (Join-Path $example 'protocol/rules.json') -Destination (Join-Path $project 'Assets/Resources/TowerDuel/rules.json')
$editor=Resolve-GameNetKitUnityEditor -ProjectPath $project
$owned=$null
try {
    $status=& $UnityCli status --project-path $project --format json | ConvertFrom-Json
    if(-not $status.data.instances){
        $log=Join-Path $example 'Builds/editor-build.log';New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null
        $buildTarget=if($Target -eq 'WebGL'){'WebGL'}else{'Win64'}
        $owned=Start-Process -FilePath $editor -ArgumentList @('-batchmode','-projectPath',('"'+$project+'"'),'-buildTarget',$buildTarget,'-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
    }
    & $UnityCli status --until-ready --project-path $project --format json
    if($LASTEXITCODE -ne 0){throw 'Editor did not become ready; inspect the Unity log'}
    $playing=& $UnityCli command script eval --caller plugin --skill unity-cli --project-path $project --format json -- --code 'return UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;'
    if($LASTEXITCODE -ne 0 -or ($playing | ConvertFrom-Json)){throw 'Stop this project in Play Mode before building'}
    & $UnityCli recompile --project-path $project --timeout 120 --format json
    if($LASTEXITCODE -ne 0){throw 'Unity scripts did not compile'}
    # Compilation can finish just before Pipeline reconnects after domain reload.
    & $UnityCli status --until-ready --project-path $project --format json
    if($LASTEXITCODE -ne 0){throw 'Editor did not reconnect after compilation'}
    $build=if($Target -eq 'WebGL'){'TowerDuel.Editor.TowerDuelBuild.BuildWeb();'}else{'TowerDuel.Editor.TowerDuelBuild.BuildWindows();'}
    & $UnityCli command script eval --caller plugin --skill unity-cli --project-path $project --timeout 1800 --format json -- --code $build --timeout 1780
    if($LASTEXITCODE -ne 0){throw 'Unity build failed; inspect Builds/build-result.json and the Editor log'}
    Write-Output (Join-Path $project $(if($Target -eq 'WebGL'){'Builds/WebGL/index.html'}else{'Builds/Windows/TowerDuel.exe'}))
} finally {
    if($owned -and -not $owned.HasExited){
        & $UnityCli command script eval --caller plugin --skill unity-cli --project-path $project --format json -- --code 'UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);'
        $owned.Dispose()
    }
}
