<# Regression tests against the real client session, SDK transport inbox and spell renderer. #>
param([string]$UnityCli='', [string]$Output='')
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
$project=Join-Path $example 'client'
. (Join-Path $repo 'tools/internal/Toolchain.ps1')
if(-not $UnityCli){$UnityCli=Join-Path $repo '.local/unity-cli/bin/unity.exe'}
if(-not $Output){$Output=Join-Path $repo 'artifacts/tower-duel/client-tests'}
New-Item -ItemType Directory -Path $Output -Force | Out-Null
function Invoke-Unity([string[]]$Arguments){
    $result=& $UnityCli @Arguments
    if($LASTEXITCODE -ne 0){throw "Unity command failed: $($Arguments -join ' ')`n$($result -join "`n")"}
    $result
}
function Eval([string]$Code){
    Invoke-Unity -Arguments @('command','script','eval','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--code',$Code)
}
$owned=$null;$prepared=$false;$playing=$false;$state=$null
try {
    $status=& $UnityCli status --project-path $project --format json | ConvertFrom-Json
    if(-not $status.data.instances){
        $editor=Resolve-GameNetKitUnityEditor -ProjectPath $project
        $log=Join-Path ([IO.Path]::GetFullPath($Output)) 'editor.log'
        $owned=Start-Process -FilePath $editor -ArgumentList @('-batchmode','-projectPath',('"'+$project+'"'),'-buildTarget','Win64','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
    }
    $null=Invoke-Unity -Arguments @('status','--until-ready','--project-path',$project,'--format','json')
    $state=Eval 'return new {playing=UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode, scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, dirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty, count=UnityEngine.SceneManagement.SceneManager.sceneCount};' | ConvertFrom-Json
    if($state.playing -or $state.dirty -or $state.count -ne 1){throw 'Client tests require an idle Editor with one clean scene; the current scene has been preserved.'}
    $null=Invoke-Unity -Arguments @('recompile','--project-path',$project,'--timeout','120','--format','json')
    $null=Invoke-Unity -Arguments @('status','--until-ready','--project-path',$project,'--format','json')
    $null=Eval 'TowerDuel.Editor.TowerDuelEnvironmentContent.Prepare();'
    $null=Eval 'TowerDuel.Editor.TowerDuelVfxContent.Prepare();'
    $balanceResult=Eval 'return TowerDuel.Editor.TowerDuelBalanceTests.Run();' | ConvertFrom-Json
    if($balanceResult -notlike 'TOWER_DUEL_BALANCE_TESTS PASS:*'){throw "Balance editor tests did not pass: $balanceResult"}
    $null=Eval 'UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);'
    $prepared=$true
    $null=Invoke-Unity -Arguments @('command','editor','set_auto_tick','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json')
    $null=Invoke-Unity -Arguments @('command','editor.playmode','set_run_in_background','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json')
    $null=Invoke-Unity -Arguments @('command','editor.playmode','start','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json')
    $playing=$true
    $null=Invoke-Unity -Arguments @('status','--until-ready','--project-path',$project,'--format','json')
    $frameWait=Invoke-Unity -Arguments @('command','job','wait','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--condition','{\"member\":\"UnityEngine.Time.frameCount\",\"op\":\"changed\"}','--timeout','10') | ConvertFrom-Json
    if(-not $frameWait.met){throw 'The Unity Player loop did not advance before the client tests.'}
    $result=Eval 'return TowerDuel.Editor.TowerDuelNetworkTests.Run();' | ConvertFrom-Json
    if($result -notlike 'TOWER_DUEL_NETWORK_TESTS PASS:*'){throw "Client regression tests did not pass: $result"}
    $vfxResult=Eval 'return TowerDuel.Editor.TowerDuelVfxTests.Run();' | ConvertFrom-Json
    if($vfxResult -notlike 'TOWER_DUEL_VFX_TESTS PASS:*'){throw "VFX regression tests did not pass: $vfxResult"}
    $spriteResult=Eval 'return TowerDuel.Editor.TowerDuelSpriteTests.Run();' | ConvertFrom-Json
    if($spriteResult -notlike 'TOWER_DUEL_B3_TESTS PASS:*'){throw "B3 sprite regression tests did not pass: $spriteResult"}
    [ordered]@{passed=$true;result=$result;vfx=$vfxResult;sprites=$spriteResult;unity=$script:GameNetKitVersions.unity} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $Output 'result.json')
} finally {
    if($playing){
        $null=Invoke-Unity -Arguments @('command','editor.playmode','stop','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json')
        $null=Invoke-Unity -Arguments @('status','--until-ready','--project-path',$project,'--format','json')
    }
    if($prepared){
        $null=Invoke-Unity -Arguments @('command','editor','set_auto_tick','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--enabled','false')
        $null=Invoke-Unity -Arguments @('command','editor.playmode','set_run_in_background','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--enabled','false')
        if($state.scene){$sceneLiteral=$state.scene.Replace('\','/');$null=Eval ('UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\"'+$sceneLiteral+'\");')}
    }
    if($owned -and -not $owned.HasExited){$null=Eval 'UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);';$owned.Dispose()}
}
