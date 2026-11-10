# 錄製與播放 Replay

依需求選修：先理解 [Shared World](../shared-world/README.md) 的權威 tick／快照，以及錄製資料與即時世界的分離。本課學完成錄製、驗證與離線播放，不需先修 AOI、Codec 或歷史補償；要擴充物件變化時再參考 [物件同步](../objects-actions/README.md)。選課見 [學習路線](../README.md)，錄製端與播放端如何分別加入見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

雙擊框架根目錄的 [Start.cmd](../../Start.cmd)，選 **6. 錄製與播放 Replay**，服務啟動後開啟 [http://127.0.0.1:18100/](http://127.0.0.1:18100/)。保留終端，按 Ctrl+C 停止。

也可從 `gamenetkit` 目錄執行：

```powershell
./examples/Run.ps1 -Example Replay
```

## 先照這樣操作

1. 按「開始錄製」。
2. 點畫面，使用 WASD／方向鍵移動；試「新增標記」「同 ID 重生」「移除最後一個」。每次物件動作約 0.7 秒後生效，等畫面變化後再繼續。
3. 按「結束錄製」，等到「已完成，可播放」。
4. 按「播放紀錄」。播放鍵可切換暫停／繼續；拖曳進度會暫停，方便觀察標記的生命與出現／消失；速度可選 0.5／1／2 倍。
5. 按「返回即時世界」，繼續操作自己的角色。

可再開第二個分頁。錄製會包含同一世界的所有玩家與標記；兩個分頁可以各自錄製，控制與檔案互相獨立。每人最多 4 個標記，自己的標記在離線後移除。

即時模式保留自己的預測＋平滑校正、其他玩家的自適應插值。回放使用伺服器記錄的位置，透過獨立播放時鐘及相鄰快照取樣讓移動連續。回放期間即時角色停下，移動、物件及錄製操作會停用；伺服器的世界仍持續更新。返回即時模式時會看到最新狀態。

## 這課加入什麼

| 組件 | 做的事 | 接線位置 |
| --- | --- | --- |
| [Go replay](../../server/replay/journal.go) | 有界 journal、非阻塞 Append、完成 footer 與檔案完整性驗證 | [recording.go](server/recording.go) |
| [C# playback](../../packages/unity/playback/Runtime/PlaybackClock.cs) | 播放時間、暫停、倍速、跳轉與 frame index | [PlaybackWorld.cs](client/Assets/Scripts/PlaybackWorld.cs) |
| 既有 replication | 以完整快照重建該時刻的玩家、標記及生命 | [ReplicaState.cs](client/Assets/Scripts/ReplicaState.cs) |

接入自己的程式時，伺服器先定義要錄的資料與狀態 hash，再建立 journal、在模擬 owner 擷取後 Append，結束時於背景工作完成 Close。整份檔案驗證成功後才提供播放資料。客戶端先驗證並複製完整播放檔，再 `PlaybackClock.Load → Play/Pause/Seek/Advance`，依 `FrameIndex` 選資料，由自己的呈現層建立物件。

本課每 15 Hz 保存一份完整權威快照，包括錄製起點的初始快照。journal 的事件 payload 是這些快照，應用驗證每份 state hash、tick 與物件規則，再輸出獨立播放 JSON。完整遊戲的輸入事件錄製與模擬重播可參考 [REPLAY](../../docs/REPLAY.md)。SDK 負責記錄與完整性；資料格式和重建方式由應用決定。

## 從哪些檔案看

1. [recording.go](server/recording.go)：模擬 owner 擷取、工作佇列、SDK journal 與完成驗證。檔案 I/O 及驗證在背景執行，容量不足會回報失敗。
2. [RecordingStatus.cs](client/Assets/Scripts/RecordingStatus.cs)、[ExampleSession.cs](client/Assets/Scripts/ExampleSession.cs)：同一條 SDK 連線上的錄製控制與狀態。
3. [PlaybackWorld.cs](client/Assets/Scripts/PlaybackWorld.cs)：完整驗證後才替換播放檔；獨立 clock／replica；往回跳轉時重建目標清單。
4. [Program.cs](browser-host/Program.cs)：將即時操作與回放控制分開；瀏覽器只接收呈現結果。

訊息與檔案格式集中在 [PROTOCOL](PROTOCOL.md)。純 C# 應用與 Unity 呈現共用同一份程式，本課另附 Unity 專案與建置入口。

## 檔案與限制

- 每次最多錄製 30 秒模擬時間（451 份快照），同一服務程序至多接受 16 次錄製；重新啟動服務後可繼續錄製。
- journal 位於 `.local/replay/run/recordings/<隨機ID>.jsonl`，同名檔案不會覆蓋；寫入失敗或強制停止可能留下 `.partial`，這種檔案不會提供播放。
- 展開進階區，可下載已驗證的播放 JSON。此頁目前提供本次連線的最新錄製；重新整理會建立新教學 session。下載檔可保留或交給自己的應用，本頁尚無舊檔上傳／選取器。
- 已載入的回放可在即時連線中斷後繼續使用。播放 tick、跳轉及物件清單不會寫回即時 world；下載與播放載入只接受伺服器產生的錄製 ID。
- 畫面錄製包含完整權威快照與生命變化；按鍵、暫時效果、音效與到達延遲未納入這份播放格式。

## 啟動與驗證

需要 Go、Node（測試用）與含 ASP.NET Core 的 .NET 8 SDK；腳本預設使用本機 Unity 安裝附帶的 .NET SDK，可用 `-DotNet` 指定。啟動與編譯不需要 Unity 授權或 NuGet 還原。網頁預設 18100、內部服務 18139。

```powershell
./examples/replay/scripts/Run.ps1 -Http 127.0.0.1:18100 -BackendPort 18139
./examples/replay/scripts/Test-ReplayLesson.ps1
```

測試使用隔離埠 18142／18143 與 `.local/replay/tests`，不覆蓋正在執行的 `run` 輸出。涵蓋實際 Go journal、C# PlaybackClock、雙人 WS、物件生命錄製、拒絕未完成／損壞檔、原子載入、暫停／跳轉／倍速與即時狀態隔離。正式網頁來源測試以 DOM／Canvas 替身執行；另外已完成雙 Windows Player 的連線／移動，以及實際 WebGL 的錄製、離線播放、暫停／跳轉／倍速操作。證據與平台界線見 [VALIDATION](../../docs/VALIDATION.md)。

## Unity 版

另附 [Unity 專案](client) 與 [當課場景接線](client/Assets/Scripts/Unity/ExampleClient.cs)。開啟專案、Windows／WebGL 建置與操作方式統一見 [Unity 教學說明](../unity-common/README.md)。預設瀏覽器入口維持不變。
