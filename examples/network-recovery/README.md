# 即時連線與恢復：弱網、診斷、時鐘與自動重連

核心課程：先看 [預測與校正](../prediction/README.md)，並理解連線、收包與每幀更新。本課練習弱網與斷線時的清理／重建，下一站是 [物件同步與動作確認](../objects-actions/README.md)。診斷、時鐘、弱網模擬可分別加入；自動重連策略已在 core。完整順序見 [學習路線](../README.md)，各能力的接入點見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

這頁整合四項能力：實際 SDK 的 MessageConditioner、ClientDiagnostics、DisciplinedClock 與 core ReconnectPolicy。自己的預測／校正、其他玩家的插值同時可用。錄製／離線播放維持獨立情境，不在這個頁面。

## 直接操作

在 gamenetkit 根目錄的 PowerShell 執行，保持終端開著：

```powershell
./examples/Run.ps1 -Example NetworkRecovery
```

開啟 [http://127.0.0.1:18096/](http://127.0.0.1:18096/)，頁面自動加入。使用 Go 與 .NET 8 SDK（含 ASP.NET Core），預設採本機 Unity 附帶 SDK，不需 Unity Editor 授權或 NuGet 還原。第一次先編譯，看到 `Network recovery lesson:` 後開啟頁面。

操作頁 18096、私有 Gin 世界 18119；可與先前插值 18094／18099、預測 18095／18109 同時執行。停止時按 Ctrl+C。自訂位址、SDK：

```powershell
./examples/network-recovery/scripts/Run.ps1 -Http '127.0.0.1:18126' -BackendPort 18129
```

`-DotNet` 指定其他 .NET 8 SDK 的 dotnet.exe。測試與正式執行分別編譯到 `.local/network-recovery/tests`、`.local/network-recovery/run`，不覆寫正在使用的教學主機。

## 入門先只學自動重連

預設頁面只顯示三個按鈕與方塊，參數及數字都收在下方「進階觀察」。如果服務已經開著，Ctrl+F5 重新載入即可使用新介面，不必為這次前端修改重啟服務。

1. 按「正常移動／重新開始」，確認方塊往返。
2. 按「模擬意外斷線」，先等幾秒：方塊消失後，程式會自己重試並重新加入。
3. 按「手動停止」，它不會自動連回；按第一個按鈕才再加入。

這一步只理解「意外斷線會自動重試、手動停止不會」。理解後再展開「進階觀察」，一次選下列一項體驗。

## 進階體驗

1. 展開「移動呈現設定」，可操作預測、校正及遠端平滑：藍色是自己，橘色是其他玩家。也可取消自動往返，用 WASD／方向鍵控制，失焦送零。
2. 選乾淨／150 ms／300 ms／抖動／高損失預設，再分別調送出延遲、接收延遲、抖動與損失。外框是收到的權威位置，藍色及黃點是自己的顯示／內部預測位置。
3. 看應用往返時間、輸入回覆、快照間隔與 JSON 流量；切換診斷開關或清除量測。沒有樣本時顯示暖機中，不把零當成已量測的零延遲。
4. 切「最後觀察 tick」和「RTT 校準時鐘」，觀察估計值與遠端時間軸。時鐘只是估計，沒有權威，也不會外推到無限遠。
5. 按「暫停快照 3 秒」：連線仍在，權威快照暫停交付，預測在期限後停止；恢復後重新對齊。這不觸發自動重連。
6. 保持自動重連，按「模擬這位玩家斷線 4 秒」：真正關掉該玩家的遊戲 WebSocket，再模擬該連線暫時不可用。看倒數、退避次數、事件紀錄與重新加入的 ID。
7. 再模擬斷線並按「停止重試」，確認不會自己連回；最後手動重新連線。也可先關閉自動重連再測試斷線。

重試間隔為 0.5、1、2、最多 3 秒，期限 30 秒。四秒的模擬不可用結束後，可能還需等下一個已安排的重試，因此不保證剛好四秒就恢復。初次連線失敗不自動循環；必須先成功連線，才啟用意外斷線恢復。協定錯誤／容量拒絕不自動重試，手動中斷／停止重試會取消等待及進行中的嘗試。

**重連以新玩家 ID 重新加入，不保留席位或原位置。** SDK 策略只決定何時重試，身份與席位恢復屬於應用。重連會清除舊預測、快照、探測對應、傳輸 epoch 與時鐘基準；保留你選的預測、校正、插值、網路設定與有界事件紀錄。網頁控制通道維持連線，讓你觀察遊戲 SDK 連線恢復；手動「重新連線」會建立新的教學 session，連同模擬中斷狀態一併重設。

## 量測與模擬的範圍

- 兩個方向各有有界的 MessageConditioner。可替換的 input／snapshot 可模擬丟失；welcome、探測與推移控制不模擬丟失。這是應用訊息調理，不是 TCP／QUIC 封包模擬。
- 改設定會重建佇列並清掉舊待交付資料與丟失計數；仍在執行的快照暫停期限會保留。暫停期間快照被丟棄，控制回覆則保留等待恢復。
- ping 的 id 由應用保存配對，伺服器只在世界 owner 中擷取 tick，回覆 pong。每個回覆最多使用一次，未知／過期／舊連線回覆不會新增 RTT 樣本。
- 應用往返從送入模擬鏈路，到配對回覆經接收鏈路交付為止，包含模擬延遲與伺服器等待。它不是對外網路 ping；不將本機瀏覽器預覽通道當作遊戲網路基準測試。
- JSON 流量只統計遊戲 SDK 實際收送的文字內容，不含 WebSocket／TCP 額外成本，也不含瀏覽器畫面資料。C# 更新間隔不是瀏覽器 FPS。
- 輸入在送入「模擬網路」的邊界記錄 SentInput，表示封包開始經過被模擬的網路延遲；不能等延遲結束才把預測歷史往後移。實體 WebSocket 轉送的流量則在真正寫入時統計。
- 收到 welcome 先建立無 RTT 的時鐘基準，配對探測回來後再用 RTT 中點校準；估計最多外推半秒。上下行不對稱與主機排程都會造成誤差，不能作精確單向延遲或反作弊證明。
- 自動重連不因單純快照變慢而觸發；傳輸錯誤、握手期限或四秒完全沒有伺服器流量才視為連線故障。快照停頓有自己的預測停止期限。

## 組件接線在哪裡？

| 檔案 | 要讀的接線 |
| --- | --- |
| [SimulatedLink.cs](client/Assets/Scripts/SimulatedLink.cs) | 雙向 Enqueue／TryTake、可丟棄分類、暫停與 ClearPending |
| [SessionTelemetry.cs](client/Assets/Scripts/SessionTelemetry.cs) | 探測／輸入的有界配對、一次性消費、Traffic／Timing、Clock.Reset／Observe |
| [RecoveryController.cs](client/Assets/Scripts/RecoveryController.cs) | Connected → Fail → Poll、手動 Stop、重試開關、最多 16 筆事件 |
| [ExampleSession.cs](client/Assets/Scripts/ExampleSession.cs) | 送收、延遲交付、失敗分類、重建 attempt 與生命週期 |
| [RemotePresentation.cs](client/Assets/Scripts/RemotePresentation.cs) | 對 clock-aware 插值時間軸注入選定的 IServerClock |
| [PredictedPlayer.cs](client/Assets/Scripts/PredictedPlayer.cs)、[MotionModel.cs](client/Assets/Scripts/MotionModel.cs) | 自己的預測／校正，規則對照 Go；沿用前一課 |
| [server/session.go](server/session.go)、[world.go](server/world.go) | 嚴格 ping、單一寫入者、owner 產生 pong tick、有界回覆佇列 |
| [browser-host](browser-host/Program.cs)、[app.js](web/app.js) | 操作及畫面適配，不另寫一套 JS 同步演算法 |

新增依賴是 clock／diagnostics，simulation 與 core 內的 ReconnectPolicy 這次擴充接線。完整純 C# 應用仍需要 core、transport、prediction、correction、interpolation、simulation、clock、diagnostics。自己的 Unity 專案安裝對應 `com.realtime.*` 套件、在 asmdef 引用 `Realtime.*`，再接入應用的 DTO／JSON／場景。這一課另附獨立 Unity 場景與呈現接線；參考編譯不能當成 Player 或 WebGL 執行驗收。

停用方式：弱網直接交付；診斷用 NoClientDiagnostics；時鐘用 SnapshotClock；自動重連關閉時只保留手動連線。它們各自有獨立開關，不需要同時停用預測與插值。移除套件時另清掉具體型別註冊和引用；[MODULES](../../docs/MODULES.md) 保留完整契約與限制。

## 驗證

```powershell
./examples/network-recovery/scripts/Test-NetworkRecovery.ps1
```

隔離埠為 18122／18123，使用 `-Port`／`-WebPort` 可修改。測試保留預測、校正與遠端插值案例，另驗證雙向調理、RTT 配對與去重、時鐘界線、診斷開關、快照暫停、重試／取消／期限、真正 SDK 連線重建、新 ID、舊 epoch 與有界狀態。正式頁面原始碼以 Node 連到真正 C#／Gin 服務，DOM／Canvas 使用替身。

成功訊號為 `RF_NETWORK_RECOVERY PASS` 與 `RF_NETWORK_RECOVERY_BROWSER_SOURCE PASS`；這不是實際瀏覽器 renderer 驗收。Go test／vet、編譯與證據集中於 [VALIDATION](../../docs/VALIDATION.md)。


## Unity 版

另附 [Unity 專案](client) 與 [當課場景接線](client/Assets/Scripts/Unity/ExampleClient.cs)。開啟專案、Windows／WebGL 建置與操作方式統一見 [Unity 教學說明](../unity-common/README.md)。預設瀏覽器入口維持不變。


