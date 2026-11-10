<#
.SYNOPSIS
驗證指定 HTTPS 站台的資源、安全標頭與遊戲流程。
.DESCRIPTION
分類：Rumblebound。所有命令從 gamenetkit 根目錄執行。
前提：需明確指定 -Origin、網路、釘選 Go 與 .NET SDK。
行為與產物：會向指定站台發出請求並建立測試遊戲；不會部署。
.EXAMPLE
./examples/rumblebound/scripts/Test-Deployment.ps1 -Origin https://game.example.com
#>
#requires -Version 7.0
param([Parameter(Mandatory)][uri]$Origin)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Toolchain.ps1"
if($Origin.Scheme -ne 'https' -or $Origin.AbsolutePath -ne '/' -or $Origin.Query -or $Origin.UserInfo){throw 'Use an HTTPS origin without paths or credentials'}
$base=$Origin.GetLeftPart([UriPartial]::Authority)
$health=Invoke-RestMethod "$base/healthz" -TimeoutSec 15
$page=Invoke-WebRequest "$base/" -TimeoutSec 20 -MaximumRedirection 0
if($page.StatusCode -ne 200 -or $page.Content -notmatch 'WebGL.loader.js'){throw 'Unity page missing'}
foreach($path in @('/Build/WebGL.loader.js','/transport.js','/google-login.js','/unity-view.css','/mobile-view.js')){
    $asset=Invoke-WebRequest "$base$path" -TimeoutSec 20
    if($asset.StatusCode -ne 200){throw "Asset unavailable: $path"}
}
$wasm=Invoke-WebRequest "$base/Build/WebGL.wasm" -Method Head -TimeoutSec 20
if($wasm.Headers['Content-Type'] -notmatch 'application/wasm'){throw 'Incorrect WASM MIME type'}
foreach($path in @('/api/session/guest','/ws')){
    $options=@{Uri="$base$path";Headers=@{Origin='https://untrusted.invalid'};SkipHttpErrorCheck=$true;TimeoutSec=15}
    if($path -like '/api/*'){$options.Method='Post';$options.Body='{}';$options.ContentType='application/json'}
    $rejected=Invoke-WebRequest @options
    if($rejected.StatusCode -ne 403){throw "Cross-origin request not rejected: $path"}
}
$guest=Invoke-WebRequest "$base/api/session/guest" -Method Post -Headers @{Origin=$base} -ContentType 'application/json' -Body '{}' -TimeoutSec 15
if($guest.Headers['Set-Cookie'] -notmatch '(?i);\s*Secure' -or $guest.Headers['Set-Cookie'] -notmatch '(?i);\s*HttpOnly'){throw 'Missing secure session cookie'}
$identity=$guest.Content | ConvertFrom-Json
$null=Invoke-WebRequest "$base/api/logout" -Method Post -Headers @{Origin=$base;Authorization="Bearer $($identity.session)"} -ContentType 'application/json' -Body '{}' -TimeoutSec 15
Write-Host 'DEPLOYMENT_HTTP PASS HTTPS, Unity assets/WASM, origin rejection and secure cookies'
& "$PSScriptRoot/Test-Battle.ps1" -Origin $base
Write-Host "DEPLOYMENT_GAME PASS $base"
