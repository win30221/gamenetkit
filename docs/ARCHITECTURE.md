# Architecture

本文描述目前程式的責任與接線；模組 API 與限制集中於 [MODULES.md](MODULES.md)，啟動步驟見 [GETTING_STARTED.md](GETTING_STARTED.md)。`examples/rumblebound` 是完整對戰入口，遊戲規則與呈現位於其 `game` 目錄。部分歷史說明引用的 `samples/rumblebound` 目前不在此工作區。平台驗證證據集中於 [VALIDATION](VALIDATION.md)。

## Current Architecture

框架提供同步機制，應用提供規則與協定。Go 與 C# 的 DTO、移動步進、地圖、碰撞、攻擊、身份與房間流程均由應用擁有；安裝 SDK 不會自動得到可運行的多人世界。

| 層 | 現有位置 | 責任 |
| --- | --- | --- |
| Unity 契約與基本策略 | `packages/unity/core/Runtime` | 時間軸、預測、修正、時鐘、複製、動作、解碼、量測、弱網與播放契約；輸入歷史、身份 revision、重連計時 |
| Unity I/O | `packages/unity/transport` | 原生 WebSocket、WebGL JS bridge、取消、epoch、文字訊息邊界、有界佇列與抵達時間 |
| Unity 選用機制 | 其他十個 Unity 套件 | 實作 core 契約；不認識英雄、場景或遊戲 DTO |
| Go 執行與 I/O | `server/loop`、`server/transport` | 單 owner 固定間隔迴圈、命令預算、文字 WS 與共用送出政策 |
| Go 契約與選用機制 | `server/netcore` 與五個選用 package | 泛型歷史、查詢時間限制、範圍、差分、journal；不決定遊戲規則 |
| 共用遊戲 domain | `examples/rumblebound/game` | 規則／地圖、身份／房間、權威戰鬥／AI／據點／結果、journal，以及純 C# 幾何／DTO／移動策略；不是 SDK |
| 遊戲呈現 | `examples/rumblebound/game/unity-art` | 原角色／動畫／場景／彈體／範圍施法／特效／音效與素材 |
| 獨立最小應用 | `examples/minimal-server`、`examples/minimal-unity` | 用自己的 input/snapshot 同步一個方塊；Unity 只安裝 core/transport |
| 完整對戰 example | `examples/rumblebound` | GameFlow 擁有 HTTP／身份／房間／恢復；BattleSession 擁有 SDK／輸入／驗證；GameProjection 共用採樣；Unity 使用原版美術及 UI Toolkit 選單／HUD，Canvas 留在獨立診斷入口 |
| 獨立多人教學 | `examples/shared-world` | 保持 core／transport 基礎、只有方塊與完整清單；應用程式不引用 Rumblebound |
| 聚焦組件課程 | `examples/modules` | 逐課引用正式 SDK；以純資料示範加入前後、reset 與停用路徑，不需完整遊戲 |

完整 sample 的 `LabClient` 雖分成多個 partial 檔案，仍共用大量狀態與生命週期。檔案拆分不代表責任拆分已完成。

## Dependencies

```text
Unity 應用 ──→ core
          ├─→ transport（本身沒有 core 套件依賴）
          └─→ 十個選用套件 ──→ core

Go 應用 ──→ loop / transport / netcore
        └─→ history / interest / codec / replay ──→ netcore
        └─→ lagcomp（獨立策略，以 netcore.RewindPolicy 接入）
```

SDK 不依賴 sample。Unity 選用套件彼此沒有套件相依；「平滑需要預測」「近戰補償需要歷史」是 sample 組裝與規則限制，不能誤作所有應用的套件依賴。Go 應用用獨立 `go.mod` 的 `require`/`replace` 引用 SDK；Unity 用 manifest 的本機 `file:` 路徑。

教學與遊戲是兩條獨立應用路徑。Shared World 從重製第 1 階段保留一份小型基礎，後續 Rumblebound 的地圖、戰鬥與身份流程只在整合應用增加；兩者共用 SDK，但不互相 import。各組件課程另外提供最短接線，不把教學選單與大量條件塞入完整遊戲。學習入口見 [examples](../examples/README.md)。

Go 應用 HTTP/API 使用 Gin，SDK 的 loop／transport／同步模組不依賴 Gin。WS、QUIC、Google auth 沿用已有套件；訪客限流使用 `x/time/rate`。套件選擇、HTTP3 介面限制與限流政策集中在 [DEPENDENCIES](DEPENDENCIES.md)。

完整 example 的具體 Unity 型別註冊集中在 `BattleNetcodeModules.cs`，其餘程式透過 core 契約使用；Go 選用註冊在 `examples/rumblebound/game/sim/net_module_*.go`。實際選項在新連線建立時組裝，自動重連保留已選組合。這不是逐物件任意熱切換。

## Ownership

