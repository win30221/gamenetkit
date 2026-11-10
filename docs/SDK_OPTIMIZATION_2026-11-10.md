# SDK 優化實作與驗證（2026-11-10）

依 [檢查報告](SDK_REVIEW_2026-11-10.md) 的七項優先建議完成實作。既有遊戲規則、封包格式、選用模組依賴及單 owner 模擬政策保留。

## 完成項目

1. **有限值契約**：Correction、FixedTimeline、ReplayPrediction 設定拒絕 NaN/Infinity；時間軸 Reset、預測 Reconcile、輸入歷史時間與 Correction.Decay 也防止非有限值污染。固定時間軸 Observe/Advance 忽略非有限觀察，符合 adaptive 模式。
2. **共用廣播快照**：每次 publication 建立一次不可變 world view；各收件者 generation/ACK 與投影仍獨立。AOI/量化先複製會修改的陣列，發布後不回收或修改。不依 tick 快取，保留同 tick 控制事件語意。公開 Snapshot 呼叫仍各自建立獨立資料；內部發布的共用欄位僅供 writer 讀取。
3. **Codec**：先決定 full/delta 再做一次 Base64；64 筆 eviction 改固定 ring。基準仍獨立複製，缺基準與 reset 語意不變。
4. **封包上限**：二分搜尋最少須刪除的事件數，保留原裁切優先級及尾段。以舊實作作 oracle，比對混合事件、Unicode、尺寸與不可裁切資料過大的情境，輸出 bytes 相同。
5. **歷史資料**：Go history 改 ring 與二分搜尋，仍 clone 查詢結果；C# InputHistory 改有序 ring view，保留 240 frame／120 sent 上限、時間窗、缺口及 ack 語意。固定步重播使用單向查找游標。
6. **過載診斷**：loop 新增可選 Scheduled callback，health metrics 增加 dispatchLagNanos、simulationLagNanos、actualTickRate。後者是從 loop 啟動至今的平均迴圈步頻，包含 loading/settlement 時 Step 無遊戲推進的呼叫，不是瞬時 FPS。未加入 catch-up 或跳 tick。
7. **傳輸**：新增 LastFailure 固定原因代碼，不保存例外內容或憑證，維持既有 error JSON。重複 Open 同 attempt 明確拒絕，替換使用 BeginAttempt。Native WT 追蹤讀取 task，在背景讀取結束後 Dispose cancellation source，不阻塞 Unity owner。

另完成 diagnostics 摘要快取：沒有新樣本或過期資料時不重排分位數，窗口仍為「最多 512 筆、最長 10 秒」。

## 效能比較

Windows amd64、Intel i7-12650H，Go benchmark `-benchmem -benchtime=300ms -count=3`。表中為三次中位數，部分四捨五入；微測試不代表整場遊戲或手機效能。

| 測例 | 修改前 | 修改後 | 配置變化 |
| --- | --- | --- | --- |
| 8 接收者 publication，未開 AOI/量化 | 11.20 μs/op | 3.35 μs/op | 19,008 → 2,376 B/op；32 → 4 allocs/op |
| 100 組混合事件超限裁切 | 18.73 ms/op | 0.87 ms/op | 約 6.69 MB → 195 KB/op；約 584 → 30 allocs/op |
| 4096 筆 history 滿載 Add + 中段 At | 2,531 ns/op | 43.43 ns/op | 兩者皆 0 B/op |
| 約 5 KB payload、已確認基準的 delta 編碼 | 64.00 μs/op | 55.88 μs/op | 29,161 → 15,577 B/op；110 → 108 allocs/op |

History/codec 基準使用 HEAD 原始實作放在 `.local/sdk-optimization-baseline`，配合同一份 benchmark。Publication 比較逐收件者 Snapshot 與共用 view；裁切 benchmark 保留舊演算法。

紀錄：`artifacts/sdk-optimization-game-bench.log`、`sdk-optimization-baseline-bench.log`、`sdk-optimization-sdk-bench.log`。

## 原始工作區驗證紀錄

以下為 2026-11-10 在舊 RealtimeFramework 執行的結果；指令已對應到 GameNetKit 現行位置。合併後在 GameNetKit 的驗證另見 [合併紀錄](WORKSPACE_RECONCILIATION_2026-11-10.md)。

- `tests/scripts/Test-GoAndBrowser.ps1`：14 個 Go module 的測試/vet 及 SDK/Shared World JS 測試通過。後加 Go 測例另執行 server 全套、game sim/service 測試與 vet 通過。
- `tests/scripts/Test-SdkNative.ps1`：同一套純 C# SDK 契約通過，含 30/60/144 Hz 預測、非有限設定、ring wrap/eviction、重播缺口、diagnostics cache、跨語言 codec fixtures、replication、actions、clock、playback。
- `tests/scripts/Test-Transport.ps1`：真正 WS、原生 HTTP3 helper、重連替換、雙實例、32 KiB/UTF-8、背壓、溢出、新診斷與重複 Open 通過；JS transport/bridge 22/22 通過。
- `examples/rumblebound/scripts/Test-Battle.ps1`：真實 Go API + C# 客戶端的身份/房間/移動/動作/弱網/重連/結算，以及真實 Go 匯出重播的播放、seek、錄製與容量限制通過。
- `examples/rumblebound/scripts/Test-BattleRemoval.ps1`：十個 Unity 選用套件在隔離樹實體不存在，備援雙人流程通過。
- `tests/scripts/Test-GoModuleRemoval.ps1 -Module all`：五個 Go 選用 package 實體移除後 SDK、game、server 測試及 vet 通過，完成後還原來源。
- Python dependency guard 與 8 項反例測試通過；SDK Unity 測試專案 meta/GUID 檢查零錯誤、零警告。
- `git diff --check` 通過。

初次並行測試有一個 WSS 戰鬥案例達到既有 8 秒期限；未修改期限或斷言，稍後完整 sim/service 重跑通過。原生 UTF-8 測試確認 .NET ClientWebSocket 可能先攔截無效文字，因此診斷可能是 websocket_error，而非後續 decoder 的 invalid_utf8，測例接受這兩個分類。

## 工具修復與平台限制

Battle 測試原指向已刪除的 sample 重播檔，現在每次由現行 Go journal/export 測試產生 fixture，未跳過播放驗證。Dependency guard 保留存在的舊 sample 的 re-export 檢查，但不再要求退休目錄存在；Server-removal 入口也移除不存在的 sample 路徑。

Unity Editor 批次測試卡在 Licensing Client 重連，出現 `com.unity.editor.headless was not found`，因此終止本次啟動的測試 Editor。Native SDK runner 執行相同契約測例，只替代測試程式的 Debug.Log 與 fixture JSON reader；不代表 Unity JsonUtility、IL2CPP、實際 Player 或 WebGL 建置驗收，本輪未宣稱這些平台測試通過。

較大改造仍保留為量測後決策：delta 索引池、history 單物件 copy-out API、spatial grid、二進位協定、native plugin、catch-up 政策及 factory/DI 重組。
