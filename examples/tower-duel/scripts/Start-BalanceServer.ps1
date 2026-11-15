<# Build and start a separate local backend for the Unity balance window. #>
param([ValidateRange(1024,65535)][int]$Port=18110)
$ErrorActionPreference='Stop'
$example=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repo=[IO.Path]::GetFullPath((Join-Path $example '../..'))
$output=Join-Path $example "Builds/BalanceServer/$Port"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$balanceResultPath=Join-Path $output 'launch-result.json'
trap {
    [IO.File]::WriteAllText($balanceResultPath,(@{error=$_.Exception.Message} | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
    exit 1
}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
try {$listener.Start()} catch {throw "本機連接埠 $Port 已被其他程式使用；請先停止原本的後端。"} finally {$listener.Stop()}
$env:GAMENETKIT_CACHE=Join-Path $repo '.cache'
. (Join-Path $repo 'tools/internal/Go-Env.ps1')
$binary=Join-Path $output 'tower-duel-balance-server.exe'
Push-Location (Join-Path $example 'server')
try {
    & $Go build -o $binary . *> (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw "後端建置失敗，請查看 $output/build.log"}
} finally {Pop-Location}
function Quote-BalanceArgument([string]$value){'"'+$value.Replace('"','\"')+'"'}
$server=Start-Process -FilePath $binary -WindowStyle Hidden -PassThru -ArgumentList @('-http',"127.0.0.1:$Port",
    '-map',(Quote-BalanceArgument (Join-Path $example 'protocol/map.json')),
    '-rules',(Quote-BalanceArgument (Join-Path $example 'protocol/rules.json')),
    '-transport-runtime',(Quote-BalanceArgument (Join-Path $repo 'packages/unity/transport/Browser/transport.js'))) `
    -RedirectStandardOutput (Join-Path $output 'server.log') -RedirectStandardError (Join-Path $output 'server-error.log')
try {
    $ready=$false
    for($attempt=0;$attempt -lt 50;$attempt++){
        if($server.HasExited){throw "本機後端啟動失敗，請查看 $output/server-error.log"}
        try {
            if((Invoke-WebRequest "http://127.0.0.1:$Port/healthz" -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200){$ready=$true;break}
        } catch {}
        Start-Sleep -Milliseconds 100
    }
    if(-not $ready){throw '本機後端未在預期時間內啟動。'}
    $loadedConfig=Invoke-RestMethod "http://127.0.0.1:$Port/api/config" -TimeoutSec 3
    $balanceResult=[ordered]@{pid=$server.Id;startTicks=$server.StartTime.ToUniversalTime().Ticks.ToString();executable=$binary;
        port=$Port;rulesHash=$loadedConfig.rulesHash} | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($balanceResultPath,$balanceResult,[Text.UTF8Encoding]::new($false))
} catch {
    if(-not $server.HasExited){$server.Kill();$server.WaitForExit(3000) | Out-Null}
    throw
} finally {$server.Dispose()}
