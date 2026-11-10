# 階段驗證歷史（截至 2026-11-01 收斂前）

此處保存當時的實作階段、舊命令與環境結論。部分過渡入口已移除，日誌可能已由後續建置更新；目前可執行命令與證據範圍請看 [VALIDATION](../VALIDATION.md)。

# 驗證方式與證據範圍

以下命令均從 **RealtimeFramework 根目錄**執行。必要環境與建置步驟見 [GETTING_STARTED](../GETTING_STARTED.md)。測試範圍需與要驗證的變更一致；已保存的日誌或 Player 不代表目前原始碼已重新建置。

## 範例選單入口

`Start.cmd` 尋找 PowerShell 7 並開啟根目錄 `Start.ps1`；無參數顯示編號選單，`-List` 返回目前目錄而不啟動服務。教學排在最前，後接整合示範與進階參考；目錄順序自動產生連續編號及範圍提示，新增教學不需維護多份數字。原本指定 `-Example` 名稱的入口仍可使用。選單編號、名稱唯一性、分組順序、輸入 0 退出與 PowerShell 語法驗證見 `artifacts/example-menu-order.json`；最新教學順序以 [examples 索引](../../examples/README.md) 為準。沒有在驗證中啟動或停止使用者的對戰服務。

## 不需 Unity Editor 的檢查

```powershell
python ./tools/Check-Dependencies.py
python -B -m unittest discover -s ./tools/tests -v
./tools/Test.ps1
```

`python` 需指向可用的 Python 3；也可用完整 Python 執行檔路徑。`Test.ps1` 需要 Node.js 與 Go，會載入 `Go-Env.ps1` 設定本機快取。

| 命令 | 實際範圍 | 通過訊號／限制 |
| --- | --- | --- |
| `python ./tools/Check-Dependencies.py` | Go SDK 的遊戲／Gin 依賴、C# namespace／部分 DTO 名稱、asmdef 的引擎依賴、基本教學 manifest 與遊戲隔離、JS runtime／bridge 的 JSON 解析與已知 probe 欄位、sample 單一來源 | `DEPENDENCIES PASS`；字串守門規則，不是完整靜態依賴圖 |
| `python -B -m unittest discover -s ./tools/tests -v` | 依賴檢查器的乾淨案例與刻意污染 fixture | 7 項測試；含基本教學不得加入選用套件或依賴完整遊戲，測試不修改正式來源 |
| `./tools/Test.ps1` | SDK JS、SDK 與各範例 Go module 的 test／vet、教學／整合 session 與 sample 診斷 JS | 命令 exit code 0；包括 HTTP、共用世界、呈現恢復、物件、錄製、AOI、codec 與歷史教學的 WS／協定測試 |
| `./tools/Test-Lessons.ps1` | 8 個 C# 主題分別只編譯所需 SDK 原始碼，另執行 4 個 Go 主題 | `RF_LESSONS PASS`；需要 .NET 8 SDK（可用 `-DotNet`）與 Go，純資料課程不需 Unity Editor／網路服務 |
| `./examples/interpolation/scripts/Test-Interpolation.ps1` | 真實 C# SDK 取樣與兩條 session，另以正式頁面原始碼連線真正教學主機 | `RF_INTERPOLATION PASS`、`RF_INTERPOLATION_BROWSER_SOURCE PASS`；需 .NET 8 SDK（含 ASP.NET Core）、Go、Node；預設隔離埠 18102／18103 |
| `./examples/prediction/scripts/Test-Prediction.ps1` | 真實 C# 預測／校正、帶延遲的 Go WS，正式頁面操作流程 | `RF_PREDICTION PASS`、`RF_PREDICTION_BROWSER_SOURCE PASS`；需相同工具，預設隔離埠 18112／18113 |
| `./examples/network-recovery/scripts/Test-NetworkRecovery.ps1` | 弱網、診斷、時鐘、ReconnectPolicy，保留預測／校正／插值 | `RF_NETWORK_RECOVERY PASS`、`RF_NETWORK_RECOVERY_BROWSER_SOURCE PASS`；同工具，隔離埠 18122／18123 |
| `./examples/objects-actions/scripts/Test-ObjectsActions.ps1` | 實際 ReplicaStore／ActionPresentation、兩個 session、正式網頁流程 | `RF_OBJECTS_ACTIONS PASS`、`RF_OBJECTS_ACTIONS_BROWSER_SOURCE PASS`；同工具，隔離埠 18132／18133；DOM／Canvas 替身 |
| `./examples/replay/scripts/Test-ReplayLesson.ps1` | Go journal／C# PlaybackClock、雙人 WS、錄製物件生命與正式網頁回放流程 | `RF_REPLAY_LESSON PASS`、`RF_REPLAY_LESSON_BROWSER_SOURCE PASS`；同工具，隔離埠 18142／18143；DOM／Canvas 替身 |
| `./examples/interest/scripts/Test-Interest.ps1` | 每接收者的真實 Go Radius、C# 接收清單／重入基準、雙人 WS 與正式頁面 | `RF_INTEREST PASS`、`RF_INTEREST_BROWSER_SOURCE PASS`；同工具，隔離埠 18152／18153；DOM／Canvas 替身 |
| `./examples/codec/scripts/Test-Codec.ps1` | 實際 Go Encoder／Quantize 與 C# FrameDecoder、雙人 WS、正式網頁 | `RF_CODEC PASS`、`RF_CODEC_BROWSER_SOURCE PASS`；同工具，隔離埠 18162／18163；`-NativeOnly` 可單跑數據／連線 |
| `./examples/history-lagcomp/scripts/Test-History.ps1` | SDK History／Bounded、C# 目標生命、雙人 WS、正式網頁 | `RF_HISTORY PASS`、`RF_HISTORY_BROWSER_SOURCE PASS`；同工具，隔離埠 18172／18173；支援 `-NativeOnly` |
| `./examples/shared-world/scripts/Test-SharedWorld.ps1 -NativeClient` | 獨立 Shared World 教學的 HTTP 模組類型、正式 JS／C# session，各開兩條真實 WS | `RF_SHARED_WORLD PASS`、`RF_NATIVE_SESSION PASS`；隔離埠 18096，檔案／日誌與整合遊戲分開；非 Unity Player 或 renderer |
| `./examples/rumblebound/scripts/Test-SharedWorld.ps1 -NativeClient` | 隔離 Go 伺服器、正式 JS session／transport 與純 C# ExampleSession／SDK 原生 transport，各開兩條真實 WS | `RF_SHARED_WORLD PASS`、`RF_NATIVE_SESSION PASS`；預設埠 18095。C# 使用 Unity 附帶 .NET SDK 或 `-DotNet` 指定的 .NET 8 SDK，直接編譯、不需 NuGet 套件；沒有執行 Unity Player／JsonUtility 或瀏覽器 renderer |
| `./samples/rumblebound/scripts/Test-Clients.ps1 -BrowserSourceOnly` | 自建隔離伺服器、HTTP 模組、Node 執行網頁原始碼的移動／恢復、probe RTT | `BROWSER_SOURCE PASS`；預設需要 18080／18443 可用，可用 `-Port`／`-TLSPort` 指定。沒有執行瀏覽器 renderer 或 Unity Player，不能與 Player 情境旗標並用 |
| `./tools/Test-Replay.ps1` | Go journal、遊戲重播、WSS／練習流程、獨立程序比對與刻意破壞案例 | 詳細產物及預期失敗案例見 [REPLAY](../REPLAY.md) |

