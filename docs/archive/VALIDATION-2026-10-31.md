# 第一版驗證紀錄

> 歷史證據：本文保存 2026-10-30～31 第一版實作各階段的驗證紀錄。文中的「本輪」「最新」「已啟動」均指當時狀態，不代表目前服務仍在線或本次文件整理重新跑過全部建置。日誌與建置產物可能被後續執行覆寫；目前命令與驗證範圍見 [VALIDATION](../VALIDATION.md)。文中工具與 artifacts 路徑仍相對於 RealtimeFramework 根目錄。

日期：2026-10-30。所有工作位於獨立 `RealtimeFramework` 資料夾；既有專案的 598 個程式、設定、字型與 metadata 檔案經 SHA-256 比對未變動。新設計文件移入此資料夾，不屬於既有遊戲的修改。證據：`artifacts/original-project-integrity.json`。

2026-10-31 新增伺服器輸入錄製與離線狀態一致性重播；本輪沒有修改 Unity 或線上協定格式。598 檔案完整性檢查再次通過。操作與儲存範圍見 [REPLAY.md](../REPLAY.md)。

2026-10-31 後續新增 Unity 播放套件與示範 UI：Go 輸出通過一致性驗證的快照，Unity 提供暫停、倍速、逐格、進度條與離開／重新載入。原有即時線上協定格式不變；原始 598 檔案再次確認未變動。

## 已驗證項目

