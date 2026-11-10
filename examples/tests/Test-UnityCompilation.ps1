<#
.SYNOPSIS
編譯教學與完整遊戲的 Unity、Editor、原生／WebGL 條件分支。
.DESCRIPTION
分類：跨範例驗證。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Unity 參考組件；先匯入或建置 Rumblebound，還原 URP／Newtonsoft 套件。
行為與產物：只做參考組件編譯，不代表 Editor 匯入或 Player 執行；產物存 .local/unity-lessons。
.EXAMPLE
./examples/tests/Test-UnityCompilation.ps1
#>
param([string]$UnityData='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Toolchain.ps1"
$UnityData=Resolve-GameNetKitUnityData -Path $UnityData
$root=$script:GameNetKitRoot
$library=Join-Path $root 'examples/rumblebound/client/Library/ScriptAssemblies'
if(-not(Test-Path -LiteralPath $library -PathType Container)){throw 'Import examples/rumblebound/client with the pinned Unity Editor before reference compilation, for example by building RumbleboundExample.'}
$dotnet=Join-Path $UnityData 'DotNetSdk/dotnet.exe'
$sdk=Get-ChildItem (Join-Path $UnityData 'DotNetSdk/sdk') -Directory -Filter '8.*' | Sort-Object {[version]$_.Name.Split('-')[0]} -Descending | Select-Object -First 1
$compiler=Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'
$refs=@(Get-ChildItem (Join-Path $UnityData 'NetStandard/ref/2.1.0') -Filter '*.dll' | ForEach-Object FullName)
$engineRefs=@(Get-ChildItem (Join-Path $UnityData 'Managed/UnityEngine') -Filter 'UnityEngine*.dll' | ForEach-Object FullName)
$editorRefs=@(Get-ChildItem (Join-Path $UnityData 'Managed/UnityEngine') -Filter 'UnityEditor*.dll' | ForEach-Object FullName)
$out=Join-Path $root '.local/unity-lessons/compiled'
New-Item -ItemType Directory -Force $out | Out-Null
function Compile([string]$Name,[string[]]$Sources,[string[]]$References,[string]$Defines) {
    if($automaticRefs){$References+=$automaticRefs}
    $output=Join-Path $out ($Name+'.dll')
    $arguments=@('/nologo','/nostdlib','/target:library','/langversion:latest',('-out:"'+$output+'"'))
    if($Defines){$arguments+='/define:'+$Defines}
    $arguments+=@($References | Select-Object -Unique | ForEach-Object {'-r:"'+$_+'"'})
    $arguments+=@($Sources | ForEach-Object {'"'+$_+'"'})
    $response=Join-Path $out 'compile.rsp'
    $arguments | Set-Content -LiteralPath $response -Encoding utf8
    & $dotnet $compiler "@$response" | ForEach-Object {Write-Host $_}
    if($LASTEXITCODE -ne 0){throw "Unity reference compilation failed: $Name ($Defines)"}
    return $output
}
$modules=@('core','transport','interpolation','prediction','correction','simulation','clock','diagnostics','replication','actions','codec','playback')
$sdkAssemblies=@{}
foreach($module in $modules){
    $sources=@(Get-ChildItem (Join-Path $root "packages/unity/$module/Runtime") -Filter '*.cs' | ForEach-Object FullName)
    $name='Realtime.'+(Get-Culture).TextInfo.ToTitleCase($module)
    $sdkAssemblies[$name]=Compile $name $sources @($refs+@($sdkAssemblies.Values)) 'UNITY_EDITOR'
}
$gameSources=@(Get-ChildItem (Join-Path $root 'examples/rumblebound/game/unity/Runtime') -Filter '*.cs' | ForEach-Object FullName)
$gameAssembly=Compile 'Rumblebound.Gameplay' $gameSources @($refs+@($sdkAssemblies['Realtime.Core'])) 'UNITY_EDITOR'
$sdkAssemblies['Rumblebound.Gameplay']=$gameAssembly
$library=Join-Path $root 'examples/rumblebound/client/Library/ScriptAssemblies'
$packageRefs=@(Get-ChildItem $library -Filter 'Unity*.dll' | ForEach-Object FullName)
$newtonsoft=Get-ChildItem (Join-Path $root 'examples/rumblebound/client/Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' | Select-Object -First 1
if(-not $newtonsoft){throw 'Resolve RumbleboundExample Unity packages before reference compilation (Newtonsoft is an application dependency)'}
$automaticRefs=@((Join-Path $newtonsoft.FullName 'Runtime/Newtonsoft.Json.dll'))
$art=Compile 'Rumblebound.Presentation' @(Get-ChildItem (Join-Path $root 'examples/rumblebound/game/unity-art/Runtime') -Filter '*.cs' | ForEach-Object FullName) @($refs+$engineRefs+$packageRefs+@($gameAssembly,$sdkAssemblies['Realtime.Core'])) 'UNITY_EDITOR'
$sdkAssemblies['Rumblebound.Presentation']=$art
$battlePath=Join-Path $root 'examples/rumblebound/client/Assets/Scripts/Battle'
if(Test-Path -LiteralPath $battlePath){
    $battle=Compile 'Rumblebound.Example.Battle' @(Get-ChildItem $battlePath -Filter '*.cs' | ForEach-Object FullName) @($refs+@($sdkAssemblies.Values)) 'UNITY_EDITOR'
    $sdkAssemblies['Rumblebound.Example.Battle']=$battle
    $battleUnity=Compile 'Rumblebound.Example.Battle.Unity' @(Get-ChildItem (Join-Path $battlePath 'Unity') -Filter '*.cs' | ForEach-Object FullName) @($refs+$engineRefs+$packageRefs+@($sdkAssemblies.Values)) 'UNITY_EDITOR'
    $sdkAssemblies['Rumblebound.Example.Battle.Unity']=$battleUnity
}
$common=Join-Path $root 'examples/unity-common'
$presentation=Compile 'Realtime.Examples.Presentation' @(Get-ChildItem (Join-Path $common 'Runtime') -Filter '*.cs' | ForEach-Object FullName) @($refs+$engineRefs+@($sdkAssemblies.Values)) 'UNITY_EDITOR'
$build=Compile 'Realtime.Examples.Presentation.Editor' @(Get-ChildItem (Join-Path $common 'Editor') -Filter '*.cs' | ForEach-Object FullName) @($refs+$engineRefs+$editorRefs+@($presentation)) 'UNITY_EDITOR'
$assemblies=$sdkAssemblies.Clone()
$assemblies['Realtime.Examples.Presentation']=$presentation
$assemblies['Realtime.Examples.Presentation.Editor']=$build
foreach($file in $packageRefs){$assemblies[[IO.Path]::GetFileNameWithoutExtension($file)]=$file}
function Resolve-AssemblyReferences([object]$Definition) {
    foreach($name in $Definition.references){
        if(-not $assemblies.ContainsKey($name)){throw "Unresolved actual asmdef reference: $($Definition.name) -> $name"}
        $assemblies[$name]
    }
}
$lessons=@('prediction','network-recovery','objects-actions','replay','interest','codec','history-lagcomp')
foreach($lesson in $lessons){
    $project=Join-Path $root "examples/$lesson/client"
    $scripts=Join-Path $project 'Assets/Scripts'
    $definition=Get-ChildItem $scripts -Filter '*.asmdef' | Select-Object -First 1
    $config=Get-Content -LiteralPath $definition.FullName -Raw | ConvertFrom-Json
    $dependencies=@($config.references | ForEach-Object {$sdkAssemblies[$_]})
    if($dependencies -contains $null){throw "Unresolved SDK assembly: $lesson"}
    $application=Compile $config.name @(Get-ChildItem $scripts -Filter '*.cs' | ForEach-Object FullName) @($refs+$dependencies) 'UNITY_EDITOR'
    $assemblies[$config.name]=$application
    $unityConfig=Get-Content -LiteralPath (Get-ChildItem (Join-Path $scripts 'Unity') -Filter '*.asmdef' | Select-Object -First 1).FullName -Raw | ConvertFrom-Json
    $unityReferences=@(Resolve-AssemblyReferences $unityConfig)
    $unity=Compile $unityConfig.name @(Get-ChildItem (Join-Path $scripts 'Unity') -Filter '*.cs' | ForEach-Object FullName) @($refs+$engineRefs+$unityReferences) 'UNITY_EDITOR'
    $assemblies[$unityConfig.name]=$unity
    $editorConfig=Get-Content -LiteralPath (Get-ChildItem (Join-Path $project 'Assets/Editor') -Filter '*.asmdef' | Select-Object -First 1).FullName -Raw | ConvertFrom-Json
    $editorReferences=@(Resolve-AssemblyReferences $editorConfig)
    $null=Compile $editorConfig.name @(Get-ChildItem (Join-Path $project 'Assets/Editor') -Filter '*.cs' | ForEach-Object FullName) @($refs+$engineRefs+$editorRefs+$editorReferences) 'UNITY_EDITOR'
    Write-Output "UNITY_LESSON_REFERENCE PASS $lesson"
}
$gameEditor=Join-Path $root 'examples/rumblebound/client/Assets/Editor'
$gameEditorConfig=Get-Content -LiteralPath (Join-Path $gameEditor 'Rumblebound.Example.Editor.asmdef') -Raw | ConvertFrom-Json
$gameEditorRefs=@(Resolve-AssemblyReferences $gameEditorConfig)
$null=Compile $gameEditorConfig.name @(Get-ChildItem $gameEditor -Filter '*.cs' | ForEach-Object FullName) @($refs+$engineRefs+$editorRefs+$gameEditorRefs) 'UNITY_EDITOR'
Write-Output 'UNITY_BATTLE_REFERENCE PASS full application/Unity/Editor assemblies'
# Compile the conditional transport ABI used by WebGL as well as native WS.
$null=Compile 'Realtime.Transport.WebGL' @(Get-ChildItem (Join-Path $root 'packages/unity/transport/Runtime') -Filter '*.cs' | ForEach-Object FullName) $refs 'UNITY_WEBGL'
Write-Output 'UNITY_LESSONS_REFERENCE PASS (compilation only; Editor import and Player runtime require separate verification)'
