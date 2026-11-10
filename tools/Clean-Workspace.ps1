<#
.SYNOPSIS
列出或清理白名單內的可重建快取與舊產物。
.DESCRIPTION
分類：維護。所有命令從 gamenetkit 根目錄執行。
前提：PowerShell 7；實際清理前需結束 Unity 與測試。
行為與產物：實際執行會刪除白名單產物；先用 -WhatIf 檢視。
.EXAMPLE
./tools/Clean-Workspace.ps1 -WhatIf
#>
#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$IncludeBuildCache,
    [switch]$IncludeOldBuilds,
    [ValidateRange(1,20)][int]$KeepDeploymentPackages=1
)
$ErrorActionPreference='Stop'
$frameworkRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$workspaceRoot=$frameworkRoot
if((Get-Item -LiteralPath $workspaceRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'The cleanup root must not be a filesystem link.'}
$prefix=$workspaceRoot+[IO.Path]::DirectorySeparatorChar
$targets=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-CleanupTarget([string]$Path,[string]$Reason){
    $full=[IO.Path]::GetFullPath($Path)
    if(-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw "Cleanup target escaped workspace: $full"}
    if(Test-Path -LiteralPath $full){$targets[$full]=$Reason}
}

# Enumerate source projects only. Never discover candidates inside Library or downloaded packages.
$projects=[Collections.Generic.List[string]]::new()
foreach($entry in Get-ChildItem -LiteralPath (Join-Path $frameworkRoot 'examples') -Directory){
    foreach($path in @($entry.FullName,(Join-Path $entry.FullName 'client'))){
        if(Test-Path -LiteralPath (Join-Path $path 'ProjectSettings/ProjectVersion.txt')){$projects.Add($path)}
    }
}
$projects.Add((Join-Path $frameworkRoot 'tests/sdk-unity'))
foreach($project in $projects){
    foreach($name in @('Library','Temp','Obj','Logs')){Add-CleanupTarget (Join-Path $project $name) 'Unity rebuildable cache'}
    if($IncludeOldBuilds){
        $buildRoot=Join-Path $project 'Builds'
        if(Test-Path -LiteralPath $buildRoot){
            foreach($build in Get-ChildItem -LiteralPath $buildRoot -Directory){
                # Keep every browser example and the current full game's Windows player.
                if($build.Name -eq 'WebGL'){continue}
                if($project -eq (Join-Path $frameworkRoot 'examples/rumblebound/client') -and $build.Name -eq 'Windows'){continue}
                Add-CleanupTarget $build.FullName 'Old player or optional-removal build'
            }
        }
    }
}
$localRoot=Join-Path $frameworkRoot '.local'
# Explicit tool-output allowlist: credentials and user replay directories are not candidates.
foreach($name in @('battle-removal','combat-player','docs-refresh','game-domain','history-lagcomp','interest',
    'interpolation','learning-examples','lesson-players','module-lessons','network-recovery','objects-actions',
    'prediction','replay','minimal-startup','shared-world','shared-world-teaching','transport-foundation',
    'transport-platform','unity-lessons','codec','codec-replication-perf')){
    Add-CleanupTarget (Join-Path $localRoot $name) 'Generated test host, browser profile or test output'
}
$gameOutput=Join-Path $localRoot 'rumblebound'
if(Test-Path -LiteralPath $gameOutput){
    foreach($name in @('battle-tests','battle-player-tests','battle-browser-tests')){
        Add-CleanupTarget (Join-Path $gameOutput $name) 'Generated game test host or browser profile'
    }
}
if(Test-Path -LiteralPath $localRoot){
    foreach($stash in Get-ChildItem -LiteralPath $localRoot -Directory){
        if($stash.Name -match '^(removed-modules|server-removal)-' -and
            -not (Get-ChildItem -LiteralPath $stash.FullName -Force | Select-Object -First 1)){
            Add-CleanupTarget $stash.FullName 'Empty restored module-check stash'
        }
    }
}
$deployRoot=Join-Path $localRoot 'deploy'
if(Test-Path -LiteralPath $deployRoot){
    $packages=@(Get-ChildItem -LiteralPath $deployRoot -Directory |
        Where-Object {$_.Name -match '^\d{8}T\d{6}Z-[a-f0-9]{8}$' -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'game.tar.gz') -PathType Leaf)} |
        Sort-Object Name -Descending)
    foreach($package in $packages | Select-Object -Skip $KeepDeploymentPackages){Add-CleanupTarget $package.FullName 'Older local deployment package'}
    foreach($package in $packages | Select-Object -First $KeepDeploymentPackages){
        Add-CleanupTarget (Join-Path $package.FullName 'bundle') 'Unpacked duplicate of retained deployment archive'
    }
}
if($IncludeBuildCache){
    foreach($cacheRoot in @((Join-Path $frameworkRoot '.cache'))){
        foreach($name in @('gobuild','go-build')){Add-CleanupTarget (Join-Path $cacheRoot $name) 'Rebuildable Go compiler cache; downloaded modules retained'}
    }
}