依賴檢查通過仍可能有設計問題。傳輸責任與目前邊界見 [ARCHITECTURE](../ARCHITECTURE.md)；原始文字交付、等待上限、取消與替換另由行為測試驗證。

## Unity SDK、最小 Player 與完整遊戲

Unity Editor 命令需要有效授權與對應平台模組；執行前關閉同一專案的 Editor。SDK、各教學與整合遊戲是不同 Unity 專案。工具中的 Editor 路徑限制見入門文件。

| 命令 | 前提 | 驗證內容與輸出 |
| --- | --- | --- |
| `./tools/Test-Sdk.ps1` | 可啟動 Editor | 不引用遊戲的 SDK 契約與 Go/C# codec fixtures；`artifacts/sdk-tests.log` 中的 `RF_SDK_TESTS PASS` 及各模組 PASS |
| `./tools/Build.ps1 -Project Minimal -Target Windows` | Windows 建置支援 | `examples/minimal-unity/Builds/Windows/Minimal.exe`、`artifacts/minimal-Windows.log` |
| `./tools/Build.ps1 -Project Minimal -Target Web` | WebGL 建置支援 | `examples/minimal-unity/Builds/WebGL`、`artifacts/minimal-Web.log` |
| `./tools/Build.ps1 -Project SharedWorld -Target Windows` | Windows 建置支援 | 獨立教學 `examples/shared-world/client/Builds/Windows/SharedWorld.exe`、`artifacts/shared-world-Windows.log` |
| `./tools/Build.ps1 -Project SharedWorld -Target Web` | WebGL 建置支援 | 獨立教學 `examples/shared-world/client/Builds/WebGL`、`artifacts/shared-world-Web.log`；教學伺服器 `/unity/` 提供 |
| `./tools/Build.ps1 -Project Interpolation -Target Web` | 有效 Unity 授權與 WebGL 支援 | `examples/interpolation/client/Builds/WebGL`、`artifacts/interpolation-Web.log`；瀏覽器操作介面另可直接啟動，不依賴本建置 |
| `./tools/Build.ps1 -Project RumbleboundExample -Target Windows` | Windows 建置支援 | 新 example 狀態驗證與 Player；`examples/rumblebound/client/Builds/Windows/RumbleboundExample.exe`、`artifacts/example-Windows.log` |
| `./tools/Build.ps1 -Project RumbleboundExample -Target Web` | WebGL 建置支援 | 新 example 狀態驗證與 WebGL；`examples/rumblebound/client/Builds/WebGL`、`artifacts/example-Web.log`；由新伺服器 `/unity/` 提供 |
| `./tools/Test-Minimal.ps1` | 已建置最新 Minimal Windows Player，18091 可用 | 自行啟動／關閉伺服器與 Player，檢查實際單一連線的輸入與快照；`artifacts/minimal-smoke.log` 中 `RF_MINIMAL PASS` |
| `./tools/Build.ps1 -Project Rumblebound -Target Windows` | 完整示範資源與 Windows 支援 | 遊戲 Editor gates 與 Windows 建置；`samples/rumblebound/artifacts/unity-Windows.log` |
| `./tools/Build.ps1 -Project Rumblebound -Target Web` | 完整示範資源與 WebGL 支援 | 遊戲 Editor gates 與 WebGL 建置；`samples/rumblebound/artifacts/unity-Web.log` |
| `./samples/rumblebound/scripts/Test-Clients.ps1 -UI -PrivateMatch -Practice` | 最新 Rumblebound Windows Player，預設 18080／18443 可用 | 自建隔離伺服器；實際客戶端 UI、重連、私人對戰與練習；日誌位於 sample 的 `artifacts/` |
| `./tools/Test-Playback.ps1 -Preview` | 最新 Rumblebound Windows Player 與可用桌面圖形環境 | 離線播放操作、生命週期與畫面；`artifacts/playback-smoke.log`、`artifacts/playback-ui/` |

`Test-Clients.ps1` 可分別加入 `-Prediction fixed-step`、`-NetworkTools`、`-SevenModules`。這些是既有 sample 的測試旗標，名稱不代表框架總模組數。弱網測試模擬的是應用訊息；七項整合測試包含起手、確認／拒絕、歷史、AOI、缺基準與重連。詳細情境不等於完整組合矩陣或實機跨網路驗收。

WebGL 建置通過只代表編譯與連結成功。瀏覽器人工驗收應另外檢查載入、連線、操作、切換頁籤後恢復與各傳輸，並記錄瀏覽器／裝置／條件。HTTP 200 或原生 Player 測試不能取代這一步。

## 選用套件的實體移除

```powershell
./tools/Test-OptionalRemoval.ps1 -Module interpolation
./tools/Test-ServerRemoval.ps1 -Module history
```

| 工具 | 可選名稱 | 做法 |
| --- | --- | --- |
| `Test-OptionalRemoval.ps1` | `interpolation`、`prediction`、`correction`、`diagnostics`、`simulation`、`clock`、`replication`、`actions`、`codec`、`playback`、`all` | 暫移套件、移除 manifest 與組裝註冊，建置並確認 DLL 不存在，最後還原來源 |
| `Test-ServerRemoval.ps1` | `history`、`lagcomp`、`interest`、`codec`、`replay`、`all` | 暫移 Go 套件，使用對應 `rf_no_*` tag 執行 SDK／遊戲內部套件 test 與 vet，最後還原 |

