# AOI 興趣範圍同步

依需求選修：先理解 [Shared World](../shared-world/README.md) 的單一權威世界、每位接收者與完整清單。本課只縮小接收者投影，需處理物件離開／重入；不用先學 Replay、Codec 或歷史補償。選課見 [學習路線](../README.md)，伺服器篩選與客戶端清理接法見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

雙擊框架根目錄的 [Start.cmd](../../Start.cmd)，選 **7. AOI 興趣範圍同步**，服務啟動後開啟 [http://127.0.0.1:18101/](http://127.0.0.1:18101/)。保留終端，按 Ctrl+C 停止。

也可從 `gamenetkit` 目錄執行：

```powershell
./examples/Run.ps1 -Example Interest
```

## 先照這樣操作

1. 預設「全部同步」，能看到 12 個綠色地標與 1 個金色常駐地標。
2. 按「範圍同步 AOI」。收到的物件變少，權威世界仍保留原本數量。
3. 點畫面，用 WASD／方向鍵走近其他區域；也可勾「自動左右移動」。觀察物件進出接收清單。
4. 按「全部同步」，遠處物件會再次出現，沿用相同 ID。
5. 再開第二個分頁，各自移動及切換模式，觀察兩名玩家收到的內容不同。

自己與金色地標始終同步；其他玩家、一般地標與玩家建立的標記依範圍篩選。圈的中心採伺服器最新權威位置。自己的預測＋平滑校正與其他玩家的自適應插值仍有保留；加入回包延遲時，圈可能落後於自己立即預測的位置。

## 展開進階區可以看什麼

- 改成半徑 5，比較接收範圍與數量。
- 顯示緩衝外圈：進入使用內圈，已收到的物件超過「半徑＋0.75」才離開，減少邊界反覆進出。
- 查看實際接收清單、加入／移出記錄，以及最新 Go → C# 快照的 JSON byte 數。
- 沿用前一課新增、移除、同 ID 重生自己的標記。走遠後標記會離開接收清單，但世界數量保持；真正移除才讓世界數量減少。
- 切換預測、校正、遠端插值與回包延遲，觀察不同責任如何一起運作。

即使自己的標記目前在範圍外，仍可對最後一個自己的標記執行移除／重生，伺服器會依權威歸屬處理。靠近後會收到它目前的生命。每人最多 4 個標記，離線會移除自己建立的標記；中立地標保留。

## 這課加入什麼

實際使用 [Go interest.Radius](../../server/interest/radius.go)，透過 [netcore.Interest](../../server/netcore/contracts.go) 契約組裝。每位玩家都有獨立 selector，保存各自的進出範圍歷史；切換模式或半徑會建立新的 selector。

| 接線 | 位置 |
| --- | --- |
| 每位接收者建立候選清單、設定必要物件、呼叫 Select，再篩出傳輸清單 | [projection.go](server/projection.go) |
| 模擬步進後為各接收者產生自己的快照，保留同一份權威 world | [world.go](server/world.go) |
| 驗證完整「接收清單」後套用正式 ReplicaStore，離開範圍時移出本機 replicas | [ReplicaState.cs](client/Assets/Scripts/ReplicaState.cs) |
| 玩家重新進入範圍時，從新收到的位置建立平滑基準，避免採用離開前的舊座標 | [RemotePresentation.cs](client/Assets/Scripts/RemotePresentation.cs) |

接到自己的應用時，在權威資料產生後、快照編碼前，為每位接收者呼叫 `Select`；傳送篩選後的完整清單。客戶端依差異建立／更新／回收呈現物件。接收清單的移出不代表權威實體被刪除，應用要保留這個責任區分。

本課會同時篩選玩家與標記，因此收到標記時，其 owner 可能位於接收範圍外。客戶端驗證 DTO 類型與歸屬欄位，由伺服器負責動作授權；全量模式仍驗證 owner 存在。物件生命與 ID 不因接收範圍改變。

## 範圍與限制

- 同一個世界最多 8 名玩家、13 個中立／常駐地標與 32 個玩家標記。SDK selector 容量為 53 個候選實體。
- 預設半徑 3、離開緩衝 0.75；頁面提供半徑 3／5，伺服器接受有限的 2～6。
- 接收中心由伺服器自己的玩家位置決定，客戶端只選模式與半徑。常駐旗標由伺服器應用設定。
- 世界總數只提供數量，不附帶圈外玩家與物件的座標。物件動作回覆沿用既有可靠控制訊息，與快照篩選分開。
- JSON bytes 計算 Go → C# 的應用快照，不含 WS／TCP 標頭或 C# → 瀏覽器的教學呈現訊息。物件數量與 payload 大小可比較，不代表實際遊戲固定的節省比例。
- 此處的 AOI 是距離與傳輸投影；遮蔽物、隊伍規則及遊戲視野可另實作 `netcore.Interest`。完整契約見 [MODULES](../../docs/MODULES.md#interest)。

本課是聚焦的 AOI 範例；錄製與播放仍在獨立的 [第 6 課](../replay/README.md)。瀏覽器負責按鈕與 Canvas，實際選取在 Go SDK，接收與移動呈現在 C# SDK。本課另附使用相同 C# session 的 Unity 專案。

## 啟動與驗證

需要 Go、含 ASP.NET Core 的 .NET 8 SDK，以及測試用的 Node。腳本預設使用本機 Unity 安裝附帶的 .NET SDK，可用 `-DotNet` 指定其他版本；啟動不需要 Unity 授權或 NuGet 還原。網頁預設 18101，內部服務 18149。

```powershell
./examples/interest/scripts/Run.ps1 -Http 127.0.0.1:18101 -BackendPort 18149
./examples/interest/scripts/Test-Interest.ps1
```

測試使用隔離埠 18152／18153 與 `.local/interest/tests`，不覆蓋正在執行的 `run` 輸出。涵蓋真正 SDK 的每接收者篩選、遲滯、必要實體、範圍重入與生命、JSON payload、狀態驗證與重連。正式網頁原始碼以 DOM／Canvas 替身連到真正服務；這不代表實際 renderer 或 Unity Player 已驗收，結果見 [VALIDATION](../../docs/VALIDATION.md)。

## Unity 版

另附 [Unity 專案](client) 與 [當課場景接線](client/Assets/Scripts/Unity/ExampleClient.cs)。開啟專案、Windows／WebGL 建置與操作方式統一見 [Unity 教學說明](../unity-common/README.md)。預設瀏覽器入口維持不變。
