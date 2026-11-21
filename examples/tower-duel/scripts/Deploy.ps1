<# Deploy the WebGL player and independent API beside the existing Rumblebound site. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$VmHost,
    [Parameter(Mandatory)][ValidatePattern('^(?=.{1,253}$)[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*\.[a-zA-Z]{2,}$')][string]$Domain,
    [string]$IdentityFile='',
    [switch]$PackageOnly,
    [switch]$StaticOnly,
    [switch]$ClientOnly
)
$ErrorActionPreference='Stop'
if($StaticOnly -and $ClientOnly){throw 'Choose either -StaticOnly or -ClientOnly'}
$game=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $game '../..'))
$web=Join-Path $game 'client/Builds/WebGL'
foreach($file in @('index.html','tower-duel-build.txt','Build/WebGL.loader.js','Build/WebGL.framework.js.gz','Build/WebGL.data.gz','Build/WebGL.wasm.gz')){
    if(-not(Test-Path -LiteralPath (Join-Path $web $file) -PathType Leaf)){throw "Missing WebGL build: $file"}
}
$rules=Get-Content -LiteralPath (Join-Path $game 'client/Assets/Resources/TowerDuel/rules.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$protocol=[int]$rules.schema
$buildMarker="TowerDuel protocol $protocol / $($rules.id)"
if((Get-Content -LiteralPath (Join-Path $web 'tower-duel-build.txt') -Raw).Trim() -ne $buildMarker){throw 'WebGL build does not match the canonical protocol/rules revision'}
$rulesHash=(Get-FileHash -LiteralPath (Join-Path $game 'client/Assets/Resources/TowerDuel/rules.json') -Algorithm SHA256).Hash.ToLowerInvariant()
$mapHash=(Get-FileHash -LiteralPath (Join-Path $game 'client/Assets/Resources/TowerDuel/map.json') -Algorithm SHA256).Hash.ToLowerInvariant()
$env:GAMENETKIT_CACHE=Join-Path $repo '.cache'
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
$release=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$out=Join-Path $repo ".local/deploy/tower-duel/$release"
$bundle=Join-Path $out 'bundle'
New-Item -ItemType Directory -Force (Join-Path $bundle 'public'),(Join-Path $bundle 'protocol') | Out-Null
$savedOS=$env:GOOS;$savedArch=$env:GOARCH;$savedCGO=$env:CGO_ENABLED
try{
    $env:GOOS='linux';$env:GOARCH='amd64';$env:CGO_ENABLED='0'
    Push-Location (Join-Path $game 'server')
    try{& $Go build -buildvcs=false -trimpath -o (Join-Path $bundle 'tower-duel-api') .;if($LASTEXITCODE -ne 0){throw 'Linux API build failed'}}finally{Pop-Location}
}finally{$env:GOOS=$savedOS;$env:GOARCH=$savedArch;$env:CGO_ENABLED=$savedCGO}
Copy-Item -Path (Join-Path $web '*') -Destination (Join-Path $bundle 'public') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'packages/unity/transport/Browser/transport.js') -Destination (Join-Path $bundle 'public/transport.js')
Copy-Item -LiteralPath (Join-Path $game 'client/Assets/Resources/TowerDuel/map.json'),(Join-Path $game 'client/Assets/Resources/TowerDuel/rules.json') -Destination (Join-Path $bundle 'protocol')
$site=[IO.File]::ReadAllText((Join-Path $game 'deploy/site.caddy.template')).Replace('__DOMAIN__',$Domain.ToLowerInvariant())
[IO.File]::WriteAllText((Join-Path $bundle 'site.caddy'),$site,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $bundle 'install.sh'),[IO.File]::ReadAllText((Join-Path $game 'deploy/install.sh')).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
$manifest=[ordered]@{release=$release;origin="https://$Domain";createdAt=[DateTime]::UtcNow.ToString('o');target='linux/amd64';protocol=$protocol;rulesRevision=$rules.id;rulesHash=$rulesHash;mapHash=$mapHash;files=@()}
function Get-BundleRelative([string]$Full){$b=$bundle.TrimEnd('\','/');if($Full.StartsWith($b,[StringComparison]::OrdinalIgnoreCase)){return $Full.Substring($b.Length).TrimStart('\','/').Replace('\','/')};return $Full.Replace('\','/')}
$manifest.files=@(Get-ChildItem $bundle -Recurse -File | ForEach-Object {[ordered]@{path=(Get-BundleRelative $_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$_.Length}})
$manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $bundle 'manifest.json') -Encoding utf8
$archive=Join-Path $out 'game.tar.gz'
& tar -czf $archive -C $bundle .
if($LASTEXITCODE -ne 0){throw 'Packaging failed'}
Write-Output "Package: $archive"
if($PackageOnly){return}
$options=@('-o','BatchMode=yes','-o','ConnectTimeout=15','-o','StrictHostKeyChecking=yes')
if($IdentityFile){$options+=@('-i',(Resolve-Path -LiteralPath $IdentityFile).Path)}
$target="root@$VmHost";$remote="/tmp/tower-duel-$release.tar.gz"
& scp @options $archive "${target}:$remote"
if($LASTEXITCODE -ne 0){throw 'Upload failed'}
$sha=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$mode=if($StaticOnly){'static'}elseif($ClientOnly){'client'}else{'full'}
$command="set -eu; echo '$sha  $remote' | sha256sum -c -; mkdir -p /opt/rumblebound/tower-duel/releases/$release; tar -xzf $remote -C /opt/rumblebound/tower-duel/releases/$release; sh /opt/rumblebound/tower-duel/releases/$release/install.sh $release $mode"
& ssh @options $target $command
if($LASTEXITCODE -ne 0){throw "Remote deployment failed; package retained at $archive"}
$url="https://$Domain"
$ready=$false
for($i=0;$i -lt 24;$i++){
    try{
        $health=Invoke-RestMethod "$url/healthz" -TimeoutSec 5
        $page=Invoke-WebRequest "$url/" -TimeoutSec 10
        $config=Invoke-RestMethod "$url/api/config" -TimeoutSec 5
        $marker=(Invoke-WebRequest "$url/tower-duel-build.txt" -TimeoutSec 5).Content
        if($marker -is [byte[]]){$marker=[Text.Encoding]::UTF8.GetString($marker)}
        if($health.Trim() -eq 'ok' -and $page.Content -match 'WebGL.loader.js' -and $config.protocol -eq $protocol -and $config.rulesHash -eq $rulesHash -and $config.mapHash -eq $mapHash -and $config.cards.Count -eq $rules.cards.Count -and $marker.Trim() -eq $buildMarker){$ready=$true;break}
    }catch{if($i -eq 23){Write-Warning $_}}
    Start-Sleep -Seconds 3
}
if(-not $ready){throw "HTTPS not ready. Inspect docker logs rumblebound-web. URL: $url"}
$manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $out 'deployed.json') -Encoding utf8
Write-Output "Playable URL: $url/"
Write-Output "Release: $release"