這些工具會暫時修改來源與依賴，須序列執行，不可與使用相同套件的建置、Editor 或另一個移除測試並行。一般日常測試不必每次跑全部移除建置；改到組裝、依賴或 fallback 時再選擇相關案例。

## 跨語言案例的更新

修改遊戲碰撞規則後，在框架根目錄執行：

```powershell
. ./tools/Go-Env.ps1
Push-Location ./samples/rumblebound
try {
    & $Go run ./cmd/predictionfixtures
    if ($LASTEXITCODE -ne 0) { throw 'Prediction fixture generation failed' }
} finally { Pop-Location }
```

再以 Unity 建置的 `FixedPredictionVerification` 驗證新資料。通用 codec fixtures 位於 [tests/fixtures/codec.json](../../tests/fixtures/codec.json)，SDK 測試對照 Go 與 C# 解碼；兩種 fixtures 的責任不同，遊戲規則案例不放進 SDK。

## 歷史與延遲補償教學（2026-11-01）

[第 9 課](../../examples/history-lagcomp/README.md) 的 Go／C#／雙 WS／網頁原始碼流程通過，見 `artifacts/history-tests.log`。驗證歷史讀取不改 live 位置／血量、SDK bracket 內插、實際範圍、目前未中／回溯命中、傷害只改目前 HP、動作去重、過期／未來／生命拒絕、死亡與重設清歷史、新生命呈現 tick 夾住基準，以及 query 合併、停用、重連與舊回呼。

SDK `history.Buffer.Bounds()` 另以空資料、替換、淘汰、舊 tick 與 Clear 測試；新 API 不改原本 History 契約。網頁仍使用 DOM／Canvas 替身，Unity 接線與真正平台驗收接續進行。

## Codec 與量化教學（2026-10-31）

[第 8 課](../../examples/codec/README.md) 的 Go test、C# channel／雙 WS 與網頁原始碼操作通過，見 `artifacts/codec-tests.log`。涵蓋每接收者獨立基準、只有驗證成功才 ACK、ACK 0 完整恢復、設定 stream 版本隔離、JSON／差分大小、量化誤差與權威精度不變、物件生命與 AOI、重連，以及清量測不清 decoder。量化比較測試在整數邊界可能沒有差異，測試路徑已避開邊界，避免誤判。

本機網頁測試使用 DOM／Canvas 替身；Unity 場景及正式平台驗收依 [持續作業表](worklogs/REMAINING_WORK.md) 接續完成。

## AOI 興趣範圍教學（2026-10-31）

新增 [Interest 第 7 課](../../examples/interest/README.md)，實際使用 Go `interest.Radius`，每位接收者有獨立 selector；篩選遠端玩家與中立／玩家標記，自己和金色常駐地標保留。權威資料不因範圍進出而刪除，主畫面對照接收數與世界數。

- `Test-Interest.ps1` 通過，見 `artifacts/interest-tests.log`。純 C# 驗證投影清單、範圍外 owner 引用、模型與摘要複製、同 ID 重入／新生命替換、無效資料原子拒絕、範圍重設，以及 latest／fixed／adaptive 重入不採離開前的舊座標；30／60／144 FPS 本機預測保留。
- 雙條真實 SDK WS 驗證 all／aoi 對不同接收者獨立、遠處資料不在快照陣列、實際 JSON payload 變小、必要實體與半徑變更、玩家進出、隱藏的自己標記仍由權威持有／重生，以及重入採用目前生命、真正移除與重連。正式網頁原始碼驗證選項、半徑圈／緩衝圈、數量、物件操作、手動／自動／失焦、原本平滑開關與重新連線。使用 DOM／Canvas 替身，沒有實際 renderer 驗收。
- Go 測試驗證每接收者進出遲滯、切換模式重建基準、投影不改權威、必要物件、真實 WS 載荷與客戶端中心／他人／非法半徑／重複欄位拒絕。整體 `Test.ps1` 的十一個 Go module test／vet 與 56 項既有 JS 通過，見 `artifacts/interest-framework-tests.log`。
- 八個 SDK 模組與純 C# 應用以 Unity .NET Standard 2.1 參考組件編譯通過，見 `artifacts/interest-csharp.log`。依賴守門及 7 項反例通過；十二筆選單順序、選 7 的實際分派、退出與 PowerShell 語法通過。文件連結及原專案 210 份基準檔未變，見 `example-menu-order.json`、`interest-powershell.json`、`interest-review.json`。

此課沒有新增 Unity 場景或 Player，完整對戰重製進度維持原階段。範圍清單是傳輸投影，遮蔽物與隊伍視野由應用另行定義；快照 JSON byte 數不包含傳輸標頭或本機教學通道。

## 錄製與播放教學（2026-10-31）

新增 [Replay 第 6 課](../../examples/replay/README.md)，以 Go journal 記錄完整權威快照，再用實際 C# `PlaybackClock` 控制獨立回放。開始／結束／播放為主操作，進階資訊預設收起；前五項教學順序不變，整合示範與進階參考往後排列。

- `Test-ReplayLesson.ps1` 通過，見 `artifacts/replay-lesson-tests.log`。30／60／144 FPS 驗證時間推進、平滑取樣、暫停、速度、結尾重播、向前／向後跳轉、生命與物件移除、深複製、無效載入保留舊檔及清除。
- 兩條真實 SDK WS 驗證錄製控制各自獨立、未完成檔案不公開、重複開始不重開錄製、完整玩家清單／移動／物件生命被保存、另一份錄製不覆蓋舊檔，以及即時更新與回放隔離、重連及舊 epoch。正式網頁原始碼連到真正 C#／Gin 主機，驗證錄製→驗證下載→播放、暫停／進度／速度、播放期間拒絕即時操作、新錄製替換、保留平滑選項與斷線後回放。使用 DOM／Canvas 替身，沒有實際 renderer 驗收。
- Go 測試包含 SDK journal 完成與匯出、損壞／partial／狀態 hash 拒絕、應用擷取溢位不能發布有效前綴、30 秒與 16 次錄製上限、關閉時結束錄製，以及客戶端路徑／指定他人歸屬／重複欄位拒絕。整體 `Test.ps1` 的十個 Go module test／vet 與 56 項既有 JS 通過，見 `artifacts/replay-lesson-framework-tests.log`。
- 九個 SDK 模組與純 C# 應用以 Unity .NET Standard 2.1 參考組件編譯通過，見 `artifacts/replay-lesson-csharp.log`。依賴守門及 7 項反例通過；十一筆選單順序、實際選 6 的分派、退出與 PowerShell 語法通過。文件連結及原專案 210 份基準檔未變，見 `example-menu-order.json`、`replay-lesson-powershell.json`、`replay-lesson-review.json`。