| 項目 | 結果 | 證據 |
| --- | --- | --- |
| 獨立 Go SDK | loop 預算／排序／停止及傳輸限制／取消測試通過，vet 通過 | `tools/Test.ps1`、`server/*/*_test.go` |
| 獨立傳輸 SDK browser runtime | 13 項：12 項 WS／WT，加實際 jslib 共用接收入口、送出錯誤與舊連線隔離測試 | `packages/unity/transport/Browser/Tests/*.test.mjs` |
| 獨立量測／弱網 SDK | 分位數、期限、容量、重設、內容速率、幀樣本；雙向延遲、選擇性遺失、固定種子抖動、FIFO、訊息／佇列上限、斷流與跨連線隔離通過 | `RF_NETWORK_TOOLS_SDK PASS`，`artifacts/sdk-tests.log` |
| 獨立預測／修正 SDK | 6 組幀時間／固定步長 × 30／60／144 FPS，每組持續與反向輸入、確認重播、權威資料不變、預覽隔離、凍結／重設及修正政策通過 | `artifacts/sdk-tests.log`、`tests/sdk-unity` |
| 新增七項 SDK | 單調／有界時間、原子清單與生命世代、動作 ledger；5 份 Go／C# codec 資料、缺基準恢復與無效資料拒絕通過 | `RF_SEVEN_SDK PASS`，`artifacts/sdk-tests.log` |
| 歷史與近戰補償 | 過舊／未來、不同生命／世代、當下保護拒絕；有界歷史命中且不搬動 live world；投影量化與 AOI 不變更 domain state | `samples/rumblebound/internal/sim/net_modules_test.go` |
| 真實 wire 差分 | 真正 WSS 確認基準、遺失中間 frame 不破壞基準、ack 0 完整恢復；實際 envelope 小於對照 JSON | `samples/rumblebound/internal/gateway/net_wire_test.go` |
| 獨立錄製 SDK | 版本／順序／checksum／footer、可變資料隔離、並行 Close、檔案與記錄預算、佇列滿、寫入失敗、不覆蓋既有檔案與 partial 拒絕通過 | `server/replay/journal_test.go`、`artifacts/replay-tests.log` |
| 伺服器模擬重播 | 一般連線／私人對戰／練習 AI／範圍技能四個情境，各 400 以上 step；含重複輸入、衝刺、模式切換、斷線重連、逾時淘汰與命中歷史；可指出首個不同事件並拒絕不同規則 | `samples/rumblebound/internal/sim/replay_test.go` |
| 戰鬥生命週期重播 | ranger 箭矢與 mage 範圍技能各 700 step，明確確認產生攻擊、造成傷害、死亡與復活，再逐事件重播比對通過 | `TestReplayProjectileAndGroundDamageDeathAndRespawn`、`artifacts/replay-sim-tests.log` |
| 真實連線錄製 | WSS 收入與 HTTP 建立的練習場都繼承錄製 context，正常關閉完成檔案並成功重播 | `samples/rumblebound/internal/gateway/replay_test.go` |
| 獨立程序重播 | 最後 5 秒真實排程 engine：304 事件、150 step；另一個 Go 程序重建後狀態逐筆一致。修改輸入的副本回報 sequence 5／tick 1 首次差異，CLI 回傳失敗 | `artifacts/replay-example.json`、`replay-verify.json`、`replay-divergence.json` |
| Unity 播放 SDK | tick 清單複製／原子載入、稀疏索引、暫停、獨立倍速、前後跳轉、端點／重啟、無效資料及重設通過；不引用 Unity 或遊戲 DTO | `RF_PLAYBACK_SDK PASS`，`artifacts/sdk-tests.log` |
| 播放檔匯出 | 原始 mapJSON hash、完整清單不受 AOI 影響、玩家逾時後保留空清單、損毀 journal 拒絕及不覆蓋目的檔通過 | `artifacts/playback-go-tests.log` |
| Unity 播放資料 | 真正 Go 匯出檔在 C# 驗證設定／protocol／地圖 hash／frame tick；錯誤資料拒絕 | `RF_PLAYBACK_CODEC PASS`，Unity 建置日誌 |
| 原生播放操作 | 150 份快照；暫停不重播事件、進度條跳轉、逐格、位置／HP／生命世代、2 倍速、範圍技能時鐘暫停、播完重啟、離開／再載入、禁止線上輸入及舊網路回呼通過 | `artifacts/playback-smoke.log` |
| 播放 UI | 1280×720 與 390×844，進度條可見、控制列換行、血量與比分、倍速顯示已檢查 | `artifacts/playback-ui/playback-*.png` |
| 七項一起啟用 | 原生 WS + 額外延遲、抖動與應用訊息遺失；起手立即觸發且確認不重播、拒絕、AOI 進出、基準遺失恢復與身份重連通過 | `samples/rumblebound/artifacts/seven-modules-smoke.log` |
| 獨立選項組合 | 30 個有效組合建立實際組件並驗證行為；6 個「無預測＋平滑校正」組合被拒絕 | `RF_FRAMEWORK PASS`，完整示範建置日誌 |
| 固定步長跨語言 | 自由移動、掩體停止／滑動、推擠；30／60／144 FPS 共 90 個 Go／C# 座標比對通過，容差 1e-7 | `RF_FIXED_PARITY PASS`、`samples/rumblebound/shared/prediction-cases.json` |
| 重組後 Go 應用 | 全部既有 Go 測試與 vet 通過 | `artifacts/tests.log`、`samples/rumblebound/artifacts/framework-go-tests.log` |
| 保留的網頁診斷工具 | 38 項測試通過 | `artifacts/tests.log` |
| 框架依賴方向 | SDK 不引用遊戲型別，最小範例不安裝十個選用套件 | `tools/Check-Dependencies.py` |
| 最小範例 | Windows／WebGL 建置通過；原生 Go＋Unity 真實雙向連線通過 | `artifacts/minimal-Windows.log`、`minimal-Web.log`、`minimal-smoke.log` |
| 可選套件實體移除 | 前九個套件有既有個別移除證據；新增 playback 個別移除與目前全部十個移除均通過 | `artifacts/without-playback.log`、`without-all.log`、其餘 `without-*.log` |
| 全部移除後 binary | 僅含 Realtime.Core.dll、Realtime.Transport.dll，沒有十個選用組件 DLL | `artifacts/without-all-assemblies.json`、`samples/rumblebound/client/Builds/NoOptionalNetcode/Rumblebound_Data/Managed` |
| Go 模組移除 | history／lagcomp／interest／codec 分別與全部實體移除後，SDK／遊戲測試及 vet 通過 | `artifacts/server-without-{history,lagcomp,interest,codec,all}.log` |
| 量測與弱網互相獨立 | 移除量測時保留弱網 DLL，移除弱網時保留量測 DLL；兩者建置均通過 | `artifacts/without-diagnostics-assemblies.json`、`without-simulation-assemblies.json` |
| 真實弱網回歸 | 基準與雙向延遲量測通過；有實際應用訊息遺失，三秒暫停後自動恢復原身份 | `samples/rumblebound/artifacts/network-tools-smoke.log`、`network-tools-result.json` |
| 完整示範 | Windows／WebGL 建置與遊戲 gate 通過 | `samples/rumblebound/artifacts/unity-Windows.log`、`unity-Web.log` |
| 真實遊戲回歸 | 幀時間與固定步長兩種模式：UI 獨立選項／保存／相依禁用、雙客戶端重連、私人對戰、推擠／掩體與練習 AI 通過 | `samples/rumblebound/artifacts/frame-ui-smoke.log`、`frame-recovery.log`、`unity-ui-smoke.log`、`unity-private-*.log`、`unity-practice-smoke-*.log` |
| 呈現回歸 | 插值 36 組對照、角色接觸、復活、彈體終點與去重通過 | 完整示範的 Unity 建置日誌 |
| UI 視覺 | 桌面／窄視窗的七項技術選單、相依禁用、弱網數值欄位與詳細統計浮層已檢查 | `samples/rumblebound/artifacts/seven-modules-ui`、`network-tools-ui` |
| Unity metadata | 655 檔案，0 errors、0 warnings | `artifacts/unity-project-check.log` |

## 組件移除驗證怎麼做

`Test-OptionalRemoval.ps1` 接受十個選用套件名稱或 all；真正移走目錄、移除 manifest 依賴與唯一組裝檔的註冊，再執行建置。各入口先確認策略未註冊，再執行適用的回歸 gate，並確認產物沒有對應 DLL。結束時以原始位元組還原來源。

