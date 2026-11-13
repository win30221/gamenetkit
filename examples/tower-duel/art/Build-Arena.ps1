<#
.SYNOPSIS
Rebuild the standalone Twinbridge arena and its Unity import package.
.EXAMPLE
./examples/tower-duel/art/Build-Arena.ps1
#>
[CmdletBinding()]
param(
    [string]$Blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe',
    [string]$UnityEditor = '',
    [switch]$SkipBlender,
    [switch]$SkipRender
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$outputRoot = Join-Path $repoRoot 'artifacts/kaykit-arena'
$projectRoot = Join-Path $outputRoot 'UnityPreview'
$logRoot = Join-Path $repoRoot '.local/kaykit-arena'
. (Join-Path $repoRoot 'tools/internal/Toolchain.ps1')
$editorPath = Resolve-GameNetKitUnityEditor -Path $UnityEditor
New-Item -ItemType Directory -Force $outputRoot,$logRoot | Out-Null

if (-not $SkipBlender) {
    if (-not (Test-Path -LiteralPath $Blender -PathType Leaf)) { throw "Blender was not found: $Blender" }
    $blenderArgs = @('--background','--factory-startup','--python-exit-code','1','--python',(Join-Path $PSScriptRoot 'build_arena.py'))
    if ($SkipRender) { $blenderArgs += @('--','--skip-render') }
    & $Blender @blenderArgs *> (Join-Path $logRoot 'build.log')
    if ($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath (Join-Path $logRoot 'build.log') -SimpleMatch 'ARENA_COMPLETE')) { throw "Blender failed; see $logRoot/build.log" }
}
foreach ($name in @('TwinbridgeArena.fbx','hexagons_medieval.png','KayKit-LICENSE.txt','layout.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputRoot $name))) { throw "Missing Blender output: $name" }
}
New-Item -ItemType Directory -Force "$projectRoot/Assets/Editor","$projectRoot/Assets/TwinbridgeArena","$projectRoot/Packages","$projectRoot/ProjectSettings" | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BuildArenaPreview.cs') -Destination "$projectRoot/Assets/Editor/BuildArenaPreview.cs"
foreach ($name in @('TwinbridgeArena.fbx','hexagons_medieval.png','KayKit-LICENSE.txt','layout.json')) {
    Copy-Item -LiteralPath (Join-Path $outputRoot $name) -Destination "$projectRoot/Assets/TwinbridgeArena/$name"
}
$manifestPath = "$projectRoot/Packages/manifest.json"
$manifest = if (Test-Path -LiteralPath $manifestPath) { Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable } else { @{ dependencies = @{} } }
$manifest.dependencies['com.unity.modules.physics'] = '1.0.0'
$manifest.dependencies['com.unity.modules.imageconversion'] = '1.0.0'
$manifest.dependencies['com.unity.render-pipelines.universal'] = '17.6.0'
Set-Content -LiteralPath $manifestPath -Value ($manifest | ConvertTo-Json -Depth 10) -Encoding utf8
Set-Content -LiteralPath "$projectRoot/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: $($script:GameNetKitVersions.unity)" -Encoding utf8
$validationPath = Join-Path $outputRoot 'unity-validation.txt'
if (Test-Path -LiteralPath $validationPath) { Remove-Item -LiteralPath $validationPath }
$arguments = @('-batchmode','-quit','-projectPath',('"{0}"' -f $projectRoot),'-executeMethod','BuildArenaPreview.Run','-logFile',('"{0}"' -f (Join-Path $logRoot 'unity.log')))
$process = Start-Process -FilePath $editorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(5)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $deadline) {
        $process.Kill()
        throw "Unity exceeded five minutes; see $logRoot/unity.log"
    }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $validationPath)) { throw "Unity validation failed; see $logRoot/unity.log" }
Get-Content -LiteralPath $validationPath
Write-Host "Ready: $outputRoot/TwinbridgeArena.unitypackage"
