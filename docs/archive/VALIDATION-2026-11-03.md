# 遷移前驗證紀錄（截至 2026-11-03）

本文保留遷移前的原始紀錄。版本、路徑、模組數及測試結果僅適用於當時；目前驗證入口見 [VALIDATION](../VALIDATION.md)。程式碼區塊中的路徑以當時專案根目錄為起點。

# 驗證方式與目前結果

## 接收佇列恢復、移動攻擊與技能調整（2026-11-02）

重現接收佇列停頓後塞滿的錯誤：原本第 65 筆起每次 WebGL callback 都會拋 IOException。傳輸層現在清除過期整批訊息，只放入一次 receive_overflow 終止事件，後續 callback 在該連線生命期內被忽略；GameFlow 使用既有同身份重連與完整快照／delta 基準恢復。保留 64 筆上限，沒有增加無界記憶體或任意丟棄 delta。傳輸測試連續注入 10,000 筆確認只通知一次，原生 WS／WT 與 20 項 browser transport／bridge 來源測試通過。實際 API/C# 流程停收 5 秒後，驗證同一身份與新連線 generation 恢復控制。

A＋左鍵地面啟用移動攻擊：射程內取得有效敵人後停步普攻，失去目標後續走原路線，不追逐射程外敵人。右鍵移動／S／衝刺覆寫；桌面一般移動與移動攻擊分別顯示綠／橘終點。鏡頭預設 8、設定 5～10。弓箭手普攻用普通箭矢呈現，鎖定傷害規則保留；火焰箭技能完整射程約 12.63。技能 CD 調為弓箭手 90 ticks、坦克 120 ticks、法師 150 ticks（3／4／5 秒），content 更新為 g2-targeted-combat-2。

法師手動目標徑向限制至最大施法距離，客戶端預覽與送出座標一致，authority 對 skill 也做限制（舊 fire 診斷維持原本嚴格驗證）。自動施法把爆炸半徑及敵人碰撞半徑計入，只有合法落點實際可碰到敵人才出手，無目標時不消耗技能。Go 驗證射程邊緣落點仍命中圈外敵人及冷卻保留；C# 驗證有／無可命中目標、最近選擇、掩體與徑向限制。

紀錄：`attack-move-domain-tests.log`、`attack-move-client-tests.log`、`attack-move-transport-tests.log`。舊測試中固定的短冷卻等待與射程幾何已依新的設定調整，重播驗證包含死亡後正常復活。

最終 Windows／WebGL 建置通過；真正 Player 通過終點標記、A 移動攻擊取得敵人、普攻／取消後搖、多指操作、衝刺清除舊指令及法師邊緣施法。GPU/UI 驗收共 23 張（桌面 12、觸控版 11），包含普通普攻箭、火焰技能箭、綠色移動終點及拉遠視野。共用 UI 字型從固定授權來源重新產生，補足操作提示使用的「住」字，651 字形；meta/GUID 為零錯誤及零警告，依賴檢查通過。

VM release `20261102T083139Z-fbeadd3b` 已部署；HTTPS／WASM／來源隔離／安全 Cookie、雙人／重連／重播、實際停收 5 秒後溢位恢復，以及真正 Player 對 VM 戰鬥回歸均通過。紀錄：`attack-move-player-tests.log`、`attack-move-deployment.log`、`attack-move-remote-tests.log`、`attack-move-remote-player-tests.log`、`attack-move-meta-check.json`。畫面為 Player 本身渲染；Work 設定仍停用瀏覽器操作工具，最新 WebGL 人工操作與實體手機觸控未複驗。

## 朝向、情境預覽與操作調整（2026-11-02）

普攻目標朝向與技能 Aim 分離，涵蓋本機呈現及傳送到權威的 facing；按住對應操作才畫範圍，手機拖曳後才畫選敵／方向／落點。電腦 Q/E 改成按住預覽、放開施放。衝刺清除舊點地／追擊命令，手機仍保留當下實際搖桿輸入。鏡頭略拉高／遠，預設視野 7，鏡頭設定使用新版偏好值。大型地圖一般移速乘 0.8；API、伺服器各角色／AI、客戶端預測從同一地圖規則取得速度，原 sample 小場不變。火焰箭沿用現有授權素材，既有彈體池回收時清掉拖尾和粒子，命中火花另以有界池呈現。