`Test-ServerRemoval.ps1` 則真正移走 Go 套件，用 `rf_no_history`、`rf_no_lagcomp`、`rf_no_interest`、`rf_no_codec`、`rf_no_replay` 對應排除標籤編譯應用組裝，再執行 SDK 與遊戲測試／vet。2026-10-31 的 all 包含目前五個選用 Go 套件；新增 replay 個別移除及全部移除證據分別在 `artifacts/server-without-replay.log` 與 `server-without-all.log`。兩種移除測試都不可與依賴相同套件的建置同時執行。

這項驗證與單純將 UI 開關設為關閉不同。全部移除的輸出為 NoOptionalNetcode，插值單獨移除為 NoInterpolation，其餘為 WithoutSelectedNetcode。全部移除後只有 authority／latest／snap，重連仍可獨立選擇；未安裝模組的專屬測試會跳過，其餘選項組合會重新驗證。

## 固定步長比對資料

`samples/rumblebound/cmd/predictionfixtures` 使用真正的 Go 碰撞程式產生 JSON 座標。修改碰撞規則時，先從該範例根目錄執行 `go run ./cmd/predictionfixtures` 更新資料，再執行 Unity 建置的 `FixedPredictionVerification`。不同渲染 FPS 會比對到相同 Go tick；不足 tick 的預覽另由純 SDK 測試檢查不回寫正式狀態。

## 網路量測與弱網實測

`samples/rumblebound/scripts/Test-Clients.ps1 -UI -PrivateMatch -Practice -NetworkTools` 使用隔離的本機 Go 伺服器與真正的原生 WS Player。先取得至少三筆基準回音，再重新連線啟用每方向 50 ms 額外延遲、±20 ms 抖動與 10% 應用訊息遺失。檢查量測樣本、內容流量、實際遺失、延遲增加，再暫停雙向訊息三秒，驗證自動恢復原 player／entity 與清除舊佇列。

結果由 Player 輸出並保存於 `samples/rumblebound/artifacts/network-tools-result.json`；這是少量回音的功能驗證，不是長期 p95 或真實 TCP 封包遺失效能報告。UI 顯示每項樣本數。實際 WS 仍維持作業系統的可靠傳輸，遺失由應用層輸入／快照篩選產生。

本輪最後原生短測各三筆回音：基準平均 0.40 ms，加入條件後平均 122.18 ms、樣本 p95 133.25 ms；送出遺失 4 份、接收遺失 3 份，暫停後恢復成功。這些少量數值驗證設定生效，沒有宣稱改善線路 RTT。最新資料保存於 JSON 檔。

`NetworkAdapterVerification` 另檢查送往嚴格 Go schema 的 ping 不含客戶端量測欄位、瀏覽器已有的回音時間會在共用接收邊界被替換，且 JSON 沒有重複欄位。jslib 測試直接載入實際 bridge，僅將網站 `/transport.js` 路徑映射到 SDK runtime；不宣稱這等同完整瀏覽器 WebGL 遊玩驗收。

新回歸另外確認 pong 欄位換序仍能量測、保留私人對戰 mapHash 與伺服器時鐘 metadata，以及時鐘估計超前時攻擊時間戳不超出已觀察 tick 加二的安全上限。最後七項原生測試：76 個編碼 frame、3 次缺基準事件、物件 added 5／removed 1、2 次預測起手，重連到 generation 2；`rewindUsed` 確認真的取得歷史目標。測試第一下使用明確的窗口內回溯時間，不宣稱自適應緩衝下的每一擊都一定落在補償窗口。

## 驗證範圍

最小範例與完整 Rumblebound 的真實連線驗證使用原生 Windows Player；WebGL 已通過 IL2CPP 建置與 bridge 連結，但本輪沒有宣稱在真實瀏覽器完成手動遊玩驗收。圖形預覽與合成封包重播不代表實際裝置 FPS、跨國網路或完整戰鬥容量測試。

框架包含通用傳輸、狀態生命週期、重連、預測／重播、校正、插值、量測／弱網，以及新增七項同步能力。固定預測維持 30 Hz，但不是完整世界確定性證明；歷史保存碰撞 pose，補償只回溯近戰目標；起手預測不決定命中或傷害。詳細契約與限制見 [MODULES.md](../MODULES.md)。遊戲模擬、認證、房間與戰鬥幾何保留在適配器。

本機完整示範已啟動於 `http://127.0.0.1:18090/unity/`，HTTP 頁面與 SDK browser runtime 均回應 200；原專案的 8080 伺服器也仍回應 200，未停止或替換。啟動輸出位於 `artifacts/demo.stdout.log`、`demo.stderr.log`。之後可用 `tools/Start-Demo.ps1` 重新啟動。

最後更新：Windows／WebGL 均完成最終建置；18090 上的 loader、framework JS、WASM、data 與 transport.js 全部 HTTP 200，後端公告七項相關能力。檢查紀錄：`artifacts/demo-verification.json`。已有頁面請 Ctrl＋F5 重新載入最新 WebGL。

Unity 重播更新的最終 Windows／WebGL 建置通過；頁面、loader／framework／WASM／data 與 150 份快照的 StreamingAssets 範例全部 HTTP 200。原生播放器完成操作驗證，WebGL 本輪仍只宣稱建置與資源檢查，未手動瀏覽器遊玩驗收。最新證據：`artifacts/playback-demo-verification.json`。