| 狀態 | 單一寫入者與邊界 |
| --- | --- |
| 權威 world、HP、冷卻、結果、歷史 pose | Go 模擬 owner 的 Apply/Step；網路 goroutine 解析、排入有界命令，不直接改 world |
| 連線、取消與抵達佇列 | `MessageTransport`；原生背景收包只入列，Unity owner 用 `TryRead` 取出後套用 |
| 身份與 HTTP 回覆 | 應用身份流程；`SessionState.Revision` 用於拒絕舊身份的非同步完成，SDK 不認證憑證 |
| 已接受的快照與物件清單 | Unity 主迴圈的應用適配器；驗證協定、match、generation、tick 後才交給機制 |
| 預測提交狀態與畫面預覽 | `ReplayPrediction` 的不同 buffer；應用模型只能修改 Step 的指定狀態 |
| 顯示偏移、場景物件與音效 | 應用呈現 owner；修正策略只決定保留/衰減偏移，不擁有 Transform |
| 每接收者範圍與 codec 基準 | 對應連線/seat 的投影與編碼流程；不修改權威 world，也不共用其他接收者基準 |
| 量測與弱網佇列 | 應用選定的單一 owner；其時間不可混用不同單調時鐘原點 |
| 錄製與播放 | 模擬 owner 產生 journal 事件；檔案 writer 將磁碟 I/O 移到背景 worker，owner 仍承擔擷取與 hash 成本；播放使用獨立時鐘與呈現狀態 |

Go `loop.Run` 只在每次 ticker 事件先處理最多 `CommandBudget` 個命令，再 Step 一次；它不替應用建立世界、不保證過載時補跑所有漏掉的 tick。sample 的 ping 回覆可讀 atomic tick，這個讀值不經模擬命令佇列，也不應藉此修改 world。

## Data Flow

目前完整 sample 即時連線的接線順序：

1. 應用取得身份/設定/ticket，固定這次選項；建立連線 epoch、模組實例與 hello。最小範例沒有這套身份/房間流程。
2. Go 接納連線並驗證應用 hello；讀取輸入、驗證身份與序號後，排進模擬命令佇列。
3. 模擬 owner 套用命令、步進遊戲、保存選用歷史，建立各接收者的快照投影。範圍/量化/差分作用於傳輸內容；控制訊息保留可靠路徑。
4. Unity 傳輸記錄抵達時間並排入訊息；WebGL `OnTransportMessage` 只轉交 `Receive`。sample `ClientTransport.Pump`/`TryRead` 接入量測、弱網延遲與 pong 對應。
5. 應用解碼 frame 並驗證外內 envelope，再驗證 protocol/content/match/map/generation/tick，套用權威清單、動作、比分與移動確認。
6. 每幀推進呈現時間軸與本機預測，以應用保存的快照取樣遠端狀態，更新 Transform/特效。輸入按自己的頻率送出，將實際送出的 sequence/time 記入預測器。
7. 結束連線時取消舊 I/O、清除舊佇列及呈現殘留；是否重試由應用錯誤分類決定。

最小伺服器在每個 `/ws` handler 中建立各自的 tick/x/drive 與 `loop.Run`。兩個客戶端是兩個獨立世界，不能用它驗收共用房間。它的 30 Hz 步進、隔 tick 快照與 sample 的規則都只是各應用選擇。

完整 example 的 Gin API 與共用 `service` 負責身份、房間、ticket 和連線接納；共用 `sim` 的單一 owner 執行原遊戲規則，依每條連線選項投影範圍、量化與差分。純 C# `GameFlow` 管理 HTTP／身份／房間，`BattleSession` 驗證快照、接續序號並組裝 SDK，`GameProjection`／`GameMovement` 提供網頁與 Unity 相同的採樣位置。`BattlePresentation` 只管理原角色、場地、動畫與效果。獨立方塊教學留在 `examples/shared-world`，不混入完整遊戲。協定細節見 [PROTOCOL](../examples/rumblebound/PROTOCOL.md)。

選單 11 與根網址現在導向 `/unity/`；簡化 Canvas 明確保留在 `/diagnostics/`。`BattleHud` 以 UI Toolkit 接回原登入／大廳／房間／練習／結算版面，直接呼叫 GameFlow，不依賴原 LabClient。它和 sample 共用同一份主題、字型及按鈕圖；房間選單、剪貼簿接線各自拆成 partial 檔案。遊戲攝影機恢復原版置中的方形視野，介面不再占用左側場地。

## Clock and Sequence

| 值 | 意義與禁止混用之處 |
| --- | --- |
| local monotonic seconds | 預測輸入區間、重連、時間軸、弱網 Pump 的時間；不是 UTC 或伺服器 tick |
| `Incoming.ReceivedMS` | `MessageTransport.ClockMS`（Stopwatch）的毫秒值；表示共用接收層記錄的抵達時間，不能直接和 Unity realtime 的絕對數值相減 |
| server tick | 權威模擬步數；`tickRate` 由應用提供；tick 0 可以有效 |
| estimated server tick | `IServerClock` 的估計；RTT 中點假設存在偏差，失聯外推有界，不是權威步進 |
| render tick | 呈現取樣位置；即時插值不超過最新已收到快照，通常落後權威時間 |
| input sequence / ack | 應用輸入與已套用確認；`PredictionAck.AppliedInputTicks` 由應用把協定語意映射到重播起點 |
| action ID | sample 的動作去重/確認身分，可能取自同一遞增計數器；不等同快照 tick |
| codec frame ID / ack | 某條連線已成功解碼內容的基準編號；不等同 input ack 或 tick |
| generation | sample 連線世代，用來拒絕舊連線訊息；不等同玩家生命 |
| entity ID / life | sample 物件與生命基準；復活/替換會讓舊預測與歷史目標失效 |
| epoch / identity revision | 前者隔離本機 I/O 嘗試；後者隔離身份非同步工作，與伺服器 generation 各自獨立 |
| playback tick | 檔案內容的獨立播放位置，可暫停或跳轉；不回寫即時時鐘 |