Go domain 與純 C# 流程通過，新增一秒權威移動 4.8 單位／全角色速度與衝刺不變／含速度倍率的 journal 重建驗證。真正 Player 驗證普攻放手後，模型及網路快照仍朝敵人；往前移動後往後衝刺，不會恢復舊路線；按住只有範圍、拖曳出現目標、放手隱藏，並涵蓋既有多指回歸。

GPU／UI Toolkit 產生兩組各十張畫面，包含無輸入、普攻射程、技能射程、拖曳方向／法師落點、衝刺射程與火焰箭。這是 Player 自身渲染，不是實體手機／瀏覽器畫面。紀錄：`combat-polish-domain-tests.log`、`combat-polish-client-tests.log`、`combat-polish-player-tests.log`、`combat-polish-player-final-tests.log`、`artifacts/hud-review/`。

Windows／WebGL 最終建置通過，Unity meta／GUID 檢查為零錯誤及零警告，依賴邊界檢查通過。VM release `20261102T074556Z-f1192216` 已部署；HTTPS／WASM／來源隔離／安全 Cookie、雙人流程、重連、重播與真正 Player 對 VM 戰鬥回歸均通過。紀錄：`combat-polish-deployment.log`、`combat-polish-remote-tests.log`、`combat-polish-remote-player-tests.log`、`combat-polish-meta-check.json`。

Work 設定仍停用瀏覽器操作工具，本輪未做最新 WebGL 人工點擊或實體手機驗收；多指邏輯由真正 Player 使用相同輸入流程驗證，不能代替裝置端驗收。


## 鎖定普攻、主動技能與大地圖（2026-11-02）

Example 使用獨立的 60×60 地圖、滿版近距離跟隨鏡頭、小地圖與鏡頭距離設定；原 sample 的地圖不變。新的 attack／skill／cancel 指令與 targetID／targetLife 由權威驗證，普攻前搖完成才造成鎖定傷害，取消前搖無傷害，取消後搖不重設攻速。方向／落點技能與普攻冷卻分離，既有 fire 保留作 sample／診斷相容指令。

Go domain、example server、純 C# 流程與 browser-source 測試通過。新增伺服器風險案例涵蓋取消／衝刺、死亡／復活、斷線、出射程及隊伍變更；完整 journal 可重建新普攻／取消／技能。C# 測試驗證逐邊掃掠的繞障礙物尋路、追擊、目標生命、方向選敵，以及移動／普攻／技能獨立手指。

真正 Windows Player 在隔離 API 驗證點地繞行、鏡頭跟隨、追到射程、取消前搖不扣血、出手後走位與冷卻保留、獨立方向技能、兩種衝刺方向、delta 快照、普攻 UI 點擊、按住跨兩次冷卻、多指持續走位、選單重設。高 FPS 驗證發現並修正方向更新繞過 30 Hz 排程造成限流：線路輸入上限 60 Hz，逐幀預測保留。

GPU／UI Toolkit 渲染十張桌面／觸控畫面，驗證滿版、中央交戰區無 HUD 遮擋、四個觸控區不相交，三種技能預覽可見。`artifacts/hud-review/` 為 Player 自身渲染，非實體手機或瀏覽器截圖。主要紀錄：`moba-domain-tests.log`、`moba-client-tests.log`、`moba-browser-tests.log`、`moba-player-tests.log`、`combat-player.log`。

Windows／WebGL 最終建置通過，VM release `20261102T065922Z-9050113e` 已部署；HTTPS/WSS、兩人流程、重播及新戰鬥 Player 對 VM 回歸全數通過（`moba-remote-tests.log`、`moba-remote-player-tests.log`、`moba-deployment.log`）。普攻遠端前搖動畫、鎖定命中提示及手機按鈕冷卻顯示已接入。最後追加驗證：技能尚未就緒／無效衝刺不會意外取消普攻（`moba-final-domain-tests.log`）。