此課記錄權威快照，播放資料格式由教學應用定義；完整遊戲的輸入事件重播仍依 [REPLAY](../REPLAY.md) 驗證。沒有新增 Unity 場景、重建 Player 或宣稱完整對戰重製已推進。

## 物件同步與動作確認（2026-10-31）

新增 [ObjectsActions](../../examples/objects-actions/README.md)，以兩個教學頁籤示範實際 `ReplicaStore<T>`／`ActionPresentation`，進階控制預設收起。沿用預測、校正及遠端插值；新 Go 權威服務只提供 health／WS，頁面由 C# 主機負責，沒有附加未使用的 Unity 靜態路由。

- `Test-ObjectsActions.ps1` 通過：完整清單複製與原子驗證、同 ID 不同生命 removed＋added、舊 tick、動作等待／暫時效果、確認去重、拒絕收回、逾時、晚到／未知結果、保留期限與清除；既有 30／60／144 FPS 預測／校正檢查仍通過。
- 兩條真實 SDK WS 驗證跨玩家物件新增／移除／重生、相同動作重送不重新執行、拒絕／沒有回覆情境、離線物件清除與舊 epoch 隔離。正式網頁原始碼連到真正 C#／Gin 主機，驗證頁籤、操作狀態、暫時效果、共用清單與原本移動控制；使用 DOM／Canvas 替身，不代表真正 renderer 驗收。日誌 `artifacts/objects-actions-tests.log`。
- Go test／vet 驗證所有權、容量、待執行／結果佇列上限、斷線取消、嚴格 JSON 與 HTTP 路由，見 `artifacts/objects-actions-server-tests.log`。整體 `Test.ps1` 的九個 Go module 與 56 項既有 JS 通過，見 `artifacts/objects-actions-framework-tests.log`。
- 八個 SDK 模組與純 C# 應用以 Unity .NET Standard 2.1 參考組件編譯通過，見 `artifacts/objects-actions-csharp.log`。當輪選單追加為 10；後續依學習順序移至 5，最新順序見本頁「範例選單入口」。文件、PowerShell 語法與原專案 210 份基準檔未變，見 `objects-actions-menu.json`、`objects-actions-review.json`、`objects-actions-powershell.json`。

本課沒有新增 Unity 專案或重建 Player。跨重連交易、持久化、投射物與戰鬥規則不在這個教學範圍，完整對戰重製進度沒有因此改變。

## 即時連線與恢復（2026-10-31）

後續將預設頁面改為入門引導：正常移動、模擬意外斷線、手動停止三個動作，先看方塊消失再重新加入。弱網、診斷、時鐘與呈現選項摺入「進階觀察」。正式頁面原始碼測試已使用這三個新入口驗證重試、停止及重新開始，紀錄在 `artifacts/network-recovery-guided-tests.log`；DOM／Canvas 仍是替身，不宣稱 renderer 驗收。

新增 [綜合操作教學](../../examples/network-recovery/README.md)：實際 SDK 的雙向 MessageConditioner、ClientDiagnostics、DisciplinedClock 與 core ReconnectPolicy，同時運作自己的預測／校正與遠端插值。Replay／Playback 未混入即時面板。

- `Test-NetworkRecovery.ps1` 通過，見 `artifacts/network-recovery-tests.log`。保留 30／60／144 FPS 與兩條實際 WS 基礎案例，另驗證初次失敗不循環、退避／取消／期限、診斷配對與去重、時鐘上限、丟失／雙向延遲、快照暫停後預測停止與恢復、真正 SDK WebSocket 關閉後重建、新 ID 及舊 epoch 隔離。
- 正式 app.js 在 Node＋DOM／Canvas 替身中連到真正 C#／Gin 服務；驗證各同步開關、雙向自訂設定、診斷開關、時鐘切換、三秒快照暫停、四秒斷線與退避紀錄、手動停止後不重連。沒有宣稱實際 renderer 或瀏覽器效能驗收。
- Go test／vet 包含 owner 擷取 tick 的 pong、有界探測回覆與嚴格探測 JSON；整體 `Test.ps1` 納入第八個 Go module，56 項既有 JS 通過，見 `artifacts/network-recovery-framework-tests.log`。
- 純應用以 Unity 的 .NET Standard 2.1 參考組件編譯，見 `artifacts/network-recovery-csharp.log`。文件、PowerShell 與原專案基準檢查見 `artifacts/network-recovery-review.json`、`network-recovery-powershell.json`。本課沒有新增 Unity 場景或重試尚未修復的 Editor 授權。

此處 RTT 是送入模擬鏈路至配對回覆交付的應用往返，包含模擬與 owner 等待；JSON 流量不含協定標頭，C# 更新間隔也不是瀏覽器 FPS。重新加入會取得新玩家，不保留席位／原位置；這些限制已在操作頁標明。

## 預測與校正操作教學（2026-10-31）

後續補上獨立的遠端插值開關：latest／fixed／adaptive，預設 adaptive。自己的座標仍由 prediction／correction 決定，僅遠端角色採插值取樣。`Test-Prediction.ps1` 驗證移動中的第二位玩家在關閉時採最新值、啟用時有緩衝取樣，且不覆蓋自己的預測座標；重連保留選項並清掉舊快照。日誌為 `artifacts/prediction-remote-smoothing-tests.log`。測試改用 `.local/prediction/tests`，與使用者正在執行的正式教學主機分開。

新增 [預測／校正範例](../../examples/prediction/README.md)，以正式 C# prediction／correction SDK 處理自己的輸入。應用自有 MotionModel 與 Go 規則一致；此課獨立協定增加 inputTick，明確映射 ack 的已套用時間，不修改 Shared World 或插值教學。

