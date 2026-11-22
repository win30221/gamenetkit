<# Deploy only the standalone solo test site, its companion and a separate original game process. #>
[CmdletBinding()]
param(
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$VmHost='172.233.67.181',
    [ValidatePattern('^(?=.{1,253}$)[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*\.[a-zA-Z]{2,}$')][string]$Domain='tower-duel-solo.172.233.67.181.sslip.io',
    [string]$IdentityFile='',
    [switch]$PackageOnly
)
$ErrorActionPreference='Stop'
if($Domain -eq 'tower-duel.172.233.67.181.sslip.io'){throw 'The solo test site must use its own domain'}
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
$release=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$output=Join-Path $repo ".local/deploy/tower-duel-solo/$release"
$bundle=Join-Path $output 'bundle'
. (Join-Path $example 'scripts/WebPackageContent.ps1')
$packageProtocol = New-TowerDuelWebPackageContent $example $bundle @{Solo='public'}
$marker = $packageProtocol.Markers.Solo
$env:GAMENETKIT_CACHE=Join-Path $repo '.cache'
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
$savedOS=$env:GOOS;$savedArch=$env:GOARCH;$savedCGO=$env:CGO_ENABLED
try {
    $env:GOOS='linux';$env:GOARCH='amd64';$env:CGO_ENABLED='0'
    foreach($build in @(@{Path=(Join-Path $example 'server');Name='tower-duel-api'},@{Path=(Join-Path $PSScriptRoot 'backend');Name='tower-duel-solo'})){
        Push-Location $build.Path
        try{& $Go build -buildvcs=false -trimpath -o (Join-Path $bundle $build.Name) .;if($LASTEXITCODE -ne 0){throw "Linux build failed: $($build.Name)"}}finally{Pop-Location}
    }
} finally {$env:GOOS=$savedOS;$env:GOARCH=$savedArch;$env:CGO_ENABLED=$savedCGO}
Copy-Item -LiteralPath (Join-Path $repo 'packages/unity/transport/Browser/transport.js') -Destination (Join-Path $bundle 'public/transport.js')
foreach($file in @('site.caddy.template','tower-duel-solo-api.service','tower-duel-solo-web.service','install.sh','uninstall.sh','test-uninstall.sh')){
    $destination=if($file -eq 'site.caddy.template'){'site.caddy'}else{$file}
    $content=[IO.File]::ReadAllText((Join-Path $PSScriptRoot "deploy/$file")).Replace('__DOMAIN__',$Domain.ToLowerInvariant()).Replace("`r`n","`n")
    [IO.File]::WriteAllText((Join-Path $bundle $destination),$content,[Text.UTF8Encoding]::new($false))
}
$manifest=[ordered]@{release=$release;origin="https://$Domain";plugin='tower-duel-solo';protocol=$packageProtocol.Rules.schema;build=$marker;
    mapHash=$packageProtocol.mapHash;rulesHash=$packageProtocol.rulesHash;
    services=@('tower-duel-solo-api.service','tower-duel-solo-web.service');ports=@(18111,18112);files=@()}
function Get-BundleRelative([string]$Full){$b=$bundle.TrimEnd('\','/');if($Full.StartsWith($b,[StringComparison]::OrdinalIgnoreCase)){return $Full.Substring($b.Length).TrimStart('\','/').Replace('\','/')};return $Full.Replace('\','/')}
$manifest.files=@(Get-ChildItem -LiteralPath $bundle -Recurse -File | ForEach-Object {
    [ordered]@{path=(Get-BundleRelative $_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$_.Length}
})
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $bundle 'manifest.json') -Encoding utf8
$checksums=Get-ChildItem -LiteralPath $bundle -Recurse -File | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Get-BundleRelative $_.FullName)
}
[IO.File]::WriteAllText((Join-Path $bundle 'SHA256SUMS'),($checksums -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
$archive=Join-Path $output 'solo.tar.gz'
& tar -czf $archive -C $bundle .
if($LASTEXITCODE -ne 0){throw 'Solo packaging failed'}
Write-Output "Solo package: $archive"
if($PackageOnly){return}
$options=@('-o','BatchMode=yes','-o','ConnectTimeout=15','-o','StrictHostKeyChecking=yes')
if($IdentityFile){$options+=@('-i',(Resolve-Path -LiteralPath $IdentityFile).Path)}
$target="root@$VmHost";$remote="/tmp/tower-duel-solo-$release.tar.gz"
& scp @options $archive "${target}:$remote"
if($LASTEXITCODE -ne 0){throw 'Solo upload failed'}
$sha=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$command=@"
set -eu
echo '$sha  $remote' | sha256sum -c -
test ! -e /opt/rumblebound/tower-duel-solo/releases/$release
mkdir -p /opt/rumblebound/tower-duel-solo/releases/$release
tar -xzf $remote -C /opt/rumblebound/tower-duel-solo/releases/$release
sh /opt/rumblebound/tower-duel-solo/releases/$release/install.sh $release
"@
& ssh @options $target $command
if($LASTEXITCODE -ne 0){throw "Solo deployment failed; package retained at $archive"}
$url="https://$Domain";$ready=$false
for($i=0;$i -lt 24;$i++){
    try {
        $health=Invoke-RestMethod "$url/healthz" -TimeoutSec 5
        $page=Invoke-WebRequest "$url/" -TimeoutSec 10
        $config=Invoke-RestMethod "$url/api/config" -TimeoutSec 5
        if($health.ok -and $health.plugin -eq 'tower-duel-solo' -and $health.build -eq $marker -and $page.Content -match 'content="solo-plugin"' -and $config.mapHash -eq $manifest.mapHash -and $config.rulesHash -eq $manifest.rulesHash){$ready=$true;break}
    }catch{if($i -eq 23){Write-Warning $_}}
    Start-Sleep -Seconds 3
}
if(-not $ready){throw "Solo HTTPS not ready. Inspect the solo services and rumblebound-web proxy. URL: $url"}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'deployed.json') -Encoding utf8
Write-Output "Solo test URL: $url/"
Write-Output "Solo release: $release"