Work 設定仍停用瀏覽器操作工具，因此最新 WebGL 人工點擊／畫面及實體手機觸控未複驗；不把 Player、來源流程或 HTTP 檢查當作瀏覽器／手機實機證據。

以下保留先前版本的修正紀錄。

## 手機輕點自動選敵（2026-11-02）

右側攻擊區以手指相對起點的位移區分輕點與拖曳；輕點選取射程內最近的有效敵人，拖曳維持手動瞄準。使用目前採樣的顯示位置、職業射程及地圖遮擋規則；沒有目標時保留方向攻擊。弓箭／近戰檢查掩體，法師允許越過掩體但落點必須有效，命中／冷卻／傷害仍走原伺服器動作路徑。

`REMAKE_TAP_GESTURE` 驗證偏心輕點、抖動、拖曳後返回中心、取消、重設與一次性發動；`REMAKE_AIM_ASSIST` 驗證目標過濾、最近距離、同距離穩定順序、三職業邊界與掩體；100 組多指回歸也通過。紀錄：`artifacts/tap-aim-native-tests.log`。真正 Windows Unity Player 連 VM 通過「初始朝北、東側有敵人，輕點攻擊後自動轉向並降低敵人血量」，以及手動拖曳、衝刺、持續按住移動跨三次攻擊與選單重設。紀錄：`artifacts/tap-aim-player-tests.log`。此為正常 Unity／網路路徑驗收，實體手機觸控仍需裝置端複驗。

Windows／WebGL 建置、Unity meta 與依賴邊界檢查通過，已部署 VM release `20261102T034432Z-d67b1ce6`；根網址遊戲資源、HTTPS／WSS、雙人流程與重播回歸通過。紀錄：`artifacts/tap-aim-deployment.log`、`artifacts/tap-aim-remote-tests.log`。

## HUD 遮擋與瞄準預覽修正（2026-11-02）

頂部比分改為單列，生命／技能／觸控按鈕留在側邊，相機使用受保護場地區域；從 sample 抽出共用 AttackRangePresentation 並保留 GUID，恢復弓箭走廊／近戰扇形，法師接回原有 GroundTargeting 與範圍圈。Windows 真正 GPU／UI Toolkit 在 989×446、844×390、1280×720 產生桌面及觸控兩組共十張圖，檢查 HUD／搖桿不與場地相交，並檢視三種角色的預覽。圖與日誌：`artifacts/hud-review/`、`hud-review-desktop.log`、`hud-review-touch.log`。這是 Unity Player 自身渲染畫面，不是實體手機／瀏覽器截圖。

重跑：`./tools/Test-HudLayout.ps1`、`./tools/Test-HudLayout.ps1 -Touch`。需要 Windows Development Player 與圖形環境，使用獨立測試訪客，不讀寫原玩家身份。`Test-TouchPlayer.ps1` 的持續多指操作回歸亦通過。

## 手機操作與橫屏追加（2026-11-02）

多指中斷修正：實際手機輸入改為每幀讀取 `Input.GetTouch` 並以 `fingerId` 維持控制所有權，UI capture 只保留滑鼠測試用途；瀏覽器尺寸微調不再清空搖桿，真正旋轉／背景／選單仍會安全停止。`REMAKE_MULTITOUCH` 覆蓋 100 組左手持續按住、右手開始／結束瞄準與衝刺、觸控陣列重排及遺失手指清理。真正 Unity Player 另通過三次攻擊硬直後無須放手即可續走，以及實際 UI 捕捉轉移不影響取樣中的左手。紀錄：`artifacts/multitouch-native-tests.log`、`touch-player.log`、`multitouch-deployment.log`。實體手機仍需裝置端複驗，不把模擬手指事件視為實機觸控證據。

