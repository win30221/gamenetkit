# 物件同步與動作確認

核心課程：先理解 [Shared World](../shared-world/README.md) 的完整清單與 [連線恢復](../network-recovery/README.md) 的 session 清理。本課學 key／life 與 action ID；完成後依需求選修 Replay、AOI、Codec 或歷史補償，或進入完整遊戲。示範沿用的預測／插值不是 replication／actions 的套件前置。完整順序見 [學習路線](../README.md)，實際接入見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

從框架根目錄雙擊 [Start.cmd](../../Start.cmd)，選 **5. 物件同步與動作確認**，接續第 4 項連線恢復教學。服務啟動後開啟 [http://127.0.0.1:18097/](http://127.0.0.1:18097/)。保留終端，按 Ctrl+C 停止。

也可從 `gamenetkit` 目錄直接啟動：

```powershell
./examples/Run.ps1 -Example ObjectsActions
```

## 先照這樣操作

1. 在「物件出現／消失」按「新增標記」。開第二個瀏覽器分頁，兩邊會看到相同標記。
2. 按「同 ID 重生」：ID 不變，標記旁的 `life` 加一。按「移除最後一個」，兩邊都會移除它。
3. 換到「動作是否成功」，先不勾選暫時效果，送出成功動作：等待伺服器確認後才顯示效果並建立標記。
4. 勾選「先顯示黃色暫時效果」，再送出：黃色立即出現，確認後轉綠。它還不是權威物件。
5. 試「模擬伺服器拒絕」與「模擬沒有回覆」：分別顯示拒絕或逾時。兩個情境都不建立標記。

每人最多 4 個標記；測試成功動作前若已滿，可回第一頁移除。按鈕等待上一個動作結束再啟用，避免同時觀察太多事件。自己的標記會在離線後移除。第二個分頁代表另一名玩家，只能操作自己建立的標記。

點畫面後可按 WASD／方向鍵移動。自己的預測＋平滑校正、其他玩家的自適應插值都有保留。進階區可以切換既有移動設定、查看物件差異、重送相同動作 ID，預設保持收起。

## 這課加入什麼

| 組件 | 這裡的用途 | 接線位置 |
| --- | --- | --- |
| [replication](../../packages/unity/replication/Runtime/ReplicaStore.cs) | 以完整清單判斷新增、更新、移除；相同 ID、不同生命會先移除舊生命再新增 | [ReplicaState.cs](client/Assets/Scripts/ReplicaState.cs) |
| [actions](../../packages/unity/actions/Runtime/ActionPresentation.cs) | 依動作 ID 追蹤等待、確認、拒絕與逾時，避免重複確認造成重複效果 | [ActionFeedback.cs](client/Assets/Scripts/ActionFeedback.cs) |
| 既有 core／transport、prediction／correction／interpolation／simulation | 連線、自己的預測校正、其他玩家平滑，以及可調回包延遲 | [ExampleSession.cs](client/Assets/Scripts/ExampleSession.cs) |

以後接投射物、場景互動物件或技能時，先由應用定義 DTO、ID 與生命編號，交給 `ReplicaStore<T>`；再由應用把差異映射到 GameObject 建立／更新／銷毀。送出技能時先以動作 ID 呼叫 `Begin`，收到伺服器決定後 `Resolve`，每幀 `Advance`，離線／換世界 `Clear`。是否先播放暫時效果由應用選擇，SDK 不會替你建立場景物件。

權威物件清單與暫時效果各有責任：收到成功回覆不會自行把物件塞進清單，仍等完整快照；拒絕或逾時也只處理該動作的暫時效果。逾時表示未取得確認，不能推論伺服器一定沒執行。這課的「沒有回覆」情境刻意不改世界，方便看清逾時流程。

## 程式從哪裡看

依序看 [WorldProtocol.cs](client/Assets/Scripts/WorldProtocol.cs) 的物件資料、[ReplicaState.cs](client/Assets/Scripts/ReplicaState.cs) 的 SDK 套用，再看 [ActionFeedback.cs](client/Assets/Scripts/ActionFeedback.cs) 與 [ExampleSession.cs](client/Assets/Scripts/ExampleSession.cs) 的送出／確認／清除。伺服器權威規則在 [world.go](server/world.go)，訊息格式集中於 [PROTOCOL](PROTOCOL.md)。完整 API 規格見 [MODULES](../../docs/MODULES.md)。

瀏覽器按鈕 → 本機 [C# 教學主機](browser-host/Program.cs) → 實際 SDK transport → Gin／coder-websocket 權威伺服器；主機把 SDK 結果送回 Canvas。JS 只處理操作與繪圖。另附的 Unity 專案重用相同純 C# 接線，見下方 Unity 版；這個本機教學主機不作為遊戲部署架構。

## 啟動與驗證

需要 Go 與 .NET 8 SDK，腳本預設使用這台 Unity 安裝附帶的 .NET SDK，不需要 Unity 授權或 NuGet 還原。可用 `scripts/Run.ps1 -DotNet <dotnet.exe>` 指定其他 .NET 8 SDK（含 ASP.NET Core）。網頁預設 18097、內部權威服務 18129，與其他課程分開。

```powershell
./examples/objects-actions/scripts/Run.ps1 -Http 127.0.0.1:18097 -BackendPort 18129
./examples/objects-actions/scripts/Test-ObjectsActions.ps1
```

測試使用隔離連接埠 18132／18133 與 `.local/objects-actions/tests`，不覆蓋正在跑的 `run` 輸出。涵蓋實際 SDK、兩條真實 WS、完整清單原子驗證、同 ID 重生、動作去重、拒絕／逾時／延後確認、離線清除，以及網頁原始碼連線與操作。網頁測試使用 DOM／Canvas 替身；不代表真正 renderer 或 Unity Player 已驗收，詳見 [VALIDATION](../../docs/VALIDATION.md)。

教學限制：動作故意等待 21 ticks（約 0.7 秒），回包預設另延遲 150 ms；客戶端 1.4 秒逾時，追蹤保留 4 秒。伺服器每人最多 8 個待執行動作、64 筆已完成結果；去重限同一次連線。重連取得新玩家 ID，沒有跨連線恢復、交易儲存或技能命中判定。

## Unity 版

另附 [Unity 專案](client) 與 [當課場景接線](client/Assets/Scripts/Unity/ExampleClient.cs)。開啟專案、Windows／WebGL 建置與操作方式統一見 [Unity 教學說明](../unity-common/README.md)。預設瀏覽器入口維持不變。