- `Test-Prediction.ps1` 通過：30／60／144 FPS 的立即預測、ack 重播、顯示偏移衰減、直接校正、模式切換、期限；斜向正規化、界線、放開與反向。兩條真實 WS 驗證回包延遲下先呈現輸入、伺服器推移只影響自己、平滑收斂、離開與舊 epoch 隔離。紀錄 `artifacts/prediction-tests.log`。
- 正式頁面原始碼以 Node＋DOM／Canvas 替身連到真正 C#／Gin 服務，驗證載入與 MIME、預測／校正開關、300 ms 延遲、輸入早於權威、失焦停止、推移、雙視窗與重連。未以此宣稱真正 renderer 或 Unity Player 已驗收。
- Go test／vet 包含 inputTick 首次套用語意、舊序號不更新、推移的連線歸屬與嚴格 JSON。整體 `Test.ps1` 納入第七個 Go module，56 項既有 JS 仍通過；見 `artifacts/prediction-framework-tests.log`。
- 純 C# 應用以 Unity .NET Standard 2.1 參考組件編譯通過；文件連結、PowerShell 語法與原專案基準核對見 `artifacts/prediction-csharp.log`、`prediction-review.json`、`prediction-powershell.json`。本課沒有新增 Unity 場景或嘗試 Player 建置。

## 插值操作教學（2026-10-31）

新增 [可操作插值範例](../../examples/interpolation/README.md)，包含 Unity 應用與不需 Editor 的本機瀏覽器展示。瀏覽器由 ASP.NET Core 教學主機直接執行正式 `ExampleSession`、`InterpolatedWorld` 與 C# SDK；JS 只處理操作／Canvas，沒有另寫插值演算法。共用權威服務沿用 Gin Shared World。

- `Test-Interpolation.ps1` 通過，日誌 `artifacts/interpolation-tests.log`。30／60／144 FPS 下，固定與自適應的最大呈現步距均小於直接套用；另驗證不倒退、不外推、有界歷史、模式切換、DTO 複製、離開、welcome／斷線重設、真實 WS 輸入歸屬與舊 epoch 隔離。
- 正式 `app.js` 以 Node＋DOM／Canvas 替身執行，連線真正 HTTP／C# 主機；頁面載入及 JavaScript MIME、模式與情境切換、自己的插值開關、自動移動、手動輸入／失焦、雙視窗與重連通過。這項測試沒有執行真正瀏覽器 renderer。
- Unity .NET Standard 2.1 參考組件編譯 SDK、純應用、Unity 呈現與 Editor 程式通過；`artifacts/interpolation-csharp.log`。資產 `.meta`／GUID、文件與 PowerShell 檢查見 `artifacts/interpolation-assets.json`、`interpolation-review.json`、`interpolation-powershell.json`。
- WebGL 建置以 180 秒上限實際嘗試，仍因 Unity Licensing IPC 連線失敗逾時，沒有 Player；詳見 `artifacts/interpolation-Web.log`、`interpolation-build-attempt.log`。瀏覽器本機展示不需這個建置，但不能拿它宣稱 IL2CPP／WebGL 已驗收。

## 教學與完整遊戲分流（2026-10-31）

學習入口見 [examples](../../examples/README.md)。新增獨立 Shared World，Rumblebound 保留完整遊戲重製責任；十個 Unity 選用套件整理成八個 C# 主題，五個 Go 選用 package 整理成四個主題，core／transport／loop 由 Minimal 與 Shared World 示範。

- `Test.ps1` 通過：六個 Go module test／vet，16 項 SDK、4 項 Shared World、4 項 Rumblebound、32 項 sample JS，合計 56 項 JS；日誌 `artifacts/learning-examples-tests.log`。Go 課程的結果另由執行課程驗證，不以「沒有 test files」當行為驗證。
- `Test-Lessons.ps1` 從框架以外的暫存目錄呼叫通過：8 個 C# 與 4 個 Go 主題；`artifacts/learning-lessons.log`。各課驗證可觀察的前後差異、必要 reset／fallback；C# 每課單獨編譯，沒有依賴其他課程或 Rumblebound。
- Shared World 的隔離伺服器、JS 與 C# 雙 session 通過，見 `artifacts/teaching-shared-world.log`。`Run-Example.ps1` 從另一個工作目錄以 18098 啟動，也通過 HTTP JavaScript MIME 與雙 WS 冒煙測試，測後已停止自建程序。
- 新 Unity 專案以 Unity 6000.6.6f1 的 .NET Standard 2.1／引擎參考組件編譯 core、transport、應用、呈現與 Editor 程式通過，見 `artifacts/learning-examples-csharp.log`。新資產有獨立 GUID，`.meta` 檢查 0 error／0 warning，見 `artifacts/teaching-unity-assets.json`。
- 依賴守門與 7 項反例測試通過；文件連結、PowerShell 語法與外層原專案 210 份基準檔雜湊核對見 `artifacts/learning-examples-review.json`、`learning-examples-powershell.json`。

這次沒有重新嘗試尚未修復的 Unity Licensing，也沒有實際瀏覽器工具驗收。上述編譯與數值／連線檢查不替代 Player、WebGL 或畫面驗收；完整對戰重製仍是第 1 階段基礎，沒有宣稱房間與戰鬥已移植完成。

## 瀏覽器連線修正（2026-10-31）

實際讀取 18093 的 HTTP 回應發現 `/session.mjs` 為 `text/plain; charset=utf-8`：雖然回 200，瀏覽器仍會拒絕載入模組，連線按鈕因此無法運作。新 example 伺服器現在明確註冊 `.js`／`.mjs` 的 JavaScript MIME，避免依賴主機檔案關聯。

新增回歸測試先模擬錯誤 MIME，確認修正前失敗、修正後 GET／HEAD 均為 JavaScript。新 example 的 Go test／vet 與 `Test-SharedWorld.ps1 -NativeClient` 通過，後者現在也檢查三個模組端點的 Content-Type；紀錄在 `artifacts/shared-world-mime-integration.log`。這仍是 HTTP 與真實 WS 驗證，沒有額外完成瀏覽器畫面驗收。既有 `go run` 程序需要重啟才會套用修正。

## 第 1 階段：共用世界（2026-10-31）

新 example 的啟動、操作與責任分工集中在 [README](../../examples/rumblebound/README.md)，訊息格式集中在 [PROTOCOL](../../examples/rumblebound/PROTOCOL.md)。本輪結果：