完整 example 已加入多指虛擬移動／瞄準搖桿與衝刺，手機網頁以橫屏為主，提供直向提示、使用者觸發的全螢幕／橫向鎖定嘗試與手動旋轉備援。Windows／WebGL 建置通過；真正 Windows Player 在 844×390 下，透過 UI Toolkit 指標事件走正常客戶端與 VM 伺服器，驗證移動、放手停止、放開瞄準後攻擊、衝刺及選單清除捕捉（`artifacts/touch-player.log`）。這是實際 Unity／網路接線驗收，並非實體手機觸控測試。

純 C# 多指所有權／死區／對角速度／取消／一次性動作／背景重設測試、四項橫屏瀏覽器邏輯測試、網頁來源流程與 VM HTTPS／WSS 雙人回歸通過。檔案：`touch-native-tests.log`、`mobile-orientation-tests.log`、`touch-browser-tests.log`、`touch-remote-tests.log`。Meta／GUID 檢查無錯誤。重跑：`./tools/Test-TouchPlayer.ps1`、`node --test ./examples/browser-common/mobile-view.test.mjs`。實體 Android／iOS、手機瀏覽器全螢幕政策與效能仍需對應裝置驗收。

以下命令從 **RealtimeFramework 根目錄**執行。需要 PowerShell 7、Go、Node.js、Python 3 與 .NET 8 SDK；工具會載入本機 Go，C# 工具可用 `-DotNet` 指定 SDK。Unity 使用 6000.6.6f1，需有效授權與目標平台支援，建置前關閉同一專案的 Editor。

## 目前證據（2026-11-02）

