<# Builds an optional portable test bundle without modifying the normal build scripts. #>
param([switch]$SkipUnity,[string]$UnityCli='',[switch]$VerifyIsolation,[ValidateSet('Windows','WebGL')][string]$Target='Windows')
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
$bundle=Join-Path $example $(if($Target -eq 'WebGL'){'Builds/SoloWeb'}else{'Builds/Solo'})
$support=Join-Path $bundle 'Solo'
New-Item -ItemType Directory -Path $bundle,$support -Force | Out-Null
if(-not $SkipUnity){
    $project=Join-Path $example 'client'
    if(-not $UnityCli){$UnityCli=Join-Path $repo '.local/unity-cli/bin/unity.exe'}
    $owned=$null
    function Wait-SoloEditor {
        $deadline=[DateTime]::UtcNow.AddSeconds(240)
        while([DateTime]::UtcNow -lt $deadline){
            if($owned -and $owned.HasExited){throw "Unity exited with code $($owned.ExitCode); inspect $bundle/editor.log"}
            $snapshot=& $UnityCli status --project-path $project --format json | ConvertFrom-Json
            if(@($snapshot.data.instances | Where-Object state -eq 'ready').Count -gt 0){return}
            Start-Sleep -Seconds 2
        }
        throw "Unity did not become ready; inspect $bundle/editor.log"
    }
    try {
        $status=& $UnityCli status --project-path $project --format json | ConvertFrom-Json
        if(-not $status.data.instances){
            $editor=Resolve-GameNetKitUnityEditor -ProjectPath $project
            $buildTarget=if($Target -eq 'WebGL'){'WebGL'}else{'Win64'}
            $owned=Start-Process -FilePath $editor -ArgumentList @('-batchmode','-projectPath',('"'+$project+'"'),'-buildTarget',$buildTarget,
                '-logFile',('"'+(Join-Path $bundle 'editor.log')+'"')) -WindowStyle Hidden -PassThru
        }
        Wait-SoloEditor
        & $UnityCli recompile --project-path $project --timeout 120 --format json
        if($LASTEXITCODE -ne 0){throw 'Optional solo adapter did not compile'}
        Wait-SoloEditor
        $code=if($VerifyIsolation){'TowerDuel.Solo.Editor.TowerDuelSoloBuild.VerifyNormalBuild();'}elseif($Target -eq 'WebGL'){'TowerDuel.Solo.Editor.TowerDuelSoloBuild.BuildWeb();'}else{'TowerDuel.Solo.Editor.TowerDuelSoloBuild.Build();'}
        & $UnityCli command script eval --caller plugin --skill unity-cli --project-path $project --timeout 900 --format json -- --code $code --timeout 880
        if($LASTEXITCODE -ne 0){throw 'Optional solo Player build failed'}
    } finally {
        if($owned -and -not $owned.HasExited){
            & $UnityCli command script eval --caller plugin --skill unity-cli --project-path $project --format json -- --code 'UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);'
            $owned.Dispose()
        }
    }
}
if($VerifyIsolation){Write-Output 'Normal Player isolation verification passed';return}
if($Target -eq 'WebGL'){
    if(-not(Test-Path -LiteralPath (Join-Path $bundle 'WebGL/solo-build.txt'))){throw 'Solo WebGL output is missing'}
    Write-Output "Optional solo WebGL: $(Join-Path $bundle 'WebGL/index.html')";return
}
if(-not(Test-Path -LiteralPath (Join-Path $bundle 'TowerDuel.exe'))){throw 'Build the optional Player first, or omit -SkipUnity.'}
foreach($entry in @(@{Source=(Join-Path $example 'server');Name='tower-duel-server.exe'},@{Source=(Join-Path $PSScriptRoot 'backend');Name='tower-duel-solo.exe'})){
    Push-Location $entry.Source
    try {
        & $Go build -o (Join-Path $support $entry.Name) .
        if($LASTEXITCODE -ne 0){throw "Build failed: $($entry.Name)"}
    } finally {Pop-Location}
}
foreach($file in @('map.json','rules.json')){Copy-Item -LiteralPath (Join-Path $example "client/Assets/Resources/TowerDuel/$file") -Destination (Join-Path $support $file)}
Copy-Item -LiteralPath (Join-Path $repo 'packages/unity/transport/Browser/transport.js') -Destination (Join-Path $support 'transport.js')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Play-Solo.ps1') -Destination (Join-Path $support 'Play-Solo.ps1')
foreach($file in @('Start-Solo.cmd','Start-Sandbox.cmd')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $bundle $file)}
Write-Output "Optional solo bundle: $(Join-Path $bundle 'Start-Solo.cmd')"