| 檢查 | 結果與範圍 |
| --- | --- |
| `Test.ps1` | 四個 Go module 的 test／vet、16 項 SDK JS、4 項新 example session 與 32 項 sample JS 通過，合計 52 項 JS；見 `artifacts/shared-world-tests.log` |
| 共用世界 Go 測試 | 真實 WS 驗證同清單、自己的輸入、停止、離開與重新加入；另驗證輸入逾時、方向正規化、界線、舊序號、容量、取消與最新快照佇列，以及畸形／重複／額外欄位的拒絕 |
| `Test-SharedWorld.ps1 -NativeClient` | `RF_SHARED_WORLD PASS` 與 `RF_NATIVE_SESSION PASS`；正式 JS session 與純 C# ExampleSession 各以兩條真實 WS 驗證移動、送零、離開與重連。JS 另驗證輸入逾時，C# 另驗證時間軸、舊 epoch 與原子套用；日誌為 `artifacts/shared-world-integration.log`、`shared-world-node.log`、`shared-world-native.log` |
| Unity 程式編譯 | Unity 6000.6.6f1 附帶 Roslyn、.NET Standard 2.1 與 Unity 參考組件編譯 core、transport、純應用、Unity 呈現與 Editor 建置／狀態驗證程式通過；不等同 Editor 執行或 Player 建置 |
| 資產與來源邊界 | 新 Unity 專案 `.meta`／GUID 檢查無錯誤或警告；依賴守門與 6 項反例測試、文件連結與 PowerShell 語法檢查通過；外層原專案 210 份基準檔雜湊未變。檢查結果見 `artifacts/shared-world-review.json`、`shared-world-powershell.json` |

Unity Windows 建置已嘗試 `-TimeoutSeconds 180`，因 Licensing IPC 連線失敗且缺少 `com.unity.editor.headless` 授權而逾時，沒有產生 Player，詳見 `artifacts/example-Windows.log`。實際瀏覽器操作被 Work settings 停用，也未完成畫面驗收。第 1 階段程式與連線驗證已完成；兩個 Unity Player 的互見、鍵盤／失焦、重連，以及 WebGL 載入、橋接與呈現仍待上述環境可用後補驗，不能用 C# 主控台或 Node 結果替代。

## Gin 與通用套件整合（2026-10-31，歷史）

套件選擇與限流行為見 [DEPENDENCIES](../DEPENDENCIES.md)。本輪驗證：

- `Test.ps1` 通過：四個 Go module 的 test／vet、16 項 SDK JS 與 32 項 sample JS。新增 HTTP GET／HEAD／404／405、靜態來源、Unity 目錄轉址及 clientlab 覆寫路由測試。
- Minimal 以真正 Gin server 與兩條 `coder/websocket` 連線驗證獨立世界、各自輸入、送零停止，以及另一條連線關閉後仍可移動。
- sample 既有登入、Origin、cookie、嚴格 JSON、房間與 WebTransport 測試通過；WebTransport 測試包含本機 UDP／HTTP3 握手、可靠串流及 datagram，與真正瀏覽器驗收仍不同。
- 訪客限流驗證突發上限、每五秒補一額度、跨分鐘不重設突發額度、併發、256 peer 容量與閒置清除。
- `Test-Clients.ps1 -BrowserSourceOnly -Port 18094 -TLSPort 18494` 通過；隔離伺服器的移動、放開停止、反向、重連、停收恢復與 probe 量測正常。這是 Node 執行網頁原始碼與真實 WS，不包含 renderer／Unity Player。
- 依賴檢查與 6 項反例測試通過；`go list -deps ./...` 確認 Go 同步 SDK 沒有 Gin 相依。
- 四個 Go module 的 `go mod verify` 全部通過；文件連結／錨點與 22 個 PowerShell 範例通過檢查，外層原專案的 210 份基準檔雜湊未變。

結果保存在 `artifacts/gin-adoption-tests.log`、`artifacts/gin-adoption-live.log` 與 `artifacts/gin-adoption-review.json`。當輪沒有改 Unity 程式或重建 Player；當時重製版為第 0 階段，共用世界的後續結果見上節。

## 第 0 階段驗證（2026-10-31）

本階段修改 JS 傳輸與 sample 適配器，並建立 [重製骨架](../../examples/rumblebound/README.md)。下列結果對應本次來源：

| 檢查 | 結果與範圍 |
| --- | --- |
| `Test.ps1` | 16 項 SDK JS、32 項 sample JS、四個 Go module 的 test／vet 通過；sample 已移除與 SDK 重複的低階 socket 測試 |
| 依賴守門與反例 | `DEPENDENCIES PASS`；5 項檢查器測試通過，涵蓋 runtime／bridge 解析 JSON、遊戲 probe 欄位與第二份傳輸實作 |
| `Test-Clients.ps1 -BrowserSourceOnly -Port 18094 -TLSPort 18494` | 隔離伺服器與真實 loopback WS 通過；移動、停止、反向、重連、自動恢復與停收快照恢復正常；probe 收到 7 個有效 RTT、113 個 input echo。日誌在 sample `artifacts/browser-movement-smoke.json`、`artifacts/connection-latency.json` |
| 新 example 啟動 | 重新建置 `examples/rumblebound/server` 並從其目錄啟動；18093 的 `/healthz` 與 `/transport.js` 通過，HTTP runtime 與 SDK 原檔一致 |
| C# 組裝編譯 | 使用 Unity 6000.6.6f1 附帶的 Roslyn 與 .NET Standard 2.1 參考組件，編譯 core、native／WebGL transport 及新 example 組裝通過。WebGL 分支仍有兩個既存原生欄位未使用警告；這不是 Editor 匯入或 IL2CPP 建置驗收 |
| 新 Unity 資產 | `.meta`／GUID 檢查 0 error、0 warning；UPM 的 core／transport 相對路徑都能解析到來源套件 |
| 文件與原專案 | 12 份文件、105 個本機連結／錨點、22 個 PowerShell 區塊與兩支修改腳本通過檢查；外層原專案的 210 份基準檔雜湊未變。結果在 `artifacts/transport-foundation-review.json` 與 `artifacts/transport-foundation-powershell.json` |

WT 背壓、取消、替換與真正 `.jslib` 的呼叫路徑由 Node 測試驗證，平台物件使用測試替身。當輪沒有重新建置 Unity Player、執行真正瀏覽器 WebTransport 或雙人新 example；共用世界的後續結果見第 1 階段。

## 前一輪文件整理與歷史紀錄

2026-10-31 前一輪只整理文件，當時沒有修改 SDK／遊戲實作。下表保留當輪證據；不以舊 Player 或舊建置日誌宣稱目前原始碼完成 Unity 全平台驗收。