| 項目 | 已驗證範圍與紀錄 |
| --- | --- |
| 遊戲內選單導覽修正（11-02） | 選單／設定／框架改為同一面板的分頁，固定繼續按鈕，支援套用後自動返回、統一 Esc 與未套用變更處理。導覽狀態測試及 Windows 實際操作通過：一般設定一步返回且 generation 維持 1、套用固定平滑後重連為 2 並自動回原局、切頁保留草稿、提示留在設定／捨棄返回，以及無變更 Esc 直接繼續。Windows／WebGL 建置通過；[操作畫面與紀錄](../../artifacts/menu-ux-review/REVIEW.md) |
| 框架技術選項操作修正（11-02） | 選項改為直接點選並標示選取狀態，補上半徑／收件延遲滑桿與數值；套用列固定在底部，顯示修改、套用及重連結果。Windows 隔離服務實際切換固定平滑／附近物件、半徑 12→26.7、延遲 0→143 ms、歷史／補償連動、套用後重新開啟保留，以及對戰中切換差分後重連繼續遊玩。見 [操作紀錄](../../artifacts/technical-settings-review/REVIEW.md) |
| 重播播放器排版追加（11-02） | 底部置中播放器、統一深色按鈕、金色時間軸、直接點選倍速與窄版換列。Windows 實際選檔載入使用者的 18.8 秒重播，檢視寬／480×720 窄視窗，確認暫停、跳轉、逐格、倍速選取及離開；最終 Windows／WebGL 建置、參考編譯通過。瀏覽器工具仍停用，未宣稱新版 WebGL 操作通過。見 [畫面及紀錄](../../artifacts/replay-ui-review/REVIEW.md) |
| Unity 重播選檔 | Windows 系統選檔與 WebGL 本機檔案 bridge 已接入原播放器，網址欄位收進進階選項。Windows／WebGL 建置、參考編譯與資產 GUID 檢查通過；12 項實際 `.jslib` 的 DOM 替身測試覆蓋選擇／取消、32 MiB 邊界、過期事件與 Blob 清理，已納入 `Test-Browser`。本次實際選檔操作受工具阻擋，未宣稱通過；見 [選檔紀錄](../../artifacts/replay-picker-review/REVIEW.md) |
| 原版美術／UI 還原追加 | 選單 11、根網址直接進入 `/unity/`，Canvas 留在 `/diagnostics/`；Windows／WebGL 重新建置，樣式依賴確認實際引用原字型及五張 UI 圖。Windows 真實視窗驗證大廳、選角練習、自動收起載入面板、原版 HUD／血條、攻擊、自然結算／返回、房間捲動及固定底列；另修正核取方塊同列與空白訊息佔位。[本輪紀錄](../../artifacts/art-parity-review/REVIEW.md)。最新 WebGL 畫面複驗仍受 Local browser use 停用阻擋，未宣稱已驗收 |
| 框架回歸 | 15 個 Go module 的 test／vet、20 項 SDK JS、4 項 Shared World JS、32 項 sample JS 通過；`artifacts/remake-final-framework-tests.log` |
| 純 C# 完整應用 | 真實兩個 C# SDK 用戶端：身份／房間／分隊選角／準備／載入、移動／動作、差分恢復、時鐘、原身份席位續接、HTTP 取消／過期回覆、練習 AI、結算與錄製播放；另覆蓋慢速背景查詢與已消失房間的冪等離開。`artifacts/remake-final-native-tests.log` |
| 結果與重播 | 同 tick 最終結果、重複結果、result-only 重接、錯誤結算原子拒絕；錄製保留與最後快照同 tick 的結算，替換尾幀以維持唯一 tick。播放實際 Go 匯出檔、seek／速度／暫停、快照往返與錯誤檔拒絕，由上述 C# suite 覆蓋 |
| 網頁來源流程 | 正式頁面、實際 C# 主機／SDK 與 Go HTTP3 服務完成雙人與離線播放流程；`artifacts/remake-final-browser-tests.log`。DOM／Canvas 使用替身，不代表真實瀏覽器渲染 |
| 真實瀏覽器完整範例 | IAB 實際兩頁籤完成訪客、私人房間、容量／分隊、選角準備載入、鍵盤移動／攻擊、自然 180 秒結算與手動返回；錄製檔實際下載後重新選檔載入，確認暫停、2 倍速與跳回首幀。`artifacts/browser-review/` 保存畫面與 1834 幀原始錄製檔 |
| 真實 Unity WebGL | 原素材／中文／訪客／法師與弓箭手練習、攻擊／衝刺、面板上方 WASD、文字欄位隔離、自然結算／離開成功。原生瀏覽器 WT 真正中斷後同身份世代 1→2→3，5400 tick journal 重建通過；643 幀 Blob 下載檔重新載入，2 倍速、暫停／跳轉與固定寬度網址欄位通過。`artifacts/browser-review/REVIEW.md`、`webgl-reconnect-authority.json`、`webgl-reconnect-journal.log`、`webgl-recorded.playback.json` 與畫面 |
| 原遊戲規則對照 | 共用原地圖、碰撞、推擠、固定步進，30／60／144 FPS 與恢復案例通過；`artifacts/remake-shared-domain.log` |
| Unity SDK Editor | 獨立 SDK 契約、codec fixture 與 32 KiB／UTF-8 邊界通過；`artifacts/remake-sdk-unity-tests.log`、`sdk-tests.log` |
| 第 3～9 課 Unity | 七課雙平台重新建置、14 個真正 Windows Player 的兩人 roster／ACK／移動通過。實際 WebGL 覆蓋雙人預測／圈線、斷線重試、物件建立／移除／回覆、Replay、AOI 13→2、Codec 缺基準恢復與歷史回溯命中。`artifacts/lesson-browser-fix-builds.json`、`lesson-player-browser-fix-tests.log` 及 `browser-review/lesson-*.jpg` |
| 完整遊戲 Unity | 新 Battle 場景、原角色動畫／場地素材、JSON、URP 與 BuildReport 通過 Windows／WebGL 建置；見 `artifacts/example-Windows.log`、`example-Web.log` |
| 真正雙 Windows Player | WS 與原生 HTTP3 helper 均完成房間／角色／載入、權威 ACK／移動、遠程／近戰、同席位重連、法師傷害／死亡／復活、據點、result／返回、錄製／播放；三份 journal 各由獨立 Go 程序重建通過。`artifacts/battle-player-tests.log`、`battle-player-wt-tests.log`；使用 headless／nographics，真人 UI 操作另驗 |
| 原生 3D 渲染 | 實際 GPU／URP 相機離屏輸出對戰、練習與播放共五張圖；空白與缺 shader 檢查通過，已檢視角色／地圖／掩體／據點。`artifacts/battle-player-render-tests.log`、`artifacts/battle-render/`。不涵蓋 UI 或真人輸入 |
| 原生 UI 操作 | 真實 Windows 視窗完成訪客、選角、法師練習、滑鼠施法、衝刺位移／冷卻與離開；檢視中文字形與按鈕對比。`artifacts/browser-review/native-practice.png`、`battle-native-ui-final.log`。雙人完整流程由真正 Player 自動驗證，沒有把基本 UI 檢視擴大為所有組合人工操作 |
| 選用套件移除 | 十個選用套件不存在的純 C# 完整連線通過；原 sample 與新 example 均已實體移除十個 package 建置，Managed 只剩 Core／Transport。最後修正版精簡雙 Player 的遊戲／恢復／錄製也通過；播放未安裝，入口明確停用。見 `artifacts/remake-final-removal-tests.log`、`remake-unity-removal.log`、`remake-example-unity-removal.log`、`battle-core-player-tests.log`；原 3D 備援驗證另見 `battle-core-player-render-tests.log`、`battle-core-render/manifest.json` |
| Go 選用套件移除 | 五個 package 全部實體移除後 SDK／共用遊戲／sample／新應用的 test／vet 通過；`artifacts/remake-server-removal.log` |
| 傳輸邊界 | 原生 WS 的 fragmented UTF-8、32 KiB、有界收送、epoch／取消；實際 WT helper 的 pin／可靠串流／datagram／兩實例／替換通過。`artifacts/transport-platform-tests.log`；Linux amd64／macOS arm64 僅交叉編譯，未實機執行 |
| 來源與文件 | 依賴守門、7 個污染反例、PowerShell 語法、`.meta`／GUID／參考編譯通過；外層原專案 210 份基準檔雜湊維持不變。文件報告 `artifacts/lessons-8-9-review.json` |

