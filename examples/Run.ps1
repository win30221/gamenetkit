<#
.SYNOPSIS
顯示範例選單，或啟動指定的教學／遊戲。
.DESCRIPTION
分類：範例操作。所有命令從 gamenetkit 根目錄執行。
前提：PowerShell 7、Go；部分範例需要 .NET SDK，Unity 模式需先建置 Player。
行為與產物：啟動本機服務；Ctrl+C 停止服務。
.EXAMPLE
./examples/Run.ps1 -List
#>
param(
    [ValidateSet('Minimal','SharedWorld','Rumblebound','Interpolation','Prediction','NetworkRecovery','ObjectsActions','Replay','Interest','Codec','History','ClientLessons','ServerLessons')][string]$Example,
    [string]$Http,
    [switch]$Unity,
    [switch]$WebTransport,
    [string]$ReplayDirectory='',
    [switch]$List
)

$ErrorActionPreference = 'Stop'
$catalog=@(
    [pscustomobject]@{Name='SharedWorld';Title='多人方塊';Description='先操作兩人互見，再回頭看多人 owner 接線';Guide='examples/shared-world/README.md';URL='http://127.0.0.1:18092/'}
    [pscustomobject]@{Name='Interpolation';Title='插值與平滑';Description='比較關閉、固定與自適應插值';Guide='examples/interpolation/README.md';URL='http://127.0.0.1:18094/'}
    [pscustomobject]@{Name='Prediction';Title='預測與校正';Description='自己的即時移動、校正與遠端平滑';Guide='examples/prediction/README.md';URL='http://127.0.0.1:18095/'}
    [pscustomobject]@{Name='NetworkRecovery';Title='斷線後自動回來';Description='意外斷線重試、手動停止；弱網、診斷與時鐘';Guide='examples/network-recovery/README.md';URL='http://127.0.0.1:18096/'}
    [pscustomobject]@{Name='ObjectsActions';Title='物件同步與動作確認';Description='新增、移除、同 ID 重生，以及確認／拒絕／逾時';Guide='examples/objects-actions/README.md';URL='http://127.0.0.1:18097/'}
    [pscustomobject]@{Name='Replay';Title='錄製與播放 Replay';Description='需要錄製／離線播放時選；暫停、跳轉與倍速';Guide='examples/replay/README.md';URL='http://127.0.0.1:18100/'}
    [pscustomobject]@{Name='Interest';Title='AOI 興趣範圍同步';Description='需要減少同步對象時選；每人接收範圍不同';Guide='examples/interest/README.md';URL='http://127.0.0.1:18101/'}
    [pscustomobject]@{Name='Codec';Title='Codec 與座標量化';Description='需要調整流量／精度時選；差分與基準恢復';Guide='examples/codec/README.md';URL='http://127.0.0.1:18104/'}
    [pscustomobject]@{Name='History';Title='歷史位置與延遲補償';Description='需要回溯判定時選；合法時間窗與命中規則';Guide='examples/history-lagcomp/README.md';URL='http://127.0.0.1:18105/'}
    [pscustomobject]@{Name='Rumblebound';Title='Rumblebound 完整遊戲';Description='組件整合、房間、選角、對戰與練習 AI；需 WebGL 建置';Guide='examples/rumblebound/README.md';URL='http://127.0.0.1:18093/'}
    [pscustomobject]@{Name='Minimal';Title='最小 Unity 接線（基礎參考）';Description='可先讀 source 看最短資料流；執行畫面需 Unity 建置';Guide='docs/GETTING_STARTED.md';URL='http://127.0.0.1:18091/unity/'}
    [pscustomobject]@{Name='ClientLessons';Title='C# 組件 API 參考';Description='八個終端主題，學到對應組件時穿插執行';Guide='examples/modules/client/README.md';URL=''}
    [pscustomobject]@{Name='ServerLessons';Title='Go 組件 API 參考';Description='四個終端主題，學到對應組件時穿插執行';Guide='examples/modules/server/README.md';URL=''}
)
$catalog=@($catalog | ForEach-Object -Begin {$entryNumber=0} -Process {$entryNumber++;$_ | Add-Member -NotePropertyName Number -NotePropertyValue $entryNumber -PassThru})
if($List){$catalog | Select-Object Number,Name,Title,Description,Guide,URL;return}
if(-not $Example){
    Write-Host ''
    Write-Host 'GameNetKit 範例選單' -ForegroundColor Cyan
    Write-Host '學習路線：examples/README.md；組件加入步驟：docs/COMPONENT_GUIDE.md'
    Write-Host '第一次：操作 1 → 讀 11 的最短接線 → 回 1 看多人 owner → 2～5。'
    Write-Host '6～9 按需求選修；編號用來啟動，不必全部依序完成。'
    foreach($item in $catalog){
        if($item.Name -eq 'SharedWorld'){Write-Host '';Write-Host '基礎操作與多人世界' -ForegroundColor Cyan}
        if($item.Name -eq 'Interpolation'){Write-Host '';Write-Host '核心組件：建議依序理解 2～5' -ForegroundColor Cyan}
        if($item.Name -eq 'Replay'){Write-Host '';Write-Host '需求選修：各自選擇，不必依編號全部學完' -ForegroundColor Cyan}
        if($item.Name -eq 'Rumblebound'){Write-Host '';Write-Host '整合示範' -ForegroundColor Cyan}
        if($item.Name -eq 'Minimal'){Write-Host '';Write-Host '程式接線與 API 參考：可穿插閱讀' -ForegroundColor Cyan}
        Write-Host ("  {0}. {1} — {2}" -f $item.Number,$item.Title,$item.Description)
        Write-Host ("     文件：{0}" -f $item.Guide)
    }
    Write-Host '  0. 離開'
    while($true){
        $answer=Read-Host '請輸入編號（第一次建議選 1）'
        if([string]::IsNullOrWhiteSpace($answer) -or $answer -eq '0' -or $answer -eq 'q'){return}
        $answer=$answer.Trim()
        $selection=$catalog | Where-Object { [string]$_.Number -eq $answer -or $_.Name -eq $answer } | Select-Object -First 1
        if($selection){$Example=$selection.Name;break}
        Write-Host "請輸入 1～$($catalog.Count) 的編號，或輸入 0 離開。" -ForegroundColor Yellow
    }
}
if (($WebTransport -or $ReplayDirectory) -and $Example -ne 'Rumblebound') {
    throw '-WebTransport and -ReplayDirectory apply only to -Example Rumblebound.'
}
$chosen=$catalog | Where-Object Name -eq $Example | Select-Object -First 1
Write-Host ("啟動：{0}" -f $chosen.Title) -ForegroundColor Cyan
Write-Host ("教學文件：{0}" -f $chosen.Guide)
if($chosen.URL){
    $url=$chosen.URL
    if($Http){$url=$url.Replace(([uri]$url).Authority,$Http)}
    Write-Host ("服務啟動後，用瀏覽器開啟：{0}" -f $url)
    Write-Host '保持終端開著；Ctrl+C 停止服務。要換範例，再執行 examples/Run.ps1。'
}
if($Example -eq 'ClientLessons' -or $Example -eq 'ServerLessons'){
    if($Http -or $Unity){throw 'Console lessons do not use HTTP or Unity launch options.'}
    $side=if($Example -eq 'ClientLessons'){'client'}else{'server'}
    & "$PSScriptRoot/modules/$side/Run.ps1"
    return
}
if($Example -eq 'NetworkRecovery') {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/network-recovery/scripts/Run.ps1" -Unity:$Unity @options
    return
}
if($Example -eq 'ObjectsActions') {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/objects-actions/scripts/Run.ps1" -Unity:$Unity @options
    return
}
if($Example -eq 'Replay') {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/replay/scripts/Run.ps1" -Unity:$Unity @options
    return
}
if($Example -eq 'Interest') {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/interest/scripts/Run.ps1" -Unity:$Unity @options
    return
}
if($Example -eq 'Codec') {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/codec/scripts/Run.ps1" -Unity:$Unity @options
    return
}
if($Example -eq 'History') {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/history-lagcomp/scripts/Run.ps1" -Unity:$Unity @options
    return
}
if($Example -eq 'Rumblebound') {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/rumblebound/scripts/Run.ps1" -Unity:$Unity -WebTransport:$WebTransport -ReplayDirectory $ReplayDirectory @options
    return
}
if($Example -eq 'Prediction') {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/prediction/scripts/Run.ps1" -Unity:$Unity @options
    return
}
if($Example -eq 'Interpolation' -and -not $Unity) {
    $options=@{}
    if($Http){$options.Http=$Http}
    & "$PSScriptRoot/interpolation/scripts/Run.ps1" @options
    return
}
. "$PSScriptRoot/../tools/internal/Go-Env.ps1"
$examplePaths = @{Minimal='examples/minimal-server'; SharedWorld='examples/shared-world/server'; Rumblebound='examples/rumblebound/server'; Interpolation='examples/shared-world/server'}
if ($Example -eq 'Minimal' -and $Http) { throw 'Minimal uses fixed port 18091; -Http is supported by SharedWorld and Rumblebound.' }
if (-not (Test-Path -LiteralPath $Go -PathType Leaf)) {
    throw 'Go executable not found. Install Go and add it to PATH, then reopen the terminal.'
}
$runArguments = @('run', '-buildvcs=false', '.')
if ($Http) { $runArguments += @('-http', $Http) }
if ($Example -eq 'Interpolation') {
    if (-not $Http) {$runArguments += @('-http', '127.0.0.1:18094')}
    $lessonRoot=Join-Path (Split-Path -Parent $PSScriptRoot) 'examples/interpolation'
    $runArguments += @('-web-dir', (Join-Path $lessonRoot 'client/Builds/WebGL'), '-unity-dir', (Join-Path $lessonRoot 'client/Builds/WebGL'))
}
Push-Location (Join-Path (Split-Path -Parent $PSScriptRoot) $examplePaths[$Example])
try {
    & $Go @runArguments
    if ($LASTEXITCODE -ne 0) { throw "$Example server exited with code $LASTEXITCODE" }
} finally { Pop-Location }
