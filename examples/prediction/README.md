# 預測與校正：按下就移動，權威回來後再對齊

核心課程：先理解 [Shared World](../shared-world/README.md) 的權威快照與輸入歸屬，再用 [插值](../interpolation/README.md) 區分自己的預測與遠端呈現。本課讀程式時要留意輸入序號、權威 ack 及共用移動模型；下一站是 [連線恢復](../network-recovery/README.md)。完整順序見 [學習路線](../README.md)，加入套件與生命週期接線見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

這一課以自己的移動為主，使用真正的 C# `ReplayPrediction`、`AuthorityPrediction`、`SnapCorrection` 與 `ExponentialCorrection`。你可以切換預測、增加快照回包延遲，再讓伺服器推移角色，觀察修正方式。下方另有「遠端平滑」開關，預設用自適應插值處理其他玩家；單獨學插值仍可使用 [前一課](../interpolation/README.md)。

## 直接操作

在 gamenetkit 根目錄的 PowerShell 執行，保持終端開著：

```powershell
./examples/Run.ps1 -Example Prediction
```

開啟 [http://127.0.0.1:18095/](http://127.0.0.1:18095/)，頁面會自動連線。需要 Go 與 .NET 8 SDK（含 ASP.NET Core），預設使用本機 Unity 附帶的 SDK，不需 Editor 授權或 NuGet 還原。額外的本機教學通道不是遊戲網路效能量測工具。

1. 預設回包延遲 150 ms、自動往返。切「關閉：等待權威」與「啟用預測」，比較藍色方塊與權威外框。
2. 改為 300 ms，取消自動往返，點擊場地，用 WASD／方向鍵體驗按下、放開與反向。啟用預測時會先模擬自己的輸入；失焦會送零停止。
3. 選「直接校正」，按「伺服器向左推移一單位」：回包後內部狀態與顯示位置會一起改變。
4. 選「平滑校正」再推移：黃點是立即修正的內部預測位置，藍色是帶短暫顯示偏移的位置，偏移會逐漸歸零。
5. 開另一個並排視窗，確認每個視窗只控制自己。推移按鈕也只能改自己的權威位置。
6. 在「其他玩家的平滑移動」切換關閉／固定／自適應；只改橘色遠端角色。自己的平滑校正在上方控制，兩者可以同時啟用。

外框是**已收到的權威快照**，不是伺服器此刻的位置；預測領先這個外框是正常現象。只在 snapshot 回包加入 0／150／300 ms 延遲，輸入直接交付，沒有宣稱模擬完整 RTT。改延遲會清除待交付快照；切換預測模式會用最新權威建立新基準，切換當下可能跳動。

平滑只影響顯示座標，不延遲權威狀態／碰撞規則的採用。兩單位以上的修正會直接校正；位置仍限制在 [-8,8]。長時間沒有新權威快照時，預測會在 0.8 秒期限後停止，避免無限制猜測。

停止時按 `Ctrl+C`。操作頁使用 18095，本課獨立的 Gin 世界使用 18109，與插值教學的 18094／18099 分開。自訂位址：

```powershell
./examples/prediction/scripts/Run.ps1 -Http '127.0.0.1:18115' -BackendPort 18119
```

`-DotNet` 可指定其他 .NET 8 SDK 的 `dotnet.exe`。

## 加入組件的接線

| 步驟 | 實際來源 | 責任 |
| --- | --- | --- |
| 應用規則 | [MotionModel.cs](client/Assets/Scripts/MotionModel.cs) | CreateState／深複製／Step；速度、斜向正規化與界線，對照 Go 規則 |
| 組裝與校正 | [PredictedPlayer.cs](client/Assets/Scripts/PredictedPlayer.cs) | 選 authority／prediction、snap／smooth；保存顯示偏移與重設基準 |
| 遠端平滑 | [RemotePresentation.cs](client/Assets/Scripts/RemotePresentation.cs) | 使用 interpolation SDK 時間軸，保存與取樣遠端快照；不覆蓋自己的預測座標 |
| 送出與收包 | [ExampleSession.cs](client/Assets/Scripts/ExampleSession.cs) | 在實際呼叫傳輸送出時記錄序號與時間；每幀 Advance；驗證快照後 Reconcile |
| 應用協定 | [WorldProtocol.cs](client/Assets/Scripts/WorldProtocol.cs)、[PROTOCOL](PROTOCOL.md) | 輸入 ack 與首次套用 tick，不是框架固定的遊戲 DTO |
| 伺服器規則 | [world.go](server/world.go)、[session.go](server/session.go) | 單一 owner 套用輸入、回報時間語意、只推移該連線玩家 |
| 瀏覽器適配 | [browser-host](browser-host/Program.cs)、[app.js](web/app.js) | 同一份 C# 邏輯計算；JS 只傳操作與畫圖，沒有另一份預測演算法 |

先讀 `MotionModel`，再讀 `PredictedPlayer` 的 SentInput／Advance／Reconcile。`PredictionAck` 必須由應用協定映射，不能只拿 sequence 相減當經過的時間。

這個伺服器先 Apply 命令，再 Step：新的 `inputTick = 原 tick + 1`。快照中若 `ack > 0`，已套用步數為 `snapshot.tick - inputTick + 1`，再限制在輸入有效期 15 tick 內；`ack = 0` 時為零。SDK 以該輸入實際送出時間加上套用時長為重播錨點，重播後續尚未確認的輸入區間。

收到新權威時立即更新內部預測，顯示層另外保留 `原顯示位置 - 新預測位置`，每幀依 correction policy 衰減。直接校正／大誤差／新連線清掉偏移。welcome 與斷線重設輸入歷史、權威基準、待交付資料與顯示偏移；舊 epoch 不能污染新連線。

## 加入自己的 Unity 專案

這一課交付瀏覽器可操作範例、純 C# 應用邏輯與 Unity 場景。與 core／transport 一起安裝 prediction／correction；若保留遠端平滑也需 interpolation，本課的 simulation 只負責回包延遲，可另行停用。

```json
{"dependencies":{"com.realtime.prediction":"file:../../../../packages/unity/prediction","com.realtime.correction":"file:../../../../packages/unity/correction","com.realtime.interpolation":"file:../../../../packages/unity/interpolation"}}
```

這是 manifest 合併片段，路徑以 `examples/<應用>/client/Packages` 為例，需依自己的專案位置調整。使用端 asmdef 加 `Realtime.Prediction`／`Realtime.Correction`／`Realtime.Interpolation`，保留既有 core 等引用。自己的呈現取 `PredictedPlayer.X/Z`，其他玩家才取 `RemotePresentation.Sample`；權威碰撞／規則不可讀顯示偏移。Unity WebGL 另需接上 callback target 與 `MessageTransport.Receive`，本機瀏覽器教學主機使用的是 SDK 原生 WebSocket。

停用預測改用 `AuthorityPrediction`，停用平滑改 `SnapCorrection`；真正移除套件時也要移除具體型別與 asmdef／manifest 引用。完整限制見 [MODULES](../../docs/MODULES.md#prediction)。

## 驗證

```powershell
./examples/prediction/scripts/Test-Prediction.ps1
```

使用 18112／18113 隔離埠，可用 `-Port`／`-WebPort` 改埠。包含真實 SDK 的 30／60／144 FPS、ack 重播、輸入停止／反向、斜向與界線、期限、直接／平滑修正、兩條實際 WS 的輸入歸屬、伺服器推移及重連；另以正式頁面原始碼連到真正 C#／Go 服務，操作所有開關及鍵盤／失焦。

成功訊號為 `RF_PREDICTION PASS`、`RF_PREDICTION_BROWSER_SOURCE PASS`。頁面原始碼測試使用 DOM／Canvas 替身，與真正 renderer／Unity Player 驗收不同。Go test／vet 與測試證據集中在 [VALIDATION](../../docs/VALIDATION.md)。

## Unity 版

另附 [Unity 專案](client) 與 [當課場景接線](client/Assets/Scripts/Unity/ExampleClient.cs)。開啟專案、Windows／WebGL 建置與操作方式統一見 [Unity 教學說明](../unity-common/README.md)。預設瀏覽器入口維持不變。