## 日常檢查與完整應用

```powershell
python ./tools/Check-Dependencies.py
python -B -m unittest discover -s ./tools/tests -v
./tools/Test.ps1
./examples/rumblebound/scripts/Test-Battle.ps1
./examples/rumblebound/scripts/Test-Browser.ps1 -WebTransport
```

`Test-Battle` 使用真正原生 C# SDK／Go 服務；`Test-Browser` 執行正式 JavaScript，但 DOM／Canvas 是測試替身。Google start／attempt cookie／錯誤 token／nonce 重複使用已覆蓋，沒有代替真實 Google 帳號登入。完整操作見 [範例 README](../../examples/rumblebound/README.md)，原規則與 FPS 對照用 `./tools/Test-GameDomain.ps1`。

### Rumblebound 整合修正（2026-11-03）

- `Test-Battle.ps1`：六種診斷實際採樣、上下行延遲／抖動／丟包、100% 上行輸入丟失時可靠載入與探測仍可用、未送達動作不假確認、重連清佇列、真實多批快照的 view／陣列重用及新世代重建、回放匯入／seek 通過。另以真實 loopback WebSocket 驗證可靠結算後立即關閉連線，結算仍在下行延遲／暫停時不送資料、不因 live timeout 遺失結果。
- `Test-BattleRemoval.ps1`：隔離樹沒有十個選用套件來源，實際雙人流程、移動、projection 快照備援、原身份重連與練習通過。
- `Test-Browser.ps1`：22 個正式 HTML／JavaScript 設定及選檔案例通過，另有真正 C#／Go 服務的雙人操作、codec、重連、錄製播放及缺少 Unity 建置入口測試。成功施放案例先等待權威冷卻，避免把合法拒絕當成確認失敗。
- 使用 Unity 6000.6.6f1 的 .NET Standard／原生及 WebGL 參考組件，配合保留的 Player 套件 DLL，完成 SDK、遊戲、呈現、應用、兩平台 UI 與傳輸條件編譯。`.meta` 檢查沒有錯誤；清理後缺少 PackageCache，21 個套件腳本參考只能列為未確認警告。

