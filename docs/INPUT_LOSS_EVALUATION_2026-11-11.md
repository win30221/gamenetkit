# 輸入丟包與歷史冗餘對照（2026-11-11）

## 決策與範圍

本次評估「同一個輸入封包附帶前兩筆輸入」是否值得加入。目前對照沒有觀察到改善，因此不加入正式協定或 SDK。同步預算不在本次範圍。

這個結論限定於目前 Rumblebound 的 held-state 模擬與本次候選方式，並不是說所有遊戲或所有冗餘方式都沒有用途。獨立多發封包、輸入緩衝、按輸入時間重播世界，都不是此次測試的功能；它們會改變流量、延遲或模擬語意，不能當作免費的歷史補送。

## 為什麼可能沒有收益

- [BattleSession](../examples/rumblebound/client/Assets/Scripts/Battle/BattleSession.cs) 持續送出目前方向：一般為 30 Hz，方向變化最多 60 Hz。動作佇列的第一筆會使用相同 action ID 持續送到收到 actionAck，每次送出更新 actionTick。
- [World.Input](../examples/rumblebound/game/sim/world.go) 用遞增 seq 接受輸入並取代 DX/DZ；移動在 Step 才執行。輸入不是「收到一筆就跑一次移動」。
- [Engine](../examples/rumblebound/game/sim/engine.go) 在 Step 前處理排隊命令。同一包依序套用兩筆歷史與目前方向，歷史方向會在移動前被目前方向覆蓋。
- 一次性動作另有 ActionAck 與 Pending 去重。歷史輸入不是現有動作重試的替代品。

因此判定標準是先觀察操作結果有沒有改善，而非只看補回多少序號。若位置、方向、停止恢復與動作結果都相同，就不因為多收到了歷史資料而加入功能。

## 測試方法

新增兩個僅參與測試的檔案，沒有修改執行期程式：

- [input_loss_comparison_test.go](../examples/rumblebound/game/sim/input_loss_comparison_test.go)：移動 A/B、無丟包參考與單次轉換。
- [input_loss_actions_test.go](../examples/rumblebound/game/sim/input_loss_actions_test.go)：真實戰鬥 World 的動作重試與快照確認。

A 是目前每包一筆輸入，B 是同包攜帶前兩筆與目前輸入，舊到新套用。兩者使用相同發送時點及整包丟棄／抵達時序；沒有讓歷史資料獲得額外的獨立送達機會，也沒有偷偷提高 tick 或發包頻率。

移動使用真實 World.Input／Step，在空場地擴大邊界，避免碰牆掩蓋累積誤差。發送模型為 60 Hz 檢查、30 Hz 心跳、變向最多 60 Hz。每段 10 秒，另留 0.5 秒排空與逾時尾段；上下游未串接真實 socket。

測試持續移動後放開、兩幀短按、左右變向、斜向轉彎；比較兩種發送／模擬相位。每個隨機情境使用 32 個固定種子，非隨機情境只跑一個種子，不用重複相同時序膨脹案例數。

丟包與抖動預先產生為固定時序，兩組共用；不依賴「不同送法套同一個隨機 seed」來假設公平。亂序情境明確允許封包交錯，並加入延遲的網路重複包。

## 結果

最終移動矩陣為 **1,320 組 A/B、415,800 對 tick 樣本**。每對樣本的位置 X/Z、持續方向 DX/DZ 與 ACK 完全相同。

| 網路條件 | A/B 組數 | 加入歷史後的移動改善 |
| --- | ---: | --- |
| 無丟包、單向 50 ms | 8 | 無差異 |
| 隨機丟包 1%、5%、10%、20% | 每種 256 | 全部無差異 |
| 連續丟包 3、7、8、9 個 tick | 每種 8 | 全部無差異 |
| 10% 丟包、額外 0–100 ms 抖動、亂序及重複 | 256 | 無差異 |

