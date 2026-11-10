# 插值：開關後，移動差在哪裡？

核心課程：先看 [Shared World](../shared-world/README.md)，理解快照與每幀呈現的差別。本課學遠端位置的歷史取樣與緩衝，下一站是 [預測與校正](../prediction/README.md)，處理自己的輸入反應。完整順序見 [學習路線](../README.md)；從基本版逐步加入插值見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

這是可操作的組件教學。方塊自動往返，你可以切換關閉／固定／自適應插值，加入延遲、抖動與快照丟失，觀察實心方塊與最新位置參考框的差異。基礎多人版本仍保持在 [Shared World](../shared-world/README.md)。

## 直接使用

在 gamenetkit 根目錄的 PowerShell 執行，保持終端開著：

```powershell
./examples/Run.ps1 -Example Interpolation
```

開啟 [http://127.0.0.1:18094/](http://127.0.0.1:18094/) 即自動加入。這個瀏覽器介面使用本機 C# SDK 計算，不需要 Unity Editor 授權或 WebGL 建置。需要 Go 與 .NET 8 SDK（含 ASP.NET Core）；預設使用已安裝的 Unity 附帶 SDK，不需 NuGet 還原。首次編譯稍候片刻，看到 `Interpolation lesson:` 即可開頁面。

1. 保留「自動往返」與「自己的方塊也比較插值」。單一視窗即可觀察效果。
2. 選「關閉」：方塊收到新快照才改變位置，和外框重疊。
3. 選「固定插值」：方塊在兩份快照之間連續取樣，位置會落後外框；緩衝約 133 ms。
4. 選「自適應插值」，再切「延遲、抖動、丟失」情境：觀察緩衝與平順度的變化。恢復乾淨網路後，緩衝會慢慢降低。
5. 開第二個並排視窗，橘色代表對方。取消「自己的方塊也比較插值」，即可只處理其他玩家；關掉自動往返後，點擊場地用 WASD／方向鍵操作，失焦會送零停止。

實心與外框使用**同一批經過模擬交付的快照**，差別只在呈現取樣；外框不是無延遲的伺服器真實位置。丟失只作用在 snapshot，輸入／welcome 不丟棄。調整網路情境會清除舊待交付快照；切換模式會用現有歷史重新建立時間軸，因此切換當下可能跳到新的呈現位置。

插值不會降低網路延遲，也不替代本機預測。這一課預設把自己的權威快照也當觀察資料，方便單視窗比較，所以自己的操控也可能延後；正式遊戲通常預測自己、插值遠端。

按 `Ctrl+C` 結束服務。18094 是操作頁，18099 是腳本建立的隔離共用世界；兩者都需可用，腳本會檢查並在結束時清理自己啟動的世界程序。可改位址／SDK／內部埠：

```powershell
./examples/interpolation/scripts/Run.ps1 -Http '127.0.0.1:18104' -BackendPort 18109
```

`-DotNet` 可指定其他 .NET 8 SDK 的 `dotnet.exe`。

## 增加組件，實際改了什麼？

相對 Shared World，應用增加以下接線，SDK 本身沒有修改：

| 步驟 | 實際檔案 | 新增內容 |
| --- | --- | --- |
| 安裝 | [manifest.json](client/Packages/manifest.json) | core／transport 之外加入 `com.realtime.interpolation`；`simulation` 只用於教學網路情境 |
| 引用 | [應用 asmdef](client/Assets/Scripts/Realtime.Examples.InterpolationLesson.asmdef) | 加 `Realtime.Interpolation`、`Realtime.Simulation` |
| 組裝與儲存 | [InterpolatedWorld.cs](client/Assets/Scripts/InterpolatedWorld.cs) | 註冊 latest／fixed／adaptive；保存最多 128 份快照 |
| 收包 | [ExampleSession.cs](client/Assets/Scripts/ExampleSession.cs) | 驗證狀態、交付快照；welcome 重設，正常快照 Observe，斷線清除 |
| 每幀與取樣 | [InterpolatedWorld.cs](client/Assets/Scripts/InterpolatedWorld.cs) | Advance(now)，以 RenderTick 找前後快照並線性取樣 DTO |
| Unity 呈現 | [WorldPresentation.cs](client/Assets/Scripts/Unity/WorldPresentation.cs)、[ExampleClient.cs](client/Assets/Scripts/Unity/ExampleClient.cs) | 把取樣座標套用到方塊；UI 切換模式與情境 |

從 `InterpolatedWorld` 開始讀：`SetMode` 選策略、`Accept` 保存資料並 Observe、`Advance` 推進時間、`Sample` 取出座標。時間軸不認識 PlayerState、GameObject 或碰撞，快照與場景仍由應用擁有。

所有本機時間使用同一個 `MessageTransport.ClockMS / 1000` 單調秒數，避免混用 Unity realtime 與 Stopwatch 的絕對原點。取樣夾在歷史範圍內，不外推到最新快照之後；玩家離開立即移除，新連線不沿用舊快照。

停用時切回 `LatestSnapshotTimeline` 並採最新位置。若實體移除 interpolation 套件，還要移除 fixed／adaptive 的具體型別註冊與 asmdef／manifest 引用。simulation 可獨立移除，改為直接交付快照。完整 API 與限制見 [MODULES](../../docs/MODULES.md#interpolation)。

## 瀏覽器介面與 Unity 的關係

瀏覽器操作介面是本機教學用的 [browser-host](browser-host/Program.cs)：每個視窗有自己的 `ExampleSession`，透過 SDK 原生 WebSocket 連到 Gin 共用世界。它直接使用上表同一份 C# 應用、插值及弱網組件；[app.js](web/app.js) 只傳操作與畫方塊，沒有另一份 JS 插值演算法。這條預覽通道多一層本機傳輸，因此不用它量測遊戲 RTT 或實際瀏覽器效能。

正式 Unity 接線使用 `client` 專案，場景為 `Assets/Scenes/Interpolation.unity`，版本 6000.6.8f1；Play 後顯示相同模式與網路情境。原生預設連 18094，需以 Go 模式啟動服務，與瀏覽器預覽二選一使用相同埠：

```powershell
./examples/Run.ps1 -Example Interpolation -Unity
```

另一個終端建置：

```powershell
./examples/Build-Unity.ps1 -Project Interpolation -Target Windows
Start-Process ./examples/interpolation/client/Builds/Windows/InterpolationDemo.exe
```

WebGL 建置後使用同一個 `-Unity` 服務與 `/unity/`：

```powershell
./examples/Build-Unity.ps1 -Project Interpolation -Target Web
Start-Process 'http://127.0.0.1:18094/unity/'
```

瀏覽器操作頁不依賴 Unity WebGL 建置。Unity 建置需要有效授權；各平台的實際驗證範圍見 [VALIDATION](../../docs/VALIDATION.md)，原生 C# 預覽不能代替 IL2CPP／WebGL 驗收。

## 驗證

```powershell
./examples/interpolation/scripts/Test-Interpolation.ps1
```

測試使用 18102／18103 隔離埠，可用 `-Port`／`-WebPort` 改埠。檢查實際 SDK 在 30／60／144 FPS 下的取樣、模式切換、有界歷史、離開、重連、舊 epoch 與受調理的真實 WS；另以 Node 執行正式頁面原始碼，連到真正 C# 教學主機，操作所有模式、抖動、鍵盤／失焦與雙視窗生命週期。

成功訊號為 `RF_INTERPOLATION PASS` 與 `RF_INTERPOLATION_BROWSER_SOURCE PASS`。Node 使用 DOM／Canvas 測試替身，沒有驗收真正 renderer；Unity 資產、編譯與平台限制見 [VALIDATION](../../docs/VALIDATION.md)。