第一批驗證當時沒有重建 Unity Player、執行 Editor 匯入或驗證實際畫面／觸控；後續結果見下節。參考編譯及 DOM／Canvas 替身不取代 Player 驗收。此輪未改 Go 歷史／journal 格式，也沒有把遊戲生命、碰撞、投射物終點或物件池搬入 SDK。

### 深層整合與配置改善（2026-11-03）

- `Test-Battle.ps1`：固定步重播在裁切／內部／過期／尾段歷史缺口時不再補算未知時間；獨立遠端接觸衰減、生命重設及碰撞安全通過。四筆取消動作在雙向延遲／抖動／丟包下依序、各確認一次，滿四筆時明確拒收第五筆；一次記錄為 0.955 秒、probe RTT 0.249 秒，沒有宣稱消除 stop-and-wait 的 RTT 成本。
- 回放摘要測試保留 Unicode、surrogate、跨字串／分塊及同 tick 替換的舊摘要定義；1 MiB 案例的原生 .NET 累計配置由 7,275,496 bytes 降為 6,040 bytes。此數字只涵蓋雜湊計算，未包含最終 JSON、Unity／WebGL heap 或手機量測。實際約 32 MiB 容量停止與最終輸出大小限制通過。
- 彈體視圖重用清單：真實生成／消失後保持同一清單，原生及 Newtonsoft JSON 只輸出有效 Count，回放陣列相容。Snapshot 解析次數測例與延遲結算／關閉連線／長暫停案例通過。
- `go test -count=1 ./sim ./service`、停用 lagcomp／history 的 sim 測試及 `go vet ./sim` 通過，含回放確定性。新增案例驗證 200ms 回溯窗依 TickRate 向下取完整 tick，以及動作成功／拒絕的提早 snapshot、start 優先、定期廣播不重複、掉包與重連去重。
- `Test-GameDomain.ps1`、`Test-BattleRemoval.ps1`、`Test-Browser.ps1`（22 個 Node 案例及正式 C#／Go 服務流程）、原生／WebGL UI 與 SDK 參考編譯通過。
- Unity 6000.6.6f1 實際 Editor 匯入與最終 Windows／WebGL 建置通過，見 `artifacts/example-Windows.log`、`example-Web.log`。弱網設定新增的「丟」字由固定來源及版本的字型工具重新產生；目前 652 字形、185,436 bytes，既有 GUID 不變。套件還原後的 `.meta`／GUID 檢查為 103 份、零錯誤及零警告。
- `Test-BattlePlayer.ps1` 的 WebSocket 與原生 WebTransport 均通過真正雙 Windows Player：房間／載入、權威移動與遠近程動作、同身份重連、結算／返回、練習場傷害／死亡復活／據點，以及錄製／播放；每種傳輸各三份 journal 均由獨立 Go 程序重建通過。驗收腳本依正式地圖掩體尺寸選合法目標，並使用正式尋路繞過掩體；沒有更動遊戲碰撞或出生點。此次使用 headless／nographics，沒有把結果擴大為新版 UI、GPU 畫面、真實 WebGL 操作或行動裝置驗收。

持續 held-state 輸入仍由伺服器每 tick 採最後收到的方向，固定步預測仍按區間起點取樣；快速幀內轉向可能需要權威校正。本輪修正確切的歷史缺口重播問題並減少動作確認排程等待，沒有改成帶子 tick 時間的輸入協定或多動作在途佇列。

## Unity 建置與執行

```powershell
./tools/Test-Sdk.ps1
./tools/Test-UnityLessons.ps1 -Sample
./tools/Build.ps1 -Project RumbleboundExample -Target Windows
./tools/Test-BattlePlayer.ps1
./tools/Test-BattlePlayer.ps1 -WebTransport
./tools/Build.ps1 -Project RumbleboundExample -Target Web
```