同時以「相同延遲／抖動但不丟包」重跑作參考。每個丟包條件都確實造成方向偏差，排除測試沒有真正打到輸入的情況。完整輸出包含平均／最大權威位置偏差與方向不符比例；這是權威軌跡誤差，不是 Unity 畫面的校正距離。

三個單次轉換測例另外確認：

- 遺失按下：兩組都等下一筆目前輸入恢復，多延遲一個 tick（約 33.3 ms）。
- 遺失放開：兩組都多移動一個 tick，本例多走 0.2 單位。
- 遺失一 tick 的短按、下一包為放開：兩組都無法恢復這段短按；把舊方向套用後立即改為零，不會產生移動。

動作對照使用正式地圖與真實投射物／傷害流程，讀取實際發布的快照後模擬 ACK 丟失／延遲：

| 條件 | A 與 B 的執行延遲（tick） | A 與 B 收到 ACK（tick） |
| --- | ---: | ---: |
| 正常 | 1 | 1 |
| 第一個動作包遺失 | 2 | 2 |
| 兩個 ACK 快照遺失，再延遲 5 tick | 1 | 9 |
| 連續丟失 7／8／9 tick | 8／9／10 | 8／9／10 |
| 重複封包 | 1 | 1 |
| 亂序封包 | 2 | 2 |

八種動作條件都只生成一發投射物、造成一次 25 傷害，執行／確認延遲與重試次數相同。ACK 遺失案例確實送了九次同 ID 動作，沒有重複執行。

測試也計算送端 JSON payload（含途中遺失的包），本次候選約為原版的 3.129 倍。候選以完整歷史 DTO 的測試 envelope 編碼；它不是正式 Unity 編碼，也不是最佳化後的最小冗餘格式，不能當作正式頻寬估算。取捨依據是零操作改善，不是這個特定編碼的成本。

## 重現與驗證邊界

從儲存庫根目錄的 PowerShell 執行：

```powershell
# 使用工作區快取，避免系統預設快取的寫入權限影響測試。
$env:GOCACHE = Join-Path (Get-Location) '.cache/gobuild'
. ./tools/internal/Go-Env.ps1
& $Go -C examples/rumblebound/game test ./sim -run '^TestInputLoss' -count=1 -v
& $Go -C examples/rumblebound/game test ./... -count=1
& $Go -C examples/rumblebound/game vet ./...
```

這些測例也會由既有 `tests/scripts/Test-GoAndBrowser.ps1` 的 game module 測試收錄。

本次實際驗證：Go 1.27.3、全部新增對照通過；game module 全套測試重跑通過、`go vet ./...` 通過、兩個新 Go 檔案 gofmt 檢查通過、`git diff --check` 通過。初次全套測試有既有 `TestNegotiatedDeltaAckLossAndFullRecovery` 的封包大小斷言失敗（439 bytes 對 415 bytes）；未修改該測試或壓縮程式，單獨連跑十次及完整回歸重跑都通過。這筆首次失敗保留為間歇性失敗紀錄，未宣稱已查明或修復其根因。

本機原始輸出保存在 `artifacts/input-loss-comparison-2026-11-11.log`、`input-loss-game-regression-2026-11-11.log`、`input-loss-codec-recheck-2026-11-11.log`、`input-loss-game-recheck-2026-11-11.log`；artifacts 不隨 Git 提供，以上文件保留決策與可重現命令。

本次為可重現的伺服器模擬層實驗。C# 發送節奏與動作重送依來源建模，沒有執行 Unity Player、真實 WebTransport 網路、擁塞／MTU、輸入命令佇列過載或客戶端預測校正。沒有宣稱端到端手感量測或平台驗收。正式 protocol.Decode 仍不接受候選 envelope；測試直接把歷史項目交給 World。

若未來改成每筆輸入代表一段待執行時間、增加伺服器輸入緩衝，或不再持續重送待確認動作，應重新評估；目前的逐 tick 一致性斷言會提示原結論可能失效。
