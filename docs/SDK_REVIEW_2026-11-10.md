# SDK、使用方式與架構檢查（2026-11-10）

後續實作及驗證見 [SDK 優化紀錄](SDK_OPTIMIZATION_2026-11-10.md)。以下保留檢查階段的原始發現。

本次為來源檢查，未修改 SDK 或遊戲行為。範圍為 12 個 Unity 套件、Go SDK 各 package、原生傳輸 adapter，以及 Rumblebound 的主要組裝與同步路徑。效能建議根據實際程式路徑提出，尚未量測改善百分比；不是所有範例、平台或第三方套件版本的完整稽核。

## 結論

保留現有分層：純資料契約、選用機制、傳輸、應用協定及 Unity 呈現各有邊界。單一模擬 owner、有界佇列、epoch/generation 隔離、可移除模組與跨語言 codec fixture 都值得維持。優先處理參數契約與同步熱路徑，再考慮較大的架構調整。

## 建議優先順序

### 1. 統一浮點參數的有限值驗證（正確性，優先）

- `packages/unity/correction/Runtime/ExponentialCorrection.cs:12` 只驗證正數，NaN 會通過；`seconds=NaN` 使 Decay 傳出 NaN。
- `packages/unity/interpolation/Runtime/FixedTimeline.cs:18` 的 intervals 同樣接受 NaN，Reset 後 RenderTick 會變成 NaN。
- `packages/unity/prediction/Runtime/ReplayPrediction.cs:25` 接受 NaN timeout，Advance 的期限比較永遠不成立。
- 建議公開 constructor 與時間入口採一致的 finite/range 規則。應用已有設定驗證，但獨立 SDK 消費者不一定走該路徑。加入 NaN、正負無限大與邊界值測例，不把每次 hot path 都重複驗證完整設定。

### 2. 同一次廣播共用不可變世界快照（擴充性）

`examples/rumblebound/game/sim/snapshot.go:8` 每次 Snapshot 都建立玩家、投射物、事件、比分等資料並排序玩家；publishUpdates 對每個接收者再次呼叫。成本隨接收者數與世界資料量相乘。

建議每次 publication 建立一次 immutable world view，再做各接收者的 ACK、generation、AOI 與量化投影。注意 `sim/net_modules.go` 目前用 `slice[:0]` 原地過濾並修改量化欄位，因此不能直接把現有 slice 共用；投影必須持有獨立輸出，且不能在非同步 writer 使用完前回收。不要只依 tick 快取：同 tick 的 start、result 或設定變更也可能改變內容。

### 3. 減少 codec 的無效配置（低風險效能改善）

`server/codec/delta.go:27` 先對完整 payload 做 Base64，選中 delta 後第 33 行又做一次並丟棄前者；delta 每次另建 `map[uint64][]int` 索引。

先決定 full/delta，再僅編碼勝出內容。接著以 benchmark 評估重用索引、固定候選陣列與 ring eviction。64 個、每個最多 16 KiB 的基準，單條連線單側 raw cache 上限約 1 MiB，另有容器與暫存成本；容量應以 RTT、快照率與可接受的 full-frame 恢復頻率評估。保留基準不可變與缺基準恢復語意。

### 4. 封包裁切避免逐事件重做完整 JSON（尖峰效能）

`examples/rumblebound/game/service/net_wire.go:13` 的 boundedSnapshot 每刪除一筆可選事件，就重新 Marshal 整份快照。事件密集又剛好超限時，工作量會隨刪除筆數放大。

可在保留既有事件優先級下，分批裁切或搜尋可保留的尾段長度，最後再做確切 byte-size 檢查。維持玩家、有效投射物、控制與結果訊息不被裁切的契約，加入上限附近與多種事件混合的 benchmark。

### 5. 歷史資料改用環形儲存與順序查找游標（中優先）

- `server/history/history.go:32` 滿容量後每次 Add 搬移所有 entry；At 線性查找且複製 Before/After。
- `packages/unity/core/Runtime/InputHistory.cs:24` 用 List 刪頭部資料；HeldAt 每次從頭掃描。ReplayPrediction 每個重播步都呼叫它。
- Go history 改為 ring，時間查詢使用二分搜尋；C# 保留對外有序 view，但重播使用單向游標。不要移除 clone 以換速度，否則會破壞歷史不可變契約。
- Rumblebound 的 historicalPosition 只讀單一玩家，卻由 At 取得兩個完整 pose map clone；可評估額外的 selector/copy-out API，避免暴露內部可變 map。
- 現有容量有界，小房間未必需要立刻改，先量測重播與回溯查詢成本。

### 6. 補上模擬落後時間與過載政策（架構決策）

`server/loop/loop.go:38` 每次 ticker 通知只 Step 一次；Measured 只量測本輪命令及 Step 執行時間，沒有量測排程延遲。這是文件已有描述的政策，不是未揭露的 bug。

