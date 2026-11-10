<#
.SYNOPSIS
驗證選用 Go 模組移除後的 SDK 與應用備援。
.DESCRIPTION
分類：進階框架驗證。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go；需序列執行，避免其他程序同時編譯來源。
行為與產物：會暫移 server 選用模組並在 finally 還原；日誌存 artifacts。
.EXAMPLE
./tests/scripts/Test-GoModuleRemoval.ps1 -Module all
#>
param([ValidateSet('history','lagcomp','interest','codec','replay','all')][string]$Module='all')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot
$names=if($Module -eq 'all'){@('history','lagcomp','interest','codec','replay')}else{@($Module)}
$stashRoot=[IO.Path]::GetFullPath((Join-Path $root ('.local/server-removal-'+[Guid]::NewGuid().ToString('N'))))
$locations=@{}
foreach($name in $names){
 $source=[IO.Path]::GetFullPath((Join-Path $root "server/$name"));$stash=[IO.Path]::GetFullPath((Join-Path $stashRoot $name))
 foreach($path in @($source,$stash)){if(-not $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Server-removal path escaped framework'}}
 $locations[$name]=@($source,$stash)
}
$moved=@();$tags=($names|ForEach-Object{"rf_no_$_"})-join ','
$log=Join-Path $root "artifacts/server-without-$Module.log"
try{
 New-Item -ItemType Directory -Force $stashRoot,(Join-Path $root 'artifacts')|Out-Null
 foreach($name in $names){Move-Item -LiteralPath $locations[$name][0] -Destination $locations[$name][1];$moved+=$name}
 foreach($part in @('server','examples/rumblebound/game','examples/rumblebound/server')){
  Push-Location (Join-Path $root $part)
  try{
   $patterns=@('./...')
   # Always pass an array, including the single ./... pattern.
   $arguments=@('test','-count=1',"-tags=$tags")+@($patterns)
   & $Go @arguments 2>&1|Tee-Object -FilePath $log -Append
   if($LASTEXITCODE -ne 0){throw "Server module removal failed in $part"}
   & $Go vet "-tags=$tags" @patterns
   if($LASTEXITCODE -ne 0){throw "Server module removal vet failed in $part"}
  }finally{Pop-Location}
 }
 "RF_SERVER_REMOVAL PASS: $Module physically absent; Go SDK and game tests/vet passed with $tags"|Tee-Object -FilePath $log -Append
}finally{
 foreach($name in $moved){Move-Item -LiteralPath $locations[$name][1] -Destination $locations[$name][0]}
 if((Test-Path -LiteralPath $stashRoot) -and -not (Get-ChildItem -LiteralPath $stashRoot -Force | Select-Object -First 1)){
  Remove-Item -LiteralPath $stashRoot -Force
 }
}
