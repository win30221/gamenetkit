# 伺服器輸入錄製與離線播放

GameNetKit 的 `server/replay` 提供有界事件錄製與完整性驗證；`examples/rumblebound/game/sim` 負責遊戲規則、重建與播放快照。兩者的檔案格式與接線見 [MODULES](MODULES.md)。第一次使用可先看 [第 6 課：錄製與播放](../examples/replay/README.md)。

## 錄製完整遊戲

從 gamenetkit 根目錄啟動：

```powershell
./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Web
./examples/Run.ps1 -Example Rumblebound -ReplayDirectory artifacts/my-replays
```

開啟 [本機遊戲](http://127.0.0.1:18093/)，完成一場對戰或練習。伺服器會列出錄製位置；正常關閉 engine 後才會完成 `.replay.jsonl`。錄製中的 `.partial` 不可當成完成檔。啟動器將錄製路徑傳給目前的 `examples/rumblebound/server/cmd/battle`。

## 驗證與匯出

```powershell
./examples/rumblebound/scripts/Test-Replay.ps1
./examples/rumblebound/scripts/Export-Playback.ps1 -File artifacts/my-replays/your-match.replay.jsonl -Output artifacts/my-match.playback.json
```

Go 工具先重建完整 journal，再輸出播放檔；目的檔已存在時拒絕覆寫。直接驗證一份錄製檔：

```powershell
. ./tools/internal/Go-Env.ps1
$recordingFile = (Resolve-Path ./artifacts/my-replays/your-match.replay.jsonl).Path
Push-Location ./examples/rumblebound/game
try {
    & $Go run -buildvcs=false ./cmd/replay -file $recordingFile
    if ($LASTEXITCODE -ne 0) { throw 'Replay verification failed' }
} finally { Pop-Location }
```

成功輸出 JSON 驗證報告；失敗回傳非零 exit code。模擬不一致時會列出第一個不同事件及狀態摘要。這些摘要用於一致性檢查，並非數位簽章。

## Unity／網頁播放

在 [Rumblebound example](../examples/rumblebound/README.md) 的「錄製與離線播放」區域選取本機 JSON；Unity 也支援路徑／網址。WebGL 跨來源網址需要檔案伺服器允許 CORS。

接收端匯出的 `rumblebound-view-v1` 包含 AOI／量化後快照；Go journal 驗證匯出的 `rumblebound-sim-v1` 保存完整權威快照。兩者都透過 SDK `PlaybackClock` 提供播放、暫停、速度與跳轉。播放期間不連線或送出遊戲輸入。

Unity 的 `BattleReplay.cs` 負責遊戲檔案與呈現，跳轉最多重建五份相鄰快照；通用 `com.realtime.playback` 只依賴 core 契約。驗證入口為 `tests/scripts/Test-UnitySdk.ps1`、`examples/rumblebound/scripts/Test-BattlePlayer.ps1`，以及 `examples/rumblebound/game/sim/playback_export_test.go`。

## 記錄與比對範圍

| 資料 | 用途 |
|---|---|
| 版本、模擬模型 ID、完整規則、地圖、隊伍、英雄及練習模式 | 建立相同的初始世界；規則不同時明確拒絕 |
| attach／takeover、loaded、disconnect | 重現身份世代、載入門檻及斷線生命週期 |
| 實際由 owner 處理的輸入，含重複或被拒絕的輸入 | 保留順序、序號、動作與時間要求；未進入有界命令佇列的輸入沒有模擬效果，因此不記錄 |
| 網路模組設定、練習模式變更 | 重現歷史開關、近戰回溯及 AI 切換 |
| 每次操作採樣的 UTC 奈秒時間 | 所有該操作內的時鐘讀取使用同一值，重現重連逾時與 tombstone 到期 |
| 每個 step 與事件之後的完整模擬狀態 hash | 找出第一次差異，包含未公開到 Snapshot 的內部狀態 |

比對狀態包含玩家位置、血量、生命／連線世代、輸入與動作確認、待處理攻擊、冷卻、衝刺、出生點、彈體速度／傷害／初始碰撞狀態、範圍技能、保留戰鬥事件、比分、結果、AI 路徑／決策狀態，以及有界命中歷史。欄位清單在 `examples/rumblebound/game/sim/replay_state.go`；新增會影響模擬的欄位時必須同步維護。

目前遊戲模擬沒有使用 PRNG，header 明確記錄 `randomSource: none`、`seed: 0`；檔名與房間的隨機 ID 不作為模擬亂數。未來新增隨機戰鬥時，必須加入確定性 PRNG、初始種子與狀態，再更新模型版本。

不保存認證 token、ticket、socket、實際傳輸佇列或每位玩家的 AOI 暫存。檔案仍含內部玩家 ID 與遊戲輸入，保存與分享由使用者管理。讀檔只重建離線模擬，不會把檔案套回線上權威狀態。

## 模組與加入／移除

- `server/replay`：獨立 JSONL journal、非同步有界寫入、完整性驗證；不認識遊戲 DTO。
- `server/netcore/replay.go`：資料契約與 ReplaySink。
- `examples/rumblebound/game/sim/replay*.go`：遊戲定義、操作重建及狀態指紋。
- 唯一具體 SDK 引用在 `net_module_replay.go`，由 `!rf_no_replay` 控制。

```powershell
./tests/scripts/Test-GoModuleRemoval.ps1 -Module replay
# 或將目前五個選用 Go 套件全部實體移走再驗證
./tests/scripts/Test-GoModuleRemoval.ps1 -Module all
```

Rumblebound 應用用 `sim.WithReplayDirectory(ctx, path)` 建立錄製 context，傳給 engine constructor。另一款遊戲需自行提供事件套用與狀態指紋，透過 `netcore.ReplaySink` 接上錄製模組。啟用失敗透過 Engine.ReplayStatus 與日誌顯示；未安裝模組卻要求錄製會明確回報。關閉時不計算狀態 hash、不建立檔案；核心只有必要的 hook 與介面。

## 儲存邊界與本版限制

客戶端快照匯出的來源摘要以固定 1024 字元／約 3 KiB byte 緩衝逐段更新 SHA-256；UTF-8 encoder 跨幀保留 surrogate 狀態，摘要與原本整份串接的定義相同。這移除了整份 concat 與 UTF-8 暫存，並未將匯出改成串流檔案：最終 JSON string、下載橋接與 Blob 仍可能占用額外記憶體，32 MiB 檔案上限也不代表工作記憶體上限。

預設最多 1,024 筆待寫記錄、每筆 64 KiB、每檔 256 MiB、1,000,000 個事件。owner 只做資料擷取／hash／非阻塞 enqueue，磁碟寫入在 worker；資料擷取與 hash 仍會增加 CPU 與配置成本。本版不宣稱啟用錄製後效能不變。

正常 Close 會完成 footer、flush、fsync、關檔，再用同目錄 hard link 原子發布且不覆蓋既有檔案，最後移除 partial。檔案系統不支援 hard link 時明確失敗。寫入後不自動清除舊錄製，沒有資料庫索引或儲存配額管理服務。

伺服器驗證仍從初始狀態順序重播，沒有中途模擬 checkpoint 還原。Unity 播放已提供進度條跳轉，因為播放檔存的是完整權威快照；它不會在 Unity 重跑 Go 戰鬥規則。播放檔最多 32 MiB、120,000 份快照、tick 範圍至多一小時，JSON 在載入時解析，沒有分段串流。沒有觀戰服務、擊殺攝影機、客戶端到達時間重播或模組 A/B 圖表。倍速控制視覺時鐘，音效依事件播放，不做音訊時間伸縮。

相同程式與內容版本下的既有情境已驗證，不代表跨 CPU／OS 的浮點位元一致性或任何未來版本都能重播。播放檔的來源 digest 是原始 journal 的識別資料，不是客戶端重新驗證過的數位簽章。
