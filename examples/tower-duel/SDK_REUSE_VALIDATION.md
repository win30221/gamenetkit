# SDK 共用化驗證 · 2026-11-13

這次先處理客戶端重連排程及法術呈現時間軸。
本頁保留首輪 Unity／Windows 結果；後續 WebGL 重建與公開站部署已通過，見
[WebGL SDK 部署驗證](WEBGL_SDK_DEPLOYMENT_VALIDATION.md)。

## 改動

- `DuelSession` 使用現有 core `ReconnectPolicy`：30 秒恢復窗口，首次延遲 0.5 秒，
  後續指數退避，上限 3 秒。重試失敗不延長窗口，握手中仍會檢查整體期限。
- 一般 welcome 與已保存的原席位憑證會啟用恢復策略；welcome 成功後重設退避，
  主動離線、不可恢復錯誤及 Dispose 停止重試。原席位、手牌、待確認動作及 action watermark
  繼續依遊戲協定處理。
- 士兵與法術共用 SDK `FixedTimeline.RenderTick`，時間軸使用 SDK 傳輸記錄的
  `Incoming.ReceivedMS`，不把主執行緒排空佇列的時間當作抵達時間。
- 法術由 SDK `SnapshotBuffer` 的前後快照取樣，依權威 start/end 篩選並依 ID 合併。
  這樣新快照首次帶入法術時可以準時呈現，新快照移除法術時也不會提早結束畫面上的存續時間。
- 法術移動、命中音效及結束均依共用呈現 tick；快照停頓時不外推超過最新權威 tick。
  重連 welcome、重賽及結算會清除舊特效基準。

## 本次驗證

Unity 6000.6.8f1；Go 1.27.3。沿用 core、transport、interpolation 三個 SDK 套件。

| 驗證 | 結果 |
| --- | --- |
| 客戶端 Unity Play Mode 回歸測試 | PASS |
| SDK 原生契約測試 | PASS，五組 PASS 標記 |
| 伺服器 `go test -count=1 ./...` | PASS，選用渲染負載 fixture 未啟動 |
| 伺服器 `go vet ./...` | PASS |
| Unity `.meta`／GUID／腳本引用檢查 | 306 個檔案，0 errors、0 warnings |
| 以現有場景重建 Windows development Player | Succeeded，0 errors |
| 兩個真實 Windows Player 完整弱網對戰及原局恢復 | PASS |

客戶端回歸測試涵蓋：重試排程、退避上限、期限不被失敗延長、握手中到期、
舊 epoch 回呼、保存憑證恢復、未確認出牌保留與重複確認、手動取消、致命錯誤、
法術開始／命中／結束、前後快照清單、停頓上限、恢復中凍結、重賽及結算清理。

雙 Player 使用單向 120 ± 80 ms 的訊息層延遲，丟棄每第三次部署及第五次 ACK，
重複每第四次部署。這不是 IP 層丟包模擬。實際記錄為 3 條連線、16 則丟棄、6 則重複。

- 原局成功恢復 1 次；雙方完成同一局，結果皆為 seat 0 摧毀主堡獲勝。
- 雙方成功出牌各為 13／8 次，均操作全部八張卡、呈現六種部隊及火球／冰霜。
- 雙方各觀察到 22 個士兵移除，六座塔的最終血量一致。
- 隱藏 Player 的數值驗收通過；這份結果不代替可見視窗的人工畫面驗收。

首輪尚未包含 WebGL 與公開部署；這兩項已於後續 [WebGL SDK 部署驗證](WEBGL_SDK_DEPLOYMENT_VALIDATION.md) 完成。

## 重跑與產物

```powershell
./examples/tower-duel/scripts/Test-Client.ps1
./tests/scripts/Test-SdkNative.ps1
./examples/tower-duel/scripts/Test-Server.ps1
./examples/tower-duel/scripts/Test-Players.ps1 -WeakNetwork -ExerciseRecovery
```

本次產物位於 `artifacts/tower-duel/sdk-refactor`：
`client-tests/result.json`、`build-result.json`、`player-test/result.json` 及相關日誌。
雙 Player 測試需先建置包含目前程式的 Windows development Player。
