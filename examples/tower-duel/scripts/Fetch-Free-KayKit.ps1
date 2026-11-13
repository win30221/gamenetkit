param([Parameter(Mandatory)][ValidateSet('kaykit-adventurers','kaykit-character-animations')][string]$Slug,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$origin='https://kaylousberg.itch.io';$purchaseUrl="$origin/$Slug/purchase"
$page=Invoke-WebRequest $purchaseUrl -SessionVariable assetSession
function Token([string]$Html){$m=[regex]::Match($Html,'name="csrf_token" value="([^"]+)"');if(-not $m.Success){throw 'Anonymous CSRF token unavailable'};[Net.WebUtility]::HtmlDecode($m.Groups[1].Value)}
$csrf=Token $page.Content
$headers=@{Referer=$purchaseUrl;'X-Requested-With'='XMLHttpRequest'}
$free=Invoke-RestMethod "$origin/$Slug/download_url" -Method Post -WebSession $assetSession -Body @{csrf_token=$csrf} -Headers $headers
if(-not $free.url){throw 'Public free download URL unavailable'}
$download=Invoke-WebRequest $free.url -WebSession $assetSession
$csrf=Token $download.Content
$uploads=[regex]::Matches($download.Content,'(?s)<div class="upload">.*?data-upload_id="(\d+)".*?<strong[^>]*class="name"[^>]*>(.*?)</strong>')
if($uploads.Count -ne 1){throw "Expected one accessible free upload, got $($uploads.Count)"}
$id=$uploads[0].Groups[1].Value;$name=[Net.WebUtility]::HtmlDecode($uploads[0].Groups[2].Value)
$file=Invoke-RestMethod "$origin/$Slug/file/${id}?source=game_download" -Method Post -WebSession $assetSession -Body @{csrf_token=$csrf} -Headers @{Referer=$free.url;'X-Requested-With'='XMLHttpRequest'}
if(-not $file.url){throw 'Download did not return a direct file URL; no bypass will be attempted'}
$Output=[IO.Path]::GetFullPath($Output);New-Item -ItemType Directory -Path (Split-Path -Parent $Output) -Force | Out-Null
Invoke-WebRequest $file.url -OutFile $Output
$expected=if($Slug -eq 'kaykit-adventurers'){'ABE48F4763FBA0896BAB486EE9E6D08CA6B5B3884B9601F235C8847AE94DC479'}else{'65882F31F905AD2E953819648A59287CDEAB8F623908D5EF701971D3758BE20F'}
if((Get-FileHash -LiteralPath $Output -Algorithm SHA256).Hash -ne $expected){throw 'Official archive changed. Review the new version and update the provenance manifest before importing.'}
$record=[ordered]@{source="$origin/$Slug";downloadName=$name;uploadId=$id;downloadedUtc=[DateTime]::UtcNow.ToString('o');sha256=(Get-FileHash -LiteralPath $Output -Algorithm SHA256).Hash;bytes=(Get-Item -LiteralPath $Output).Length}
$record | ConvertTo-Json | Set-Content -LiteralPath ($Output+'.json') -Encoding utf8
$record | ConvertTo-Json
