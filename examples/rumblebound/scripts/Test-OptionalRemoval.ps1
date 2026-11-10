<#
.SYNOPSIS
實際移除選用 Unity 套件後建置完整遊戲。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：有效 Unity 授權；需關閉該專案 Editor 並序列執行。
行為與產物：會暫移共用套件與修改 manifest／asmdef，結束時還原；屬進階移除契約驗證。
.EXAMPLE
./examples/rumblebound/scripts/Test-OptionalRemoval.ps1 -Module all
#>
param([ValidateSet('interpolation','prediction','correction','diagnostics','simulation','clock','replication','actions','codec','playback','all')][string]$Module='interpolation',[ValidateRange(30,3600)][int]$TimeoutSeconds=1200,[string]$UnityEditor='')
$ErrorActionPreference='Stop'
$Project='RumbleboundExample'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$root=$script:GameNetKitRoot
$projectPath=Join-Path $root 'examples/rumblebound/client'
$unity=Resolve-GameNetKitUnityEditor -Path $UnityEditor -ProjectPath $projectPath
if (Test-Path (Join-Path $projectPath 'Temp/UnityLockfile')) {throw 'Close the selected Unity Editor before the optional-removal check.'}
$names=if($Module -eq 'all'){@('interpolation','prediction','correction','diagnostics','simulation','clock','replication','actions','codec','playback')}else{@($Module)}
$stashRoot=[IO.Path]::GetFullPath((Join-Path $root ('.local/removed-modules-'+[Guid]::NewGuid().ToString('N'))))
$locations=@{}
foreach($name in $names){
    $package=[IO.Path]::GetFullPath((Join-Path $root "packages/unity/$name"))
    $stash=[IO.Path]::GetFullPath((Join-Path $stashRoot $name))
    foreach($path in @($package,$stash)){if(-not $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Removal-check path escaped framework directory.'}}
    $locations[$name]=@($package,$stash)
}
$manifest=Join-Path $projectPath 'Packages/manifest.json'
$lock=Join-Path $projectPath 'Packages/packages-lock.json'
$composition=Join-Path $projectPath 'Assets/Scripts/Battle/BattleNetcodeModules.cs'
$assembly=Join-Path $projectPath 'Assets/Scripts/Battle/Rumblebound.Example.Battle.asmdef'
$originalAssembly=if($assembly){[IO.File]::ReadAllBytes($assembly)}else{$null}
$originalManifest=[IO.File]::ReadAllBytes($manifest)
$originalComposition=[IO.File]::ReadAllBytes($composition)
$originalLock=if(Test-Path $lock){[IO.File]::ReadAllBytes($lock)}else{$null}
$moved=@()
$oldRemoved=$env:RF_REMOVED_MODULE
$process=$null
try {
    New-Item -ItemType Directory -Force $stashRoot,(Join-Path $root 'artifacts') | Out-Null
    foreach($name in $names){Move-Item -LiteralPath $locations[$name][0] -Destination $locations[$name][1];$moved+=$name}
    $data=[Text.Encoding]::UTF8.GetString($originalManifest)|ConvertFrom-Json -AsHashtable
    foreach($name in $names){$data.dependencies.Remove("com.realtime.$name")|Out-Null}
    [IO.File]::WriteAllText($manifest,($data|ConvertTo-Json -Depth 20))
    $code=[Text.Encoding]::UTF8.GetString($originalComposition)
    foreach($name in $names){$code=[regex]::Replace($code,"(?ms)^\s*// BEGIN OPTIONAL $name\r?\n.*?^\s*// END OPTIONAL $name[^\r\n]*\r?\n?",'')}
    [IO.File]::WriteAllText($composition,$code)
    if($assembly){
        $definition=[Text.Encoding]::UTF8.GetString($originalAssembly)|ConvertFrom-Json -AsHashtable
        $removedAssemblies=@($names|ForEach-Object {"Realtime.$_"})
        $definition.references=@($definition.references|Where-Object {$_ -notin $removedAssemblies})
        [IO.File]::WriteAllText($assembly,($definition|ConvertTo-Json -Depth 20))
    }
    $prefix='battle-without'
    $log=Join-Path $root "artifacts/$prefix-$Module.log"
    $method='Rumblebound.Example.Editor.ExampleBuild.WithoutOptional';$tag='RF_EXAMPLE_BUILD PASS'
    $env:RF_REMOVED_MODULE=$Module
    $process=Start-Process -FilePath $unity -ArgumentList @('-batchmode','-nographics','-quit','-buildTarget','Win64','-projectPath',"`"$projectPath`"",'-executeMethod',$method,'-logFile',"`"$log`"") -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while(-not $process.WaitForExit(1000)){if([DateTime]::UtcNow -ge $deadline){throw "Removal build timed out: $log"}}
    if ($process.ExitCode -ne 0) {throw "Physical module removal build failed: $log"}
    if (-not (Select-String -LiteralPath $log -SimpleMatch $tag -Quiet)) {throw 'Removal verification did not run.'}
    $buildFolder='NoOptionalNetcode'
    $dataFolder='RumbleboundExample_Data'
    $managed = Join-Path $projectPath "Builds/$buildFolder/$dataFolder/Managed"
    $assemblies = @(Get-ChildItem -LiteralPath $managed -Filter 'Realtime.*.dll' | ForEach-Object { $_.Name })
    foreach ($name in $names) {
        if ($assemblies -contains "Realtime.$name.dll") { throw "Removed module remains in the built player: $name" }
    }
    @{project=$Project;removed=$names;assemblies=$assemblies} | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $root "artifacts/$prefix-$Module-assemblies.json")
    Write-Output "OPTIONAL REMOVAL PASS: $Project $Module physically absent; game built with remaining modules and core fallbacks. $log"
} finally {
    if($process -and -not $process.HasExited){Stop-Process -Id $process.Id;$null=$process.WaitForExit(10000)}
    [IO.File]::WriteAllBytes($manifest,$originalManifest)
    [IO.File]::WriteAllBytes($composition,$originalComposition)
    if($assembly){[IO.File]::WriteAllBytes($assembly,$originalAssembly)}
    if ($null -ne $originalLock) {[IO.File]::WriteAllBytes($lock,$originalLock)}
    foreach($name in $moved){Move-Item -LiteralPath $locations[$name][1] -Destination $locations[$name][0]}
    $env:RF_REMOVED_MODULE=$oldRemoved
    if((Test-Path -LiteralPath $stashRoot) -and -not (Get-ChildItem -LiteralPath $stashRoot -Force | Select-Object -First 1)){
        Remove-Item -LiteralPath $stashRoot -Force
    }
}