# Validate the entire deletion plan before deleting anything.
$tracked=@()
if((Get-Command git -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath (Join-Path $workspaceRoot '.git'))){
    $tracked=@(& git -C $workspaceRoot -c "safe.directory=$workspaceRoot" -c core.quotepath=false ls-files)
    if($LASTEXITCODE -ne 0){throw 'Cannot verify tracked-file protection'}
}
$runningPaths=@(Get-Process | ForEach-Object {try{if($_.Path){$_.Path}}catch{}})
$plan=[Collections.Generic.List[object]]::new()
foreach($target in $targets.Keys | Sort-Object){
    $cursor=$target
    while($cursor -ne $workspaceRoot){
        if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Refusing cleanup through a filesystem link: $cursor"}
        $cursor=Split-Path -Parent $cursor
    }
    $relative=[IO.Path]::GetRelativePath($workspaceRoot,$target).Replace('\','/')
    if($tracked | Where-Object {$_ -eq $relative -or $_.StartsWith($relative+'/',[StringComparison]::OrdinalIgnoreCase)} | Select-Object -First 1){
        throw "Refusing to delete tracked source: $relative"
    }
    if($runningPaths | Where-Object {$_ -eq $target -or $_.StartsWith($target+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)} | Select-Object -First 1){
        Write-Warning "Retaining running program and its directory: $relative"
        continue
    }
    $item=Get-Item -LiteralPath $target -Force
    $bytes=if($item.PSIsContainer){
        $options=[IO.EnumerationOptions]::new();$options.RecurseSubdirectories=$true
        $options.AttributesToSkip=[IO.FileAttributes]::ReparsePoint;$options.IgnoreInaccessible=$false
        [long]$size=0
        foreach($file in [IO.DirectoryInfo]::new($target).EnumerateFiles('*',$options)){$size+=$file.Length}
        $size
    }else{$item.Length}
    $plan.Add([pscustomobject]@{Path=$target;Relative=$relative;Bytes=[long]$bytes;Reason=$targets[$target]})
}
if(-not $WhatIfPreference -and (Get-Process Unity -ErrorAction SilentlyContinue)){throw 'Close Unity Editors and finish Unity tests before clearing their caches.'}
[long]$removed=0
$failed=[Collections.Generic.List[string]]::new()
foreach($entry in $plan){
    if($PSCmdlet.ShouldProcess($entry.Path,$entry.Reason)){
        # Every absolute target has already passed containment, link and source checks above.
        try{
            Remove-Item -LiteralPath $entry.Path -Recurse -Force
            $removed+=$entry.Bytes
        }catch{
            $failed.Add($entry.Relative)
            Write-Warning "Could not finish $($entry.Relative): $($_.Exception.Message)"
        }
    }
}
$planned=($plan | Measure-Object -Property Bytes -Sum).Sum
[pscustomobject]@{Targets=$plan.Count;PlannedBytes=[long]$planned;RemovedBytes=$removed;RemovedGiB=[math]::Round($removed/1GB,2);FailedTargets=$failed.ToArray()}
if($failed.Count -gt 0){throw 'Some generated files could not be removed; see FailedTargets. Other cleanup targets have been processed.'}