sample 的攻擊新鮮度用估計時間，但限制在最新已觀察快照 tick 至其後兩 tick；目標回溯則提交實際呈現 tick，伺服器再依自己的時間窗與歷史資格檢查。自己的移動預測始終以本機輸入時間重播。詳細契約見 [Clock](MODULES.md#clock)、[Prediction](MODULES.md#prediction)、[History and Lag Compensation](MODULES.md#history-and-lag-compensation)。

## Lifecycle and Reset

以下為目前 Rumblebound 適配器行為與 SDK 呼叫邊界，不代表 SDK 自動管理應用所有狀態。

| 邊界 | 現有重設/保留行為 |
| --- | --- |
| 新連線嘗試 | `BeginAttempt` 關閉舊連線並換 epoch；sample 清 pending probe/弱網待處理佇列，重建 advanced 模組、移動適配器、lastTick 與快照基準。弱網尚未結束的 Pause 期限會保留 |
| `welcome` | 接受 playerID/generation，以權威 ack 與待處理動作下界設定 sequence；重建 GameMovement，重設 server clock、replica、時間軸、快照及預測歷史，建立完整清單基準 |
| `start` | loading → playing 的可靠控制邊界；可與 welcome 同為 tick 0，因此不能一律用相同 tick 拒絕。重設 server clock、replica 與時間軸、輸入排程；它不是通用的 `PredictionAck.ResetHistory` 條件 |
| 自動重連 | 保留身份、目標比賽與原選項；新 epoch/伺服器 generation 排除舊完成。可保留等待確認的應用動作並依 welcome ack 接續；不要假設所有 ledger 都無條件清空 |
| 新生命/entity | `GameMovement` 偵測存活恢復或 entity/life 改變，設定預測 `ResetHistory`、直接校正；replica 把 life 改變視為 removed+added，場景適配器重設呈現 |
| 大誤差/攻擊停步 | 本機顯示修正直接採用位置；不代表重置伺服器世界或所有輸入序號 |
| 失敗/手動離線 | 取消傳輸並清暫存、量測與視覺殘留；只有應用允許的錯誤才重試。身份失效另清 SessionState 並增加 revision |
| 比賽結果 | sample 可接受只帶凍結結果的 envelope，不要求含 players/welcome，也可能重複最後 tick/generation；不應套用一般快照去重規則後漏掉結果 |
| 開始播放/seek | 禁止同時維持即時比賽；播放使用 authority 呈現路徑。seek 清動作/特效/物件/快照並從附近幀重建，不重播即時輸入；結束播放恢復原設定並清播放場景 |

codec cache 在新連線清除；缺少基準是要求完整內容的復原路徑，不是身份 reset。伺服器歷史查詢不外推、不搬動 live world。錄製與播放的完整生命週期見 [REPLAY.md](REPLAY.md)。

## Known Boundaries

- SDK 的 `transport.js` 原樣收送文字，不解析遊戲 ping/pong。完整遊戲的 probe、身份匹配、世代與 RTT 量測由應用層 ClientTransport 負責。
- sample 的 Node 入口只 re-export SDK；HTTP `/transport.js` 直接提供同一份 SDK 原檔。sample handler 在建立時從工作目錄向上尋找框架來源，因此需從框架目錄樹內啟動，且部署時保留 SDK 檔案；不能只帶 Go 執行檔就期待這個端點可用。
- WebGL `.jslib` 以獨立 handle 管理每個 transport，支援多連線與相對 runtime 路徑；保留舊 ABI 作相容入口。32 KiB UTF-8 邊界、釋放、替換與子路徑有 SDK 測試；實際 WebGL 已驗證雙用戶端、WS、原生 WT 與中斷重連。
- 時間軸只計算 render tick，不代替遊戲保存快照、做 DTO 插值或建立場景物件。固定步長預測也只重播應用模型，沒有完整多人世界 rollback。

## Rebuild Target

[examples/rumblebound](../examples/rumblebound/README.md) 已重新組裝完整身份／房間／載入／戰鬥／練習／結算與錄製播放；重製期間的重複方塊入口與舊場景已移除。Windows／WebGL 建置、真正 Player 與瀏覽器流程各有驗收。歷史紀錄保存在 `docs/archive`；證據與平台界線集中於 [VALIDATION](VALIDATION.md)。
