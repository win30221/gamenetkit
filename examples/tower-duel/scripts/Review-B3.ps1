param([string]$UnityCli='', [string]$Output='', [ValidateSet('guard','archer','swords','bomber','charger','siege')][string]$Kind='guard')
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
$project=Join-Path $example 'client'
if(-not $UnityCli){$UnityCli=Join-Path $repo '.local/unity-cli-pacing/unity.exe'}
if(-not $Output){$Output=Join-Path $repo ('artifacts/tower-duel/sprites-b3/'+$(if($Kind -eq 'guard'){'native'}else{'native-'+$Kind}))}
$Output=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null
function Invoke-Unity([string[]]$Arguments){
    $result=& $UnityCli @Arguments
    if($LASTEXITCODE -ne 0){throw "Unity review command failed: $($Arguments -join ' ')`n$($result -join "`n")"}
    $result
}
function Eval([string]$Code){Invoke-Unity @('command','script','eval','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--code',$Code)}
$prepared=$false;$playing=$false
try{
    $null=Invoke-Unity @('status','--until-ready','--project-path',$project,'--format','json')
    $null=Eval ('TowerDuel.Editor.TowerDuelSpriteContent.Prepare(); return TowerDuel.Editor.TowerDuelSpriteReview.Capture("'+$Kind+'");')
    $null=Eval 'TowerDuel.Editor.TowerDuelSpriteReview.PrepareUI();'
    $prepared=$true
    $null=Invoke-Unity @('command','editor','set_auto_tick','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json')
    $null=Invoke-Unity @('command','editor.playmode','set_run_in_background','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json')
    $null=Invoke-Unity @('command','editor.playmode','start','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json')
    $playing=$true
    $null=Invoke-Unity @('status','--until-ready','--project-path',$project,'--format','json')
    $null=Invoke-Unity @('command','job','wait','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--condition','{"member":"UnityEngine.Time.frameCount","op":"changed"}','--timeout','10')
    $null=Eval 'UnityEngine.Application.runInBackground=true; UnityEditor.EditorApplication.QueuePlayerLoopUpdate();'
    $null=Eval ('TowerDuel.Editor.TowerDuelSpriteReview.ShowUI("'+$Kind+'");')
    $null=Invoke-Unity @('command','job','wait','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--condition','{"member":"UnityEngine.Time.frameCount","op":"changed"}','--timeout','10')
    $null=Invoke-Unity @('command','capture','game_view','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--width','720','--height','1000','--output-path',(Join-Path $Output 'card-ui-warmup.png'))
    $null=Invoke-Unity @('command','job','wait','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--condition','{"member":"UnityEngine.Time.frameCount","op":"changed"}','--timeout','10')
    Eval 'var ui=UnityEngine.Object.FindAnyObjectByType<UnityEngine.UIElements.UIDocument>().rootVisualElement; var card=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(ui,"guardCard"); return new {width=UnityEngine.Screen.width,height=UnityEngine.Screen.height,card=card.worldBound.ToString(),display=card.resolvedStyle.display.ToString()};'
    Invoke-Unity @('command','capture','game_view','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--width','720','--height','1000','--source','screen','--output-path',(Join-Path $Output 'card-ui.png'))
} finally {
    if($playing){
        $null=Invoke-Unity @('command','editor.playmode','stop','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json')
        $null=Invoke-Unity @('status','--until-ready','--project-path',$project,'--format','json')
    }
    if($prepared){$null=Eval 'TowerDuel.Editor.TowerDuelSpriteReview.FinishUI();'}
    $null=Invoke-Unity @('command','editor','set_auto_tick','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--enabled','false')
    $null=Invoke-Unity @('command','editor.playmode','set_run_in_background','--caller','plugin','--skill','unity-cli','--project-path',$project,'--format','json','--','--enabled','false')
}
