<#
.SYNOPSIS
封裝或部署完整遊戲至指定 VM。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：釘選 Go、已建置 WebGL；實際部署另需 SSH、目標主機與網域。
行為與產物：預設會上傳並更新遠端服務；-PackageOnly 僅產生本機部署包。
.EXAMPLE
./examples/rumblebound/scripts/Deploy.ps1 -VmHost localhost -Domain game.example.com -PackageOnly
#>
#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$VmHost,
    [ValidatePattern('^[a-z_][a-z0-9_-]*$')][string]$SshUser='root',
    [ValidateRange(1,65535)][int]$SshPort=22,
    [string]$IdentityFile='',
    [Parameter(Mandatory)][ValidatePattern('^(?=.{1,253}$)[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*\.[a-zA-Z]{2,}$')][string]$Domain,
    [switch]$BuildWeb,
    [switch]$PackageOnly
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
$root=$script:GameNetKitRoot
$game=Join-Path $root 'examples/rumblebound'
if($BuildWeb){& "$PSScriptRoot/../../Build-Unity.ps1" -Project RumbleboundExample -Target Web}
$web=Join-Path $game 'client/Builds/WebGL'
foreach($file in @('index.html','rf-battle-build.txt','Build/WebGL.loader.js','Build/WebGL.framework.js','Build/WebGL.data','Build/WebGL.wasm')){
    if(-not(Test-Path -LiteralPath (Join-Path $web $file) -PathType Leaf)){throw "Missing WebGL build: $file. Run with -BuildWeb."}
}
if((Get-Content (Join-Path $web 'rf-battle-build.txt') -Raw).Trim() -ne 'Rumblebound.Example.Battle v1'){throw 'Unexpected Unity build marker'}
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$release=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$out=Join-Path $root ".local/deploy/$release"
$bundle=Join-Path $out 'bundle'
$null=New-Item -ItemType Directory -Force (Join-Path $bundle 'public/unity')
$savedOS=$env:GOOS;$savedArch=$env:GOARCH;$savedCGO=$env:CGO_ENABLED
try{
    $env:GOOS='linux';$env:GOARCH='amd64';$env:CGO_ENABLED='0'
    Push-Location (Join-Path $game 'server')
    try{
        & $Go build -buildvcs=false -trimpath -o (Join-Path $bundle 'battle-api') ./cmd/battle
        if($LASTEXITCODE -ne 0){throw 'Linux API build failed'}
    }finally{Pop-Location}
}finally{$env:GOOS=$savedOS;$env:GOARCH=$savedArch;$env:CGO_ENABLED=$savedCGO}
Copy-Item -Path (Join-Path $web '*') -Destination (Join-Path $bundle 'public/unity') -Recurse
# Match local UnityRoutes styling without changing the source Unity build.
$index=Join-Path $bundle 'public/unity/index.html'
$html=[IO.File]::ReadAllText($index).Replace('</head>','<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><link rel="stylesheet" href="/unity-view.css"><script type="module" src="/mobile-view.js"></script></head>')
[IO.File]::WriteAllText($index,$html,[Text.UTF8Encoding]::new($false))
Copy-Item (Join-Path $root 'examples/browser-common/unity-view.css') (Join-Path $bundle 'public/unity-view.css')
Copy-Item (Join-Path $root 'examples/browser-common/mobile-view.js') (Join-Path $bundle 'public/mobile-view.js')
Copy-Item (Join-Path $root 'packages/unity/transport/Browser/transport.js') (Join-Path $bundle 'public/transport.js')
Copy-Item (Join-Path $root 'examples/rumblebound/game/web/google-login.js') (Join-Path $bundle 'public/google-login.js')
$template=[IO.File]::ReadAllText((Join-Path $root 'examples/rumblebound/deploy/Caddyfile.template')).Replace('__DOMAIN__',$Domain.ToLowerInvariant())
[IO.File]::WriteAllText((Join-Path $bundle 'Caddyfile'),$template,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $bundle 'install.sh'),[IO.File]::ReadAllText((Join-Path $root 'examples/rumblebound/deploy/install.sh')).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
$manifest=[ordered]@{release=$release;domain=$Domain;origin="https://$Domain";createdAt=[DateTime]::UtcNow.ToString('o');transport='WSS';target='linux/amd64';files=@()}
$manifest.files=@(Get-ChildItem $bundle -Recurse -File | ForEach-Object { [ordered]@{path=[IO.Path]::GetRelativePath($bundle,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$_.Length} })
$manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $bundle 'manifest.json') -Encoding utf8
$archive=Join-Path $out 'game.tar.gz'
& tar -czf $archive -C $bundle .
if($LASTEXITCODE -ne 0){throw 'Packaging failed'}
Write-Host "Package: $archive"
if($PackageOnly){return}
$sshOptions=@('-o','BatchMode=yes','-o','ConnectTimeout=15','-p',[string]$SshPort)
$scpOptions=@('-o','BatchMode=yes','-o','ConnectTimeout=15','-P',[string]$SshPort)
if($IdentityFile){$key=(Resolve-Path -LiteralPath $IdentityFile).Path;$sshOptions+=@('-i',$key);$scpOptions+=@('-i',$key)}
$target="$SshUser@$VmHost"
$remote="/tmp/rumblebound-$release.tar.gz"
& scp @scpOptions $archive "${target}:$remote"
if($LASTEXITCODE -ne 0){throw 'VM upload failed'}
$sha=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
# Every interpolated remote argument is restricted to DNS/identifier/hex syntax.
$command="set -eu; echo '$sha  $remote' | sha256sum -c -; mkdir -p /opt/rumblebound/releases/$release; tar -xzf $remote -C /opt/rumblebound/releases/$release; bash /opt/rumblebound/releases/$release/install.sh $release $Domain"
if($SshUser -ne 'root'){$command="sudo -n sh -c '"+$command.Replace("'","'\''")+"'"}
& ssh @sshOptions $target $command
if($LASTEXITCODE -ne 0){throw "Remote deployment failed. Package kept at $archive"}
$url="https://$Domain"
$ready=$false
for($i=0;$i -lt 24;$i++){
    try{
        $health=Invoke-RestMethod "$url/healthz" -TimeoutSec 5
        $page=Invoke-WebRequest "$url/" -TimeoutSec 10
        if($page.StatusCode -eq 200 -and $page.Content -match 'WebGL.loader.js'){$ready=$true;break}
    }catch{if($i -eq 23){Write-Warning $_}}
    Start-Sleep -Seconds 5
}
if(-not $ready){throw "Services installed but HTTPS not ready. Check DNS/ports 80,443 and docker logs rumblebound-web. URL: $url"}
Write-Host "Deployment ready: $url/" -ForegroundColor Green
Write-Host "Release: $release; VM journal directory: /var/lib/rumblebound/replays"
