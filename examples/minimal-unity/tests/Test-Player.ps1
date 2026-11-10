<#
.SYNOPSIS
以真正 Minimal Windows Player 驗證 Go 服務連線。
.DESCRIPTION
分類：Minimal 驗證。所有命令從 gamenetkit 根目錄執行。
前提：先建置 Minimal Windows；需要釘選 Go。
行為與產物：暫啟本機 Go 服務與 Player，結果寫入 artifacts。
.EXAMPLE
./examples/minimal-unity/tests/Test-Player.ps1
#>
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../../../tools/internal/Go-Env.ps1"
$root=$script:GameNetKitRoot
New-Item -ItemType Directory -Force (Join-Path $root '.local'),(Join-Path $root 'artifacts') | Out-Null
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,18091)
try {$probe.Start()} finally {$probe.Stop()}
$server=$null;$client=$null
Push-Location (Join-Path $root 'examples/minimal-server')
try {
    $binary=Join-Path $root '.local/minimal-server.exe'
    & $Go build -buildvcs=false -o $binary .
    if($LASTEXITCODE -ne 0){throw 'Minimal server build failed'}
    $server=Start-Process -FilePath $binary -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'artifacts/minimal-server.stdout.log') -RedirectStandardError (Join-Path $root 'artifacts/minimal-server.stderr.log')
    $ready=$false
    for($i=0;$i -lt 30;$i++){try{$null=Invoke-WebRequest 'http://127.0.0.1:18091/healthz' -TimeoutSec 1;$ready=$true;break}catch{Start-Sleep -Milliseconds 100}}
    if(-not $ready){throw 'Minimal server not ready'}
    $log=Join-Path $root 'artifacts/minimal-smoke.log'
    $client=Start-Process -FilePath (Join-Path $root 'examples/minimal-unity/Builds/Windows/Minimal.exe') -WorkingDirectory $root -ArgumentList @('-batchmode','-nographics','-rf-minimal-smoke','-logFile',"`"$log`"") -WindowStyle Hidden -PassThru
    if(-not $client.WaitForExit(15000)){throw "Minimal client timed out: $log"}
    if($client.ExitCode -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch 'RF_MINIMAL PASS' -Quiet) -or (Select-String -LiteralPath $log -SimpleMatch 'Exception:' -Quiet)){throw "Minimal client failed: $log"}
    Select-String -LiteralPath $log -SimpleMatch 'RF_MINIMAL PASS' | ForEach-Object {$_.Line}
} finally {
    if($client -and -not $client.HasExited){Stop-Process -Id $client.Id}
    if($server -and -not $server.HasExited){Stop-Process -Id $server.Id}
    Pop-Location
}
