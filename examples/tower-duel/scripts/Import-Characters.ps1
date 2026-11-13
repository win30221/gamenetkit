param([string]$Source='')
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
if(-not $Source){$Source=Join-Path $repo '.local/tower-duel/characters'}
$characters=Join-Path $Source 'adventurers/KayKit_Adventurers_2.0_FREE';$animations=Join-Path $Source 'animations/KayKit_Character_Animations_1.1'
$target=Join-Path $example 'client/Assets/TowerDuelArt';New-Item -ItemType Directory -Path $target -Force | Out-Null
$files=[Collections.Generic.List[object]]::new()
function Copy-Source([string]$From,[string]$To){
    $destination=Join-Path $target $To;New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $From -Destination $destination
    $files.Add([ordered]@{path=$To.Replace('\','/');sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash})
}
foreach($name in @('Knight','Ranger','Rogue','Rogue_Hooded','Mage','Barbarian')){Copy-Source (Join-Path $characters "Characters/fbx/$name.fbx") "Characters/$name.fbx"}
foreach($name in @('knight','ranger','rogue','mage','barbarian')){Copy-Source (Join-Path $characters "Textures/${name}_texture.png") "Textures/${name}_texture.png"}
foreach($name in @('sword_1handed','shield_square','bow_withString','arrow_bow','smokebomb','sword_2handed','axe_2handed')){Copy-Source (Join-Path $characters "Assets/fbx/$name.fbx") "Equipment/$name.fbx"}
foreach($name in @('General','MovementBasic','CombatMelee','CombatRanged')){Copy-Source (Join-Path $animations "Animations/fbx/Rig_Medium/Rig_Medium_$name.fbx") "Animations/Rig_Medium_$name.fbx"}
Copy-Source (Join-Path $characters 'License.txt') 'Licenses/Adventurers.txt';Copy-Source (Join-Path $animations 'License.txt') 'Licenses/Animations.txt'
$manifest=[ordered]@{creator='Kay Lousberg';license='CC0 1.0';archives=@((Get-Content (Join-Path $Source 'Adventurers-Free-2.0.zip.json') -Raw | ConvertFrom-Json),(Get-Content (Join-Path $Source 'Animations-Free.zip.json') -Raw | ConvertFrom-Json));files=$files;processing='Selected original FBX and textures; Unity imports Generic rig, copies required clips, creates URP materials and equipped prefabs. No source mesh edits.'}
$json=$manifest | ConvertTo-Json -Depth 8
$json | Set-Content -LiteralPath (Join-Path $target 'provenance.json') -Encoding utf8
$json | Set-Content -LiteralPath (Join-Path $example 'art/character-manifest.json') -Encoding utf8
Write-Output "Imported $($files.Count) licensed source files into $target"