| 本次檢查 | 結果與證據 |
| --- | --- |
| 文件連結、錨點、JSON 範例與程式完整性 | 本機檢查通過；335 份程式／設定雜湊與整理前相同。計數與結果在 `artifacts/docs-review.json` |
| PowerShell 文件範例 | 所有 PowerShell 區塊以 Parser 做語法檢查；結果在 `artifacts/docs-powershell-check.json`。語法通過不代表每個需授權／資源的命令都已執行 |
| 當時依賴檢查 | `Check-Dependencies.py` 通過；當時尚未檢查 JS，後續第 0 階段已擴充 |
| Minimal Go 入口與直接 WS 探針 | 重新建置最小伺服器；`/healthz`、`/transport.js` 回應 200。兩個 Node WS 客戶端確認持續輸入移動、送零後停止、遞增快照及每連線獨立世界，結果為 PASS；見 `artifacts/docs-minimal-probe.json` |
| `Test-Minimal.ps1` 原生 Player 冒煙測試 | 使用既有 Windows Player 嘗試執行，15 秒逾時，未取得 `RF_MINIMAL PASS`；見 `artifacts/minimal-smoke.log`。本次未重新建置 Player，也未確認逾時根因；不能算作原生遊玩驗收通過 |

直接 WS 探針驗證伺服器入口與文件描述，不會執行 Unity C#／WebGL bridge。這次沒有重跑 Unity Editor 建置、完整遊戲回歸或瀏覽器遊玩；它們仍需依上面的命令與平台前提另行驗收。

2026-10-31 前一輪架構審查已執行 `Test.ps1`：Go test／vet、13 項 SDK JS 與 38 項診斷 JS 通過；`Check-Dependencies.py` 通過。同輪 `Test-Sdk.ps1` 因 Unity Licensing 初始化失敗未完成。當時重現的 JS ping 欄位順序問題，已在後續第 0 階段移到應用適配器修正。

第一版歷次 Windows／WebGL、套件移除、實際遊戲與重播的詳細紀錄保留在 [歷史驗證紀錄](VALIDATION-2026-10-31.md)。這些記錄描述當時版本，服務狀態與產物可能已改變；新增驗證應記錄日期、命令、版本／輸入條件、結果與尚未涵蓋的範圍。

## 第 3～9 課 Unity 接線（2026-11-01）

七課各有獨立 Unity 專案、保存場景、Windows／WebGL 建置入口、純應用／呈現／Editor assembly、DTO stripping 保留與完整 `.meta`。純應用移入 `client/Assets/Scripts`，原生測試與瀏覽器主機直接編譯同一份來源。新增共用呈現 package，不將 UnityEngine 帶進 SDK。

- `./tools/Test-UnityLessons.ps1`：七課 application／Unity／Editor 及共用層，使用本機 Unity 6000.6.6f1、.NET Standard 2.1 實際參考組件編譯通過；WebGL transport 分支也通過。紀錄：`artifacts/unity-lessons-compile.log`。
- skill 資產檢查器：七課 GUID／script 參照檢查通過，紀錄 `artifacts/unity-*-assets.json`。
- 教學主機新增 `/ws` 原始代理、`/transport.js`、`/unity/`；測試使用真正 SDK JavaScript transport、兩位 Go 權威玩家、移動及 Origin 拒絕邊界，不使用假的同步演算法。

上述不代表 Editor import 或 Player 已執行。既有 Unity Licensing／瀏覽器 Work settings 限制尚未改變，本輪沒有重複嘗試相同失敗條件，也沒有宣稱實際 Windows／WebGL 畫面驗收完成。完整命令見 [Unity 教學說明](../../examples/unity-common/README.md)。

## 傳輸平台邊界（2026-11-01）

`./tools/Test-Transport.ps1` 通過：原生 WS 使用真實本機 Kestrel 服務驗證跨 UTF-8 字元的 fragmented frame、恰好 32 KiB、送出／接收超限、非法 UTF-8、兩實例隔離、舊 epoch、公用 Receive 與 conditioner envelope。SDK JavaScript／實際 `.jslib` 共 20 項測試通過，新增 WS／WT 雙向同上限、非文字、非法／截斷 UTF-8、多 handle、相對 runtime URL、替換／釋放／容量，以及舊 ABI 隔離。紀錄 `artifacts/transport-platform-tests.log`。

七課移動後的 C#／雙人 WS／網頁原始碼流程及新增 raw SDK WS 代理測試全部通過，紀錄 `artifacts/unity-<lesson>-tests.log`。AOI 原生測試先等待第一位 welcome，再連第二位，消除同時握手與固定出生幾何假設之間的競態；應用規則未因此變更。平台參考編譯在傳輸 ABI 更新後再次通過。

原生 WebTransport 另以選用 Go helper 實作，`native/webtransport` 為獨立 module，server SDK 不新增 QUIC 相依。`Test-Transport.ps1` 的第二階段以真正 helper 子程序與 HTTP/3 測試服務通過 pinned TLS、可靠 32 KiB、UTF-8 datagram、兩實例、錯誤 pin 拒絕、Close／replacement 與診斷中斷。Go 測試／vet 也通過 stdio EOF 與 pin／文字邊界。Unity／.NET Standard 2.1 參考編譯在加入 helper adapter 後再次通過。尚未驗收 Unity Player 內啟動子程序或其他 OS 實際執行；需由應用帶入正確平台的 helper。詳見 [原生 WT 說明](../../native/webtransport/README.md)。

## 重製版第 2 階段（2026-11-01）

`examples/rumblebound/server` 的原地圖／碰撞／推擠規則與案例通過 Go test／vet，GameNetAdapter 另驗證掩體、滑動、推擠、迭代順序與投影隔離。JS 5 項狀態測試通過，包括 arena clone／範圍／原子套用。`Test-SharedWorld.ps1 -NativeClient` 使用隔離埠 18183，真實兩個 JS 與兩個純 C# SDK 用戶端通過；紀錄 `artifacts/remake-movement-tests.log`。純應用／Unity／Editor 參考編譯與資產檢查通過，紀錄 `artifacts/unity-lessons-compile.log`、`artifacts/remake-movement-assets.json`。尚未驗收真實 renderer／Player，也尚未接完整對戰。

原生 WebTransport helper 已交叉編譯 linux-amd64、darwin-arm64（`artifacts/native-wt-*-build.log`）；未在對應 OS 執行，不能據此宣稱跨 OS 執行已驗收。

## 完整重製版接線（2026-11-01）

正式 `Rumblebound` 選單已改為新的 `scripts/Run.ps1`：實際 C# SDK 主機＋獨立 Gin API。保留的 v1 方塊／SharedWorld 入口只用於地圖診斷。原遊戲 domain、純 C# 幾何／移動與美術呈現抽成 `games/rumblebound`，原 sample 與 example 共用；原素材 GUID／來源授權保留。

