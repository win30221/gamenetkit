# 歷史位置與延遲補償

依需求選修：先理解 [Shared World](../shared-world/README.md) 的權威 tick、狀態所有權與 pose 副本；回溯命中另需合法時間窗、生命識別及應用幾何。本課不需先修 Replay、AOI 或 Codec。選課見 [學習路線](../README.md)，history 保存與 lagcomp 授權如何分別加入見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

雙擊框架 [Start.cmd](../../Start.cmd)，選 **9. 歷史位置與延遲補償**，啟動後開啟 [http://127.0.0.1:18105/](http://127.0.0.1:18105/)。保留終端，Ctrl+C 停止。

```powershell
./examples/Run.ps1 -Example History
```

## 先照這樣操作

1. 第一頁「查看歷史位置」：綠色目標持續移動，淺藍輪廓是伺服器查出的歷史位置。拖曳回溯秒數，觀察兩者差別。
2. 第二頁「比較命中補償」：先用「以目前位置判定」，按「對準畫面目標射擊」。
3. 改用「回溯看到的位置判定」再射擊，比較結果。預設回包延遲 150 ms 加上平滑，畫面位置會落後於伺服器；目標折返時兩種判定也可能同時命中。
4. 展開進階區，送出超過時間窗的射擊，應看到伺服器拒絕；重設目標會建立新生命、恢復血量並清除舊歷史。
5. 開第二個分頁，觀察命中扣血與新生命由同一份權威世界決定，歷史查詢不會恢復過去血量。

目標每被命中 5 次會重新建立生命。射擊成功提交與是否命中是不同結果，命中與扣血由伺服器決定。WASD／方向鍵可移動自己；原本的預測、校正與遠端平滑仍在進階區。

## 加入的組件

| SDK | 這課做的事 | 接線 |
| --- | --- | --- |
| [Go history.Buffer](../../server/history/history.go) | 保存目標的有界位置／生命資料、查詢夾住指定 tick 的紀錄；Bounds 提供實際保留範圍 | [target.go](server/target.go) |
| [Go lagcomp.Bounded](../../server/lagcomp/policy.go) | 限制可查詢的命中時間，拒絕未來或過舊要求 | [target.go](server/target.go) |
| 既有 C# replication／actions／interpolation | 目標生命重建、射擊動作確認、取樣呈現位置與 tick | [ReplicaState.cs](client/Assets/Scripts/ReplicaState.cs)、[TargetTrack.cs](client/Assets/Scripts/TargetTrack.cs)、[ExampleSession.cs](client/Assets/Scripts/ExampleSession.cs) |

模擬 owner 每 2 ticks 保存一份目標資料，最多 90 份（約 6 秒）。歷史檢視可讀這個範圍；射擊補償另受 15 ticks（0.5 秒）限制，兩者不混用。查詢的 bracket 複製及範圍判定由 SDK 處理，位置內插、射線幾何及傷害仍是應用規則。

射擊以伺服器當下的玩家位置為起點，客戶端提交看到的目標位置、呈現 tick、最新觀察 tick 與生命。伺服器限制觀察 tick 不超過已發送的快照上限，驗證時間窗與生命，再讀歷史。SDK 的時間窗不證明玩家真的看過某份快照；正式遊戲可接入自己的輸入／呈現時間驗證。

伺服器同時計算目前／歷史命中，依選定模式決定是否扣目前血量。回溯只讀取位置副本，從不把 live 目標搬到過去或還原過去 HP。射擊 ID 沿用同一連線的動作去重，重送不重複扣血；新生命清除歷史，舊生命射擊不能傷害新目標。

## 畫面與資料

淺藍圈是歷史查詢；紫色圈是射擊使用的歷史位置；橘色圈是判定當時的位置。這些輪廓用來說明當次結果，不是額外的權威實體。射擊按鈕由 C# 取樣目前呈現的目標；C# 只保存呈現資料與送出動作，幾何與傷害在 Go 執行。

歷史查詢會合併操作，最多一個等待中的要求，兩秒無回覆才重試。改秒數或生命後忽略不符的舊查詢結果。啟動初期可用資料較少；目標重生後要重新累積歷史，查不到時保持未找到，不外推。

詳細訊息見 [PROTOCOL](PROTOCOL.md)。與 [Replay 教學](../replay/README.md) 的離線檔案播放各有用途：這課持續讀取 live 模擬的近期歷史來判定互動。

## 執行與驗證

```powershell
./examples/history-lagcomp/scripts/Run.ps1 -Http 127.0.0.1:18105 -BackendPort 18169
./examples/history-lagcomp/scripts/Test-History.ps1
```

工具前提與其他操作教學相同：Go、含 ASP.NET Core 的 .NET 8 SDK、測試用 Node；預設採本機 Unity 附帶 dotnet，不需 Unity 授權或 NuGet 還原。測試埠為 18172／18173，輸出與正式 run 分開。

Go、純 C#、雙條真實 SDK WS 及正式網頁原始碼流程涵蓋歷史查詢不改 live 狀態、延遲下目前未中／回溯命中、時間窗、去重、生命與重連。網頁來源測試使用 DOM／Canvas 替身；另外已完成雙 Windows Player 的連線／移動，以及實際 WebGL 的歷史查詢、當下未中／回溯命中操作。證據與平台界線見 [VALIDATION](../../docs/VALIDATION.md)。

## Unity 版

另附 [Unity 專案](client) 與 [當課場景接線](client/Assets/Scripts/Unity/ExampleClient.cs)。開啟專案、Windows／WebGL 建置與操作方式統一見 [Unity 教學說明](../unity-common/README.md)。預設瀏覽器入口維持不變。