Go ticker 可能丟棄慢接收者的 tick，見 [Go 官方 NewTicker 文件](https://pkg.go.dev/time#NewTicker)。持續過載時，模擬時間會落後牆鐘時間。先新增 schedule lag、實際 tick rate 與落後量指標，再由產品需求決定維持慢模擬、有限 catch-up 或終止過載房間。不要直接無限補跑，也不要僅把 tick 跳號而略過遊戲模擬。

### 7. 傳輸診斷與生命週期契約（維護性）

`MessageTransport.cs:119,164` 將各種例外折成 connection_closed/send_failed，難以區分取消、逾時、UTF-8/framing、DNS/TLS 與遠端關閉。保留現有對外錯誤代碼，新增結構化本地診斷原因；不要記錄 hello/ticket 等敏感內容。

Open/Close/Dispose 應明確宣告單 owner 呼叫要求，並拒絕同 attempt 重複 Open 或先結束前次資源。目前收件鎖不代表整個 public API 可任意多執行緒呼叫。NativeWebTransport 的背景讀取 task 與 CancellationTokenSource 可進一步集中追蹤及收尾；避免在 Unity 主執行緒同步等待 I/O。這是 API 強化建議，本次未重現資源洩漏或 race。

## 各 SDK 檢查摘要

| 模組 | 判斷與建議 |
| --- | --- |
| Unity core | SnapshotBuffer 已是 ring；InputHistory 可改 ring/游標。保留 DTO clone/驗證由應用負責的邊界。 |
| Unity transport | 有訊息大小、收件容量、epoch 與發送背壓；改善原因診斷及 owner/lifetime 契約。 |
| Unity interpolation | adaptive 有單調性、飢餓與恢復處理；固定模式補有限值驗證。64 筆掃描暫非首要瓶頸。 |
| Unity prediction | committed/rendered 分離與歷史缺口檢查值得維持；優化查找，補 timeout 驗證。 |
| Unity correction | 演算法簡單；補 finite 驗證即可，無須抽出更複雜策略框架。 |
| Unity clock | 已有 finite、RTT 限制及外推上限；維持獨立單調時鐘語意。 |
| Unity replication | staging 及穩定 dictionary view 已有重用；每筆 clone 成本需量測，不能直接取消複製。 |
| Unity actions | 有容量、逾時、保留與去重；128 筆掃描先保留。應用的動作在途政策與 SDK 顯示帳本分開處理。 |
| Unity codec | patch scratch 已重用；baseline bytes/string 仍有配置。先優化 Go encoder，再考慮二進位協定的較大變更。 |
| Unity diagnostics | 每次 Read 排序最多 512 筆；可依新樣本與過期情況快取或降低 HUD 查詢率。10 秒只是時間上限：144 FPS 時 512 個 frame 樣本約覆蓋 3.56 秒，應在指標契約說清楚。 |
| Unity simulation | 有界、可重現亂數與可靠控制不靜默丟棄，保留；字串/UTF-8 檢查成本有量測需求再改。 |
| Unity playback | Load 驗證、tick 二分搜尋與時鐘隔離清楚，暫無需改架構。 |
| Go loop | 保留單 owner；補排程落後指標與明確過載政策。 |
| Go transport | 保留 Wire abstraction、write deadline 與短寫檢查；WriteLine 的封包複製可在 profile 證實後優化。 |
| Go netcore | 泛型契約沒有遊戲相依，維持精簡；新增 API 時明示 ownership/lifetime。 |
| Go history | ring + 二分搜尋；單物件 copy-out 查詢可減少整個 world clone。 |
| Go lagcomp | 時間窗檢查保持獨立，命中/生命/傷害政策留在應用；本輪未見需要擴大 SDK 責任的理由。 |
| Go interest | 每接收者線性掃描，超量才排序。大量物件時再評估共用 spatial grid；小世界先保留簡單實作。 |
| Go codec | 先消除重複 Base64，再 benchmark delta 索引及 cache 容量。 |
| Go replay | 背景 writer、有界佇列、partial/footer 與不覆寫發布已完整；Append 仍在 caller 做 JSON，量測 owner CPU 後才移動序列化責任，避免引入可變 payload 競態。 |

## 第三方依賴與組裝

目前來源使用 Gin、coder/websocket、quic-go/webtransport-go、Google auth、x/time/rate 及 Unity Newtonsoft。檢查到 Gin 僅在應用層、Origin 在接受 WS 前驗證、WS 有 read limit、Google validator 作為實例保留、Unity JSON 關閉 TypeNameHandling。這些接線沒有顯示需要更換 SDK 的理由。

- 保留 QUIC/HTTP3 adapter 邊界；不要讓 Gin writer 包裝侵入需要 HTTP3 專用介面的 Upgrade。
- 保留 Native WT helper 作可選 adapter；改成 native plugin 的打包與 ABI 成本，需要 IPC profile 才能判斷是否值得。
- 第三方版本管理建議加入各 go.mod 的相依盤點、相容性測試與漏洞掃描。本次沒有執行 govulncheck，也沒有宣稱現有版本最新或無漏洞。
- Go 的 init/global factory 可日後改為明確傳入 Modules 設定，讓不同 engine 的配置與測試互不影響；保留 optional-removal 測試，無須引入大型 DI 容器。
- 部分 C# SDK 多個操作壓在單行，建議獨立格式整理，讓錯誤路徑與狀態變更可審查。與效能修改分開，避免 diff 難以驗證。
- 架構及驗證文件仍有不存在的 samples/rumblebound 路徑與「保留原 sample」描述，應區分歷史證據與現行可執行入口。

## 本輪驗證及限制

- `server`: `go test -count=1 ./...` 全部通過；`go vet ./...` 通過。
- Browser transport/bridge: `node --test packages/unity/transport/Browser/Tests/*.test.mjs`，22/22 通過。
- `Check-Dependencies.py` 未執行成功：目前 shell 找不到 python。
- 未重新執行 Unity Editor/Player、全部遊戲整合、native WT 平台測試或效能壓測；既有文件的歷史結果不當成本輪結果。

後續建議先建立代表性 payload 的 codec/boundedSnapshot benchmark，及多接收者 publication、history 查詢、Unity GC/frame、重連循環測試。記錄 ns/op、B/op、allocs/op、tick P95/P99、GC 配置與實際線上 bytes，再依結果決定第二階段優化。