- `Test-Battle.ps1`：真實兩個 C# SDK 用戶端通過訪客／房間／分隊選角／準備／雙人載入門檻、預測、攻擊／衝刺確認、差分基準恢復、時鐘、原身份／entity／席位續接及 input／action 序號恢復。GameFlow 另通過 HTTP 取消／過期回覆隔離、自動／手動重連、魔法師／練習 AI／離開／登出。播放器讀取原 Go 匯出的實際 clip，驗證採樣、seek、暫停／速度、接收端錄製往返與錯誤檔案原子拒絕。紀錄 `artifacts/remake-battle-native-tests.log`。
- `Test-Browser.ps1 -WebTransport`：兩個實際頁面來源連上 C#／Go，完成房間到對戰、重新選組件／差分／缺基準、同席位 HTTP/3 helper 恢復、頁面身份恢復、法師／AI、錄製下載／上傳／播放控制與連續新練習場載入。Google start／attempt cookie／錯誤 token／重複 nonce 透過同源代理驗證；不使用真實帳號。測試以 DOM／Canvas 替身執行，不代表真實 renderer 或瀏覽器原生 WebTransport。紀錄 `artifacts/remake-browser-wt-tests.log`。
- `Test-BattleRemoval.ps1`：隔離來源樹內十個選用 package／DLL 全部不存在，編譯 core／transport／遊戲／新 Battle 應用，實際連線跑完整 GameFlow。驗證 authority／snap／latest／snapshot fallback；不冒充 Unity stripped Player 驗收。紀錄 `artifacts/remake-removal-tests.log`。
- `Test-GameDomain.ps1`：原碰撞、固定步進、30／60／144 FPS 預測／遠端採樣／接觸、恢復案例通過，紀錄 `artifacts/remake-shared-domain.log`。JSON／logging 是測試適配器，遊戲算法與 SDK 是實際來源。
- `Test-UnityLessons.ps1 -Sample`：共用 gameplay、URP 呈現、新 Battle 純應用／Unity／Editor、原 sample 全部 runtime／Editor，以及七課參考編譯通過；紀錄 `artifacts/remake-unity-reference.log`。
- `Test.ps1`：15 個 Go module 的 test／vet、20 項 SDK JS、九項方塊教學 JS 與 32 項原 sample JS 通過；紀錄 `artifacts/remake-framework-tests.log`。後續新增的共用開發憑證／Google bridge 與新 HTTP/3 入口另由定向測試覆蓋。

Unity 實際建置、資產 import、Windows／WebGL Player、完整回合與 renderer、真實 Google 帳號尚未宣稱通過；最新建置結果另記，不將上述替身／參考編譯當作替代。

## Unity 實際執行補驗（2026-11-01）

以工具權限審查允許標準 Unity cache／Licensing IPC 後，授權已成功解析；先前沙箱內的 Licensing 失敗不再作為建置阻擋。實際 import 找出並修正不存在的 `com.unity.modules.inputlegacy` 宣告、Unity adapter 缺少的 core／playback asmdef 相依，以及完整遊戲 JSON 空 inline 物件問題。完整遊戲 adapter 改用官方 Newtonsoft；參考編譯也改為依各 Unity／Editor asmdef 的實際相依，避免額外 DLL 掩蓋問題。

| 項目 | 本次證據與界線 |
| --- | --- |
| 獨立 SDK Editor | `Test-Sdk.ps1` 通過四個 PASS 訊號；弱網 payload 檢查更新為真正 32 KiB／UTF-8 byte 邊界。紀錄 `artifacts/remake-sdk-unity-tests.log`、`sdk-tests.log` |
| 第 3～9 課 Windows | 七個 `Build.ps1 -Project <課名> -Target Windows` 全部成功。各 `<lesson>-Windows.log` 含建置 PASS |
| 第 3～9 課原生執行 | `Test-LessonPlayers.ps1` 啟動每課兩個真正的 Unity Player 與專用 Go 服務，14 次通過 Unity 生命週期／JSON、SDK／socket、兩人 roster、權威 input ACK 與採樣移動。紀錄 `artifacts/lesson-player-tests.log`、`lesson-player-tests-6-9.log` 及各 `player-<lesson>-1/2.log`。使用 headless／nographics，不代表畫面驗收 |
| 第 3～9 課 WebGL | 七課真正 IL2CPP／WebGL 建置成功，各 `<lesson>-Web.log` 含 PASS；尚未在瀏覽器執行 |
| 完整遊戲 Windows | `RumbleboundExample` 實際 import／JSON／原角色動畫與素材 gate／BuildReport 通過，紀錄 `artifacts/example-Windows.log`；真正遊玩流程另由 `Test-BattlePlayer.ps1` 持續補驗，不以建置當作完整驗收 |
| 桌面畫面工具 | 新建置 Player 已啟動；computer-use 的應用存取核准逾時，未取得畫面，不宣稱 UI 操作通過 |
| 瀏覽器 | 工具最新 inventory 明確回報 `Local browser use is disabled in Work settings`，未繞過設定；renderer／WebGL 實際操作仍待開放 |

這一節取代上方當時「Unity 無法實際建置」的環境結論；其餘歷史測試範圍維持原意。

完整遊戲 `Test-BattlePlayer.ps1` 隨後通過兩個真正 headless Windows Player：房間／分隊／角色／載入、權威 ACK／移動、遠程／近戰、原模型／掩體／彈體實例、原身份重連、新練習場載入、法師三次權威傷害／死亡／新生命復活、據點分數、最終 result／返回／離開，以及離線錄製播放。專用 API 由父程序 stdin 請求正常停止，三份伺服器 journal 完整收尾並逐一在獨立 Go 程序重建通過。紀錄 `artifacts/battle-player-tests.log`、`battle-player-host.log`、`battle-player-peer.log`。這仍不代表 renderer／實際 UI 操作通過。

此輪修正了非地面動作誤帶地面座標、跨元件 Awake 順序、每秒兩次探測在固定視窗邊界被誤限流，以及缺少 result 協定。probe 改用既有 `x/time/rate`，平均 2/s、burst 2；補上具體拒絕代碼。`TerminalTests` 驗證同 tick 最終狀態、重複結果、result-only 重接及錯誤結算的原子拒絕；服務 test／vet 與五個 Go 選用模組全數實體移除的 test／vet 也通過（`artifacts/remake-server-removal.log`）。
