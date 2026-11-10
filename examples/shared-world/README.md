# Shared World：多人共用世界教學

學習起點：先操作兩個視窗；讀程式前可先看 [Minimal 最短接線](../../docs/GETTING_STARTED.md#4-看懂-go-與-unity-如何接線)，理解 input、snapshot 與 tick。本課學多人共用世界及加入／離開，下一站是 [插值](../interpolation/README.md)。完整順序見 [學習路線](../README.md)，動手擴充見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

這個範例固定只使用 Unity core＋transport，以及 Go loop／transport。目的是看懂兩個客戶端如何共用一個權威世界，保持方塊與少量狀態；戰鬥、房間與其他組件會在 [Rumblebound](../rumblebound/README.md) 整合。學習順序見 [examples 索引](../README.md)。

## 啟動與操作

從 `gamenetkit` 目錄執行；腳本自行處理 Go 路徑與工作目錄：

```powershell
./examples/Run.ps1 -Example SharedWorld
```

若目前不在框架目錄，可使用完整腳本路徑（請將 C:\path\to 換成實際位置）：

```powershell
& 'C:\path\to\gamenetkit\examples\Run.ps1' -Example SharedWorld
```

另開兩個 [http://127.0.0.1:18092/](http://127.0.0.1:18092/) 視窗，各按「連線」。點擊場地後以 WASD／方向鍵移動；藍色是自己，橘色是其他玩家。放開或失焦停止，斷開一人後另一人仍可移動；重連取得新 ID。最多八人，目前直接顯示權威位置，尚無預測／插值。

`Ctrl+C` 結束服務；修改 Go 後需要重新啟動。可加 `-Http '127.0.0.1:18099'` 改埠。瀏覽器模組由 HTTP 明確回傳 JavaScript MIME，`/transport.js` 直接使用 SDK 原檔。

若手動進入 `examples/shared-world/server` 使用 `go run`，Go 參數仍是 `-http`、`-transport-runtime`、`-web-dir`、`-unity-dir`；預設來源依序為 SDK `../../../packages/unity/transport/Browser/transport.js`、`../web`、`../client/Builds/WebGL`。路徑在 handler 建立時轉為絕對路徑。

## 接線順序

| 順序 | 先讀哪個檔案 | 要理解的責任 |
| --- | --- | --- |
| 1 | [NetcodeComposition](client/Assets/Scripts/NetcodeComposition.cs) | 組裝 MessageTransport 與 LatestSnapshotTimeline；不含遊戲規則 |
| 2 | [WorldProtocol](client/Assets/Scripts/WorldProtocol.cs)、[PROTOCOL](PROTOCOL.md) | DTO 屬於應用；輸入不能指定要操控別人的 playerID |
| 3 | [ExampleSession](client/Assets/Scripts/ExampleSession.cs) | Connect／Pump／送輸入／Disconnect；epoch、序號與時間軸生命週期 |
| 4 | [ReplicaState](client/Assets/Scripts/ReplicaState.cs) | 驗證後原子套用完整清單，拒絕舊 tick；目前是基本應用邏輯，未安裝 replication |
| 5 | [ExampleClient](client/Assets/Scripts/Unity/ExampleClient.cs)、[WorldPresentation](client/Assets/Scripts/Unity/WorldPresentation.cs) | Unity JSON／鍵盤／失焦與方塊呈現，沒有權威規則 |
| 6 | [server/main.go](server/main.go)、[session.go](server/session.go) | Gin HTTP、coder/websocket 接納、嚴格協定與取消 |
| 7 | [world.go](server/world.go) | 一個 loop.Run 擁有所有玩家；個別離線不結束世界 |

和 Minimal 的差異是世界生命週期：Minimal 每條連線各有自己的世界；這裡由服務持有單一 world，session 只排入 join／input／remove 命令。世界以 30 Hz 步進、15 Hz 廣播；輸入 500 ms 未更新就停止，慢接收者只保留最新待送快照。完整欄位集中在 PROTOCOL，這份教學不另抄一套。

可做三個小練習：開兩個視窗觀察相同 roster／tick；只移動一人確認輸入歸屬；關掉一人再重連，觀察清單移除與新 ID。需要加入組件時再讀 [組件課程](../modules/README.md)，不把所有變體加回基本版。

## Unity

專案為 `examples/shared-world/client`，Unity 6000.6.8f1。開啟 `Assets/Scenes/SharedWorld.unity` 按 Play；bootstrap 建立場地、相機與方塊。從框架根目錄建置：

```powershell
./examples/Build-Unity.ps1 -Project SharedWorld -Target Windows
Start-Process ./examples/shared-world/client/Builds/Windows/SharedWorld.exe
Start-Process ./examples/shared-world/client/Builds/Windows/SharedWorld.exe
```

原生預設 `ws://127.0.0.1:18092/ws`，以 `-rf-server http://127.0.0.1:其他埠` 覆寫。WebGL 使用頁面 origin：

```powershell
./examples/Build-Unity.ps1 -Project SharedWorld -Target Web
Start-Process 'http://127.0.0.1:18092/unity/'
```

Build 可用 `-TimeoutSeconds`，成功訊號為 `RF_SHARED_WORLD_BUILD PASS`。這個專案有自己的 assembly、Unity 資產 GUID 與建置輸出，不引用 Rumblebound 應用。

## 驗證與範圍

```powershell
./examples/shared-world/scripts/Test-SharedWorld.ps1 -NativeClient
```

腳本以隔離埠 18096 建置服務，驗證 HTTP 模組類型、正式 JS session 與 C# ExampleSession 各兩條真實 WS，檢查互見、自己的輸入、停止、離開、重連與舊回呼隔離，最後清理自己啟動的程序。`-Port` 可改埠，`-DotNet` 可指定 .NET 8 SDK。產物在 `.local/shared-world-teaching`，日誌使用 `shared-world-teaching-` 前綴。

C# 連線驗證使用主控台 JSON 適配器，不是 Unity Player。不能把編譯／連線測試當成畫面驗收；各平台的既有結果與未驗收項目分開記錄；最新紀錄見 [VALIDATION](../../docs/VALIDATION.md)。

