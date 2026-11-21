<# Deploy a single website with removable practice/sandbox modes and a ready multiplayer-only fallback. #>
[CmdletBinding()]
param(
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$VmHost='172.233.67.181',
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$')][string]$Domain='tower-duel.172.233.67.181.sslip.io',
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$')][string]$LegacyDomain='tower-duel-solo.172.233.67.181.sslip.io',
    [string]$IdentityFile='',
    [switch]$PackageOnly
)
$ErrorActionPreference='Stop'
if($Domain -eq $LegacyDomain){throw 'The legacy redirect must have a different hostname'}
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
$release=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$output=Join-Path $repo ".local/deploy/tower-duel-merged/$release"
$bundle=Join-Path $output 'bundle'
. (Join-Path $example 'scripts/WebPackageContent.ps1')
$packageProtocol = New-TowerDuelWebPackageContent $example $bundle ([ordered]@{Multiplayer='public'; Solo='modes-public'})
$rules = $packageProtocol.Rules
$env:GOCACHE=Join-Path $repo '.cache/gobuild'
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
$savedOS=$env:GOOS;$savedArch=$env:GOARCH;$savedCGO=$env:CGO_ENABLED
try {
    $env:GOOS='linux';$env:GOARCH='amd64';$env:CGO_ENABLED='0'
    foreach($build in @(@{Path=(Join-Path $example 'server');Name='tower-duel-api'},@{Path=(Join-Path $PSScriptRoot 'backend');Name='tower-duel-modes'})){
        Push-Location $build.Path
        try {& $Go build -buildvcs=false -trimpath -o (Join-Path $bundle $build.Name) .;if($LASTEXITCODE -ne 0){throw "Linux build failed: $($build.Name)"}} finally {Pop-Location}
    }
} finally {$env:GOOS=$savedOS;$env:GOARCH=$savedArch;$env:CGO_ENABLED=$savedCGO}
foreach($publicDir in 'public','modes-public'){
    Copy-Item -LiteralPath (Join-Path $repo 'packages/unity/transport/Browser/transport.js') -Destination (Join-Path $bundle "$publicDir/transport.js")
}
function Write-MergedText([string]$Path,[string]$Content){[IO.File]::WriteAllText($Path,$Content.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))}
Write-MergedText (Join-Path $bundle 'site.normal.caddy') ([IO.File]::ReadAllText((Join-Path $example 'deploy/site.caddy.template')).Replace('__DOMAIN__',$Domain.ToLowerInvariant()))
Write-MergedText (Join-Path $bundle 'site.merged.caddy') ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'deploy/merged-site.caddy.template')).Replace('__DOMAIN__',$Domain.ToLowerInvariant()))
Write-MergedText (Join-Path $bundle 'site.legacy.caddy') ("$($LegacyDomain.ToLowerInvariant()) {`n    redir https://$($Domain.ToLowerInvariant()){uri} 308`n}`n")
foreach($file in @('install-merged.sh','disable-modes.sh','tower-duel-modes.service')){
    Write-MergedText (Join-Path $bundle $file) ([IO.File]::ReadAllText((Join-Path $PSScriptRoot "deploy/$file")).Replace('__DOMAIN__',$Domain.ToLowerInvariant()))
}
$manifest=[ordered]@{release=$release;origin="https://$Domain";legacyOrigin="https://$LegacyDomain";protocol=$rules.schema;rulesRevision=$rules.id;
    rulesHash=$packageProtocol.rulesHash;mapHash=$packageProtocol.mapHash;
    modes=@('practice','sandbox','multiplayer');optionalService='tower-duel-modes.service';fallback='public';modePlayer='modes-public';files=@()}
function Get-BundleRelative([string]$Full){$b=$bundle.TrimEnd('\','/');if($Full.StartsWith($b,[StringComparison]::OrdinalIgnoreCase)){return $Full.Substring($b.Length).TrimStart('\','/').Replace('\','/')};return $Full.Replace('\','/')}
$manifest.files=@(Get-ChildItem -LiteralPath $bundle -Recurse -File | ForEach-Object {[ordered]@{path=(Get-BundleRelative $_.FullName);sha256=(Get-FileHash $_.FullName).Hash.ToLowerInvariant();bytes=$_.Length}})
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $bundle 'manifest.json') -Encoding utf8
$checksums=Get-ChildItem -LiteralPath $bundle -Recurse -File | ForEach-Object {(Get-FileHash $_.FullName).Hash.ToLowerInvariant()+'  '+(Get-BundleRelative $_.FullName)}
Write-MergedText (Join-Path $bundle 'SHA256SUMS') (($checksums -join "`n")+"`n")
$archive=Join-Path $output 'merged.tar.gz'
& tar -czf $archive -C $bundle .
if($LASTEXITCODE -ne 0){throw 'Merged packaging failed'}
Write-Output "Merged package: $archive"
if($PackageOnly){return}
$options=@('-o','BatchMode=yes','-o','ConnectTimeout=15','-o','StrictHostKeyChecking=yes')
if($IdentityFile){$options+=@('-i',(Resolve-Path -LiteralPath $IdentityFile).Path)}
$target="root@$VmHost";$remote="/tmp/tower-duel-merged-$release.tar.gz"
& scp @options $archive "${target}:$remote"
if($LASTEXITCODE -ne 0){throw 'Merged upload failed'}
$sha=(Get-FileHash $archive).Hash.ToLowerInvariant()
$command=@"
set -eu
echo '$sha  $remote' | sha256sum -c -
test ! -e /opt/rumblebound/tower-duel/releases/$release
mkdir -p /opt/rumblebound/tower-duel/releases/$release
tar -xzf $remote -C /opt/rumblebound/tower-duel/releases/$release
sh /opt/rumblebound/tower-duel/releases/$release/install-merged.sh $release
"@
& ssh @options $target $command
if($LASTEXITCODE -ne 0){throw "Merged activation failed; previous configuration was restored where possible. Package: $archive"}
$url="https://$Domain"
$config=Invoke-RestMethod "$url/api/config" -TimeoutSec 15
$optional=Invoke-RestMethod "$url/solo/config" -TimeoutSec 15
$page=Invoke-WebRequest "$url/" -TimeoutSec 20
if($config.rulesHash -ne $manifest.rulesHash -or $config.mapHash -ne $manifest.mapHash -or $optional.server -ne $url -or $page.Content -notmatch 'content="solo-plugin"'){throw 'Merged public health checks failed'}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'deployed.json') -Encoding utf8
Write-Output "Playable merged URL: $url/"
Write-Output "Release: $release"