`Test-BattlePlayer` 自建隔離服務、兩個真正 Player，正常停止服務後再驗證 journal；只清理它建立的程序。`-Render` 使用圖形環境並讓 Unity 相機離屏渲染到 `artifacts/battle-render`，檢查 3D 內容／空白畫面／缺 shader。它不讀取桌面或視窗 framebuffer，也不涵蓋 UI／滑鼠鍵盤互動。

第 3～9 課的專案名為 Prediction、NetworkRecovery、ObjectsActions、Replay、Interest、Codec、History。用相同 `Build.ps1 -Project <名稱> -Target Windows`／`Web` 建置，再跑 `./tools/Test-LessonPlayers.ps1`；各課聚焦連線工具集中於 [Unity 教學說明](../../examples/unity-common/README.md)。Minimal、SharedWorld、Interpolation 也有同一建置入口；Minimal 使用 `Test-Minimal.ps1`，Shared World 使用 `examples/shared-world/scripts/Test-SharedWorld.ps1 -NativeClient`。

原 sample 保持獨立建置入口 `Build.ps1 -Project Rumblebound`，其操作回歸是 `samples/rumblebound/scripts/Test-Clients.ps1`；sample 與新 example 不共用測試旗標。伺服器 replay／export 與原 sample 播放驗證見 [REPLAY](../REPLAY.md)。

## 選用套件與傳輸檢查

```powershell
./tools/Test-BattleRemoval.ps1
./tools/Test-OptionalRemoval.ps1 -Project RumbleboundExample -Module all
./tools/Test-OptionalRemoval.ps1 -Project Sample -Module all
./tools/Test-ServerRemoval.ps1 -Module all
./tools/Test-Transport.ps1
```

前者在隔離來源樹執行完整流程；兩個 Unity 移除工具暫移真實 package、移除 manifest／註冊與必要 asmdef 參考，建置並查核 DLL，最後還原來源。Go 工具配合排除 tag 測試。**移除工具必須序列執行，不能與共享來源的 Editor／建置並行。**新 example 的精簡產物另存 `Builds/NoOptionalNetcode`，不覆寫一般 Player；可用 `Test-BattlePlayer.ps1 -PlayerPath ./examples/rumblebound/client/Builds/NoOptionalNetcode/RumbleboundExample.exe` 執行備援流程。

## 操作驗收與平台界線

既有瀏覽器證據是在工具開啟、額度補充後取得；原版 UI 還原時，工具再次回報 Local browser use 停用，使用者回覆已啟用及重新初始化後仍無法使用。因此新介面的 Windows 真實 UI 操作及兩平台建置證據，與先前 WebGL 操作分開記錄。修正包含 `/unity/` 重新導向循環、背景查詢鎖住操作、結算逾時離開、WebGL 中斷診斷、Collider 裁除、輸入焦點、過期房間離開、長網址、載入面板切換及 UI 資源路徑。共用字型目前為 652 字形，建置檢查實際 UI 字形與樣式引用。11-01 新增選檔時，Windows 擷取只得到桌面；11-02 已補驗 Windows 系統選檔、實際播放與寬／窄視窗。WebGL 選檔與新版畫面仍未完成實際操作複驗。

| 界線 | 目前範圍 |
| --- | --- |
| UI／輸入 | 實際操作覆蓋上述核心情境，沒有宣稱每個角色與所有選項組合都人工跑過；純來源、headless 與 GPU／UI 各有不同證據 |
| 瀏覽器 | 實際 IAB／Chromium，涵蓋 WebGL bridge 與原生 WT；不把結果擴大為所有瀏覽器或行動裝置 |
| Google | 依使用者要求，本輪使用訪客，既有 Google 接線保留並暫緩真實帳號驗收；不是本輪剩餘工作 |
| Linux／macOS | helper 僅交叉編譯，未在對應 OS 執行；未宣稱實機通過 |

功能狀態見 [作業表](worklogs/REMAINING_WORK.md)。已退休入口與早期工具限制保留在 [階段紀錄](../archive/VALIDATION-stages-2026-11-01.md) 及 [2026-10-31 紀錄](../archive/VALIDATION-2026-10-31.md)，不代表目前狀態。
