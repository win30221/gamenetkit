# 可玩版本驗收記錄

**八張牌、滿版鏡頭、重連／重賽的最新實作與驗收見 [P0_VALIDATION.md](P0_VALIDATION.md)。** 以下保留三兵種版本的歷史紀錄，不代表目前的功能限制。

日期：2026-11-12。目前包含 PLAN 第 1、2 階段及第 3 階段功能／技術驗收；
戰術平衡與樂趣仍待真人試玩，不代表完整八張卡遊戲已完成。

## 最新：拖牌、攻擊鎖定、推擠與主堡啟動

Release `20261112T121030Z-16632c97`、規則版 `three-units-2`，保留 protocol 2。
原始規則檔 SHA-256 為 `64f61d01ca1f37094c24985e6b6a09e1b8034af7945c2c9147dd98b05ffc4126`，
舊客戶端需要重新建置或重新整理。

- 卡牌支援滑鼠與觸控拖曳，帶有跟隨卡片、綠／紅部署圈，放開合法位置才送出。
  拖回牌區、非法位置、Esc、失去 pointer capture、離房或戰局結束時取消；保留點牌後點地。
- 士兵首次出手後鎖定目標直到死亡，目標走出索敵／攻擊距離時會繼續追擊。
  塔也保持原目標；目標走出塔射程時等待，不改射另一個活目標。
- 部署只驗證地圖、建築、能量與單位上限，允許與已有士兵重疊。
  生成與移動後由伺服器以穩定 ID 順序解圓形碰撞；友軍可推擠，敵我重疊也會分開。
- 主堡開局 `active=false`，受到傷害或己方任一前塔倒下時永久啟動，該 tick 可反擊。
  UI 顯示「主堡待命／主堡啟動」；主堡待命期間仍可被攻擊。

Go tests 與 vet 通過，含：三種兵鎖定、塔／士兵死亡後重新索敵、目標離開距離、
打塔時出現防守兵、主堡左右任一塔倒下與直接受傷啟動、同點部署九人與重送不重扣、
邊界附近推擠、移動推友軍及敵我碰撞。先前跨橋與左右路線測試亦通過。
WebGL release 建置 Succeeded、0 errors、155.60 秒；Unity 資產檢查 202 個檔案，
0 errors、0 warnings。

最終公開 Chrome WebGL 驗收：房間 `RQMZCQ`、match `RQMZCQ-2`。
滑鼠拖牌、非法河道放牌、拖回牌區及 Esc 取消均通過；取消操作沒有送出 deploy。
在相同座標 `(2.51088,-3.57759)` 連續拖放兩隊劍士，兩次各生成三人且推開。
另以 390×844 viewport 與瀏覽器合成的原生 touch events 拖出盾衛成功，
此為模擬觸控，非手機實機驗收。無 JavaScript 例外。

正式伺服器雙 WSS 實戰：房間 `8R83QN`、match `8R83QN-1`。
同點連下三隊全數接受，檢查 1,236 次活目標鎖定狀態，未發生換敵。
敵方右前塔倒下後主堡啟動，己方未受攻擊的主堡保持待命；最終藍方摧毀主堡，
兩端 winner=0、reason=keep_destroyed 一致，共收到 874 份快照。
快照區分 `target`（目前意圖）與 `lockedTarget`（實際攻擊鎖定），避免把舊 attackTick
誤認為對新目標已出手；目標可能在兩次快照之間死亡。

證據位於 `artifacts/tower-duel/drag-combat`：`browser-result.json`、
`server-live-result.json`、`server-live-snapshots.json`、`build-result.json`，
以及 `valid-preview.png`、`invalid-preview.png`、`repeated-squads.png`、`touch-deploy.png`。
本機 80 單位、300 ticks 的 Go 單場量測總計 97.55 ms、最慢 tick 16.93 ms，
不是 VM 多房或 Unity 滿員渲染的效能保證。

## 劍士卡住與左右路線

Release `20261112T114048Z-0c7e5fd6` 修正兩個已重現的伺服器問題：

- 劍士三人共用相近路點，橋頭只嘗試單側閃避，又會被拉回已越過的路點；
  塔前也會爭同一個最後路點。現在嘗試雙側避讓、略過已走過的路點，
  尋路考慮現存士兵，受阻時以固定間隔重算，並在目標周邊找可進攻的位置。
- 原本總是選最近的敵建築，導致右塔倒下後轉走左路。現在由出兵中心選較近的橋，
  小隊三人共用路線，同路前塔倒下後鎖定主堡。附近敵兵仍優先交戰。
  若交戰把士兵帶進另一座橋，可先完成當下跨橋，再回到原路線的建築目標。

Go 全部測試及 vet 通過。新增 70 組小隊落點候選的雙席位跨橋／攻塔驗證
（碰到塔或河的非法編隊位置跳過）、36 組三兵種／雙席位的前塔倒下選路驗證、
4 組中心線附近的小隊同路驗證，以及雙席位交戰後退出另一座橋的驗證。
原本失敗的 `(2.5,-3)`、`(-2.5,-3)` 橋頭落點已可全員跨橋。

最終公開站台使用兩個真正 WSS 客戶端驗證，房間 `Y3798H`、match `Y3798H-1`：
劍士 101–103 均通過右橋；右前塔摧毀後，再從右側派出劍士 105–107，三人仍全數通過右橋、
目標為主堡 4，左前塔仍為 380 HP。雙端 tick 742 的單位快照一致。
軌跡保存在 `artifacts/tower-duel/movement-fix/remote-result.json`。

另以真正 Chrome WebGL、720×1000 viewport 的滑鼠選牌與落點測試，
房間 `G5T7CD`、match `G5T7CD-2`，部署中心約 `(2.51,-3.58)`。
三名劍士在 tick 108 至 214 均從己方移動到敵方，最後 Z 分別為 2.42、3.29、4.78，
畫面可見三個模型過橋，未見 JavaScript 例外。快照軌跡在 `browser-result.json`，
實際畫面在 `browser-squad-walking.png`、`browser-squad-crossed.png`。

另做本機 Windows Go 單場 80 單位、300 ticks 的尋路量測：總計 448.15 ms，
最慢 tick 14.39 ms，硬體為 i7-12650H。這是本機伺服器單場量測，
不代表 VM 多房或 Unity 80 單位渲染效能已驗收；資料保存在 `local-validation.json`。

## 網頁版複製房間碼

Release `20261112T105245Z-d40fdafc` 修正「複製房間碼」：原本只設定
`GUIUtility.systemCopyBuffer`，只更新 Unity 的複製緩衝區，沒有主動寫入瀏覽器剪貼簿。
以實際點擊確認原按鈕沒有改變測試剪貼簿內容後，新增 WebGL JavaScript 橋接，
改用 `navigator.clipboard.writeText`；僅在 Promise 成功後顯示「已複製房間碼」。
失敗或 API 不可用時提供唯讀代碼欄位、手動複製、重試與關閉。離房或開戰會取消舊請求與視窗。

正式站台 Chrome 157、1280×600 的兩個真正 WebGL 客戶端已驗證：

- 點擊複製後讀回系統剪貼簿，內容為房間 `LWYM8Y`；實際按鈕顯示成功。
- 第二個視窗用 Ctrl+V 貼上，成功加入 `LWYM8Y-7`，取得 seat 1；沒有手動重打代碼。
- 模擬 Clipboard API 拒絕後，唯讀代碼正確，Ctrl+C 手動複製及恢復權限後的重試均成功。
- 未見 JavaScript 例外。讀取剪貼簿的測試權限是在原始按鈕完成實際寫入後才授予，
  正常複製測試未事先授予寫入權限。獨立的瀏覽器橋接測試另涵蓋手動複製與重試。

最終 WebGL 建置 Succeeded、0 errors、128.17 秒；Unity 資產檢查 200 個檔案、
0 errors、0 warnings。首次冷下載較慢，驗收等待資源完整下載後執行。
完整結果與截圖位於 `artifacts/tower-duel/copy-fix`，包括 `result.json`、
`copied-live.png`、`pasted-room.png`、`manual-live.png`、`retry-live.png`。

本次使用 `Deploy.ps1 -ClientOnly`，比對 API／協定／代理／傳輸檔相同後更新 WebGL，
未重啟遊戲服務；服務啟動時間仍為 2026-11-12 10:22:54 UTC。部署後新舊遊戲 healthz 均正常。
手機實機 Safari 與 Android 的剪貼簿權限尚未驗收。

## 電腦版畫面下半部被裁切

Release `20261112T103443Z-bf5abcf4` 修正網頁容器的 Grid intrinsic sizing：
1280×600 視窗內，原本的 canvas 實際為 900×1250，導致房間碼與加入按鈕在畫面外。
容器改用 Flex，canvas 允許收縮並依視窗高度限制寬度，現在為 540×600，整個畫面均可見。
先前主要驗收直式 viewport，未涵蓋橫向矮視窗，因此漏掉此問題。

正式站台已在 1280×600 實際輸入 `MZL3W6`，成功加入另一個瀏覽器建立的同一房間並取得 seat 1，
沒有 JavaScript 例外。修正預覽另檢查 1366×768、1280×400、390×844 的畫布邊界。
截圖與結果保存在 `artifacts/tower-duel/lobby-fix`。

本次使用 `Deploy.ps1 -StaticOnly`，逐一比對伺服器、地圖、規則、傳輸與全部 WebGL build 檔案相同後，
只切換網頁版本；未重啟遊戲服務或 Caddy。

## WebGL 公開部署

已部署至 [雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)，release
`20261112T102219Z-6c686f97`。Unity 6000.6.8f1 WebGL release／IL2CPP 建置
Succeeded、0 errors，耗時 271.55 秒。Gzip 下載內容約 15.90 MB；`.wasm.gz` 與
`.framework.js.gz` 的 HTTPS 回應分別為 `application/wasm`、`application/javascript`，
兩者皆有 `Content-Encoding: gzip`，不使用 JavaScript 解壓 fallback。

設定已透過 Editor API 保存並從磁碟確認：WebGL 2、Medium stripping、
ExplicitlyThrownExceptionsOnly、DiskSize、128／1024 MB 記憶體與 Geometric growth。
Unity 專案檢查器 196 檔案，0 errors、0 warnings；Go 測試與 vet 通過。
正式 API 的 protocol 2、地圖／規則雜湊及三張卡均與建置一致。

以本機 Chrome 157.0.8103.39 的兩個獨立 headless browser context，載入真正公開站台
及 WebAssembly，使用滑鼠點擊建立房間、輸入房間碼、準備、選牌與點擊部署。
沒有透過測試專用遊戲指令直接呼叫部署。正式 WebSocket 使用同一網域的 WSS。

| 公開站台雙瀏覽器對戰 | 結果 |
| --- | --- |
| 房間／match | PE2MM2 / PE2MM2-1 |
| 玩家席位 | 0 / 1 |
| 接受部署次數 | 7 / 3 |
| 雙方均部署三種卡 | 是，盾衛／弓箭手／劍士小隊 |
| 兩端均看見三種模型 | 是，實際畫面另存 |
| 觀測到跨橋、攻擊、單位扣血與塔扣血 | 是 |
| 同場同時觀測單位數 | 10；不代表 80 單位壓測完成 |
| 完整正常時間與結算 | 藍方以破塔數獲勝，雙端 winner=0、reason=time_score |
| 窄螢幕布局 | 390×844 viewport 截圖，非手機實機測試 |

保存於 `artifacts/tower-duel/web-deploy`：`result.json`、`build-result.json`、
`lobby-desktop.png`、`three-cards-blue.png`、`three-cards-red.png`、
`battle-blue.png`、`match-portrait-390.png`、`result-blue.png`、`result-red.png`。
瀏覽器未回報 JavaScript 例外；首次載入另記錄一筆 404 資源訊息，未影響遊戲載入或對戰。
自動連續點牌的早期測試因等待 UI 解除 pending 的時間不足而逾時，調整測試點擊間隔後
完成以上對戰；正式遊戲規則未因此修改。手機 Safari／Android、真實觸控、弱網及滿員效能尚未驗收。

主機使用獨立 `tower-duel.service` 與版本目錄，透過既有 Caddy 的站台片段提供服務。
部署前後原有 Rumblebound 的 HTTPS healthz 均正常；沒有替換其遊戲資源與 API。

## 三種兵、角色與動畫（Windows）

Windows x64 Mono 開發版建置通過，0 errors。Unity 專案檢查器涵蓋 192 個
檔案，0 errors、0 warnings。Go 測試與 vet 通過，包含三人編隊的原子部署、
重複請求、容量不足、弓箭手在射程內停步射擊、空地圍攻，以及舊協定／不同
規則雜湊拒絕。三張卡的資料集中在共用 rules.json。

| 最新雙 Player 驗收 | 藍方持續進攻 | 紅方持續進攻 |
| --- | --- | --- |
| 房間／match | RMWN5B / RMWN5B-1 | KHEEKN / KHEEKN-1 |
| 兩席接受部署次數 | 10 / 3 | 3 / 10 |
| 兩端死亡移除數 | 20 / 20 | 20 / 20 |
| 兩端 frame 數 | 5433 / 5141 | 5467 / 5161 |
| 三張卡均使用 | 是 / 是 | 是 / 是 |
| 三種角色在 Move 狀態的腿部動作 | 全部 / 全部 | 全部 / 全部 |
| 三種角色在 Attack 狀態的手臂動作 | 全部 / 全部 | 全部 / 全部 |
| Death 狀態的實際骨骼變動樣本 | 738 / 738 | 740 / 740 |
| 兩端箭矢呈現次數 | 22 / 22 | 22 / 22 |
| 勝負與原因 | 藍方摧毀敵方主堡獲勝，雙端一致 | 紅方摧毀敵方主堡獲勝，雙端一致 |

自動操作經過 UI 的焦點、按鈕送出及世界落點事件；紅方也驗證三人編隊的
方向轉換。另比較六座塔最終血量及勝／敗結算文字，均一致。檢查了最後
Player 日誌，未發現例外、資料拒絕或 USS 選擇器警告。

最新產物（位於被 Git 忽略的 artifacts）：

- `artifacts/tower-duel/three-units-blue/result.json`
- `artifacts/tower-duel/three-units-red/result.json`
- [三兵種實際戰場](../../artifacts/tower-duel/battle-three-1.png)
- [推塔畫面](../../artifacts/tower-duel/battle-three-3.png)
- [角色與裝備姿勢預覽](../../artifacts/tower-duel/characters-preview.png)
- [本輪結算](../../artifacts/tower-duel/editor-three-result.png)

畫面取自正式場景的 Editor Play Mode 與真正 Player 對戰；frame 變動等待
已確認遊戲持續更新，沒有用概念圖代替對戰畫面。八個動畫片段、骨架掛點、
原始授權、三角面數及 9,600 面弓箭手的預算調整見 art/CHARACTERS.md。

此版 Assembly-CSharp.dll SHA-256：
`0A1936F925DD02ECD9C4930AE779C2D40B2D1D672C8EF601AB0DBCFC7B2D756A`。

伺服器與 Unity 規則檔原始位元組 SHA-256：
`3D63AF47336B80524BA995ED3F43167BF4C8C653FB343D1986E4BC45BE2C4CB3`。

以下保留第 1、2 階段的單卡基線記錄，使用的是先前建置。

## 建置與規則

- Unity 6000.6.8f1、URP 17.6.0、Windows x64 Mono 開發版：Succeeded，0 errors。
- `Test-Server.ps1`：`go test ./...` 與 `go vet ./...` 通過。
- 規則測試涵蓋合法／非法部署、能量不足、重複與已淘汰 action、過橋攻塔、
  士兵互打與死亡、同 tick 雙主堡平手、準備倒數、私有能量投影、回能、
  時間到破塔計分、延長賽及逾時平手。
- WebSocket 測試使用真實連線，檢查兩席房間、第三人拒絕、重送出兵只生成
  一個單位，以及重複／未知／缺少／大小寫錯誤欄位的拒絕。
- Unity 專案檢查器：105 個檔案，0 errors、0 warnings。
- PowerShell 啟動、建置及測試腳本語法檢查通過；建置腳本已實際執行。

## 兩個真正 Windows Player

兩次均使用同一份最後建置。透過 UI Toolkit 的按鈕與指標事件建立／加入房間、
準備、選牌及部署。一方持續出兵，另一方出一名守軍。

| 驗收 | 藍方持續進攻 | 紅方持續進攻 |
| --- | --- | --- |
| 房間／match | `73B6BA` / `73B6BA-1` | `4TJLTA` / `4TJLTA-1` |
| 兩席接受的部署數 | 5 / 1 | 1 / 7 |
| 兩端觀察到的死亡移除數 | 4 / 4 | 5 / 5 |
| 兩端累計 frame 數 | 2486 / 2182 | 3828 / 3522 |
| 勝方 | 藍方，雙端一致 | 紅方，雙端一致 |
| 結束原因 | 主堡摧毀 | 主堡摧毀 |

每次另檢查：雙端 UI 高度正常、士兵跨橋、單位及塔受到傷害、六座塔最終血量
一致、勝者／敗者結算文字正確。這也驗證紅方鏡頭的落點能轉回正確世界座標。

報告（本機產物，不納入 Git）：

- `artifacts/tower-duel/player-test/result.json`
- `artifacts/tower-duel/player-test-mirrored/result.json`

最後建置 `TowerDuel_Data/Managed/Assembly-CSharp.dll` SHA-256：
`5550B1952E9A3DD0BBCE6E26A1F390A4C92FDD248C2D6A0F7AD3DC1B95DCA687`。

伺服器地圖與 Unity TextAsset 的原始位元組 SHA-256 均為：
`A9DF8FE9998DC508E1FD32902804DC00C652E6A33541ED84C3AA987C175089DB`。

## 畫面

另以正式場景在 Editor Play Mode 與一個真正 Player 對戰。Pipeline 的 frame
變動等待已確認遊戲持續更新。實際 Game View 擷取了橋上交戰、推塔與結算：

- [橋上交戰](../../artifacts/tower-duel/battle-2.png)
- [推塔](../../artifacts/tower-duel/battle-5.png)
- [結算](../../artifacts/tower-duel/editor-result.png)

已修正自訂 UI 主題的根容器高度、初次結算文字延遲，以及局部鏡頭 viewport
導致計時文字累積重影的問題。鏡頭現在每幀清除全畫面，保持原本戰場構圖。

## 驗收限制

- 自動對戰使用 UI 事件，尚未完成真人滑鼠、實機觸控與玩法樂趣試玩。
  Computer Use 的可見視窗啟動授權逾時，因此沒有宣稱原生視窗點選驗收通過。
- 隱藏 Windows Player 的驗收以數值／UI 狀態報告為準；預設不要求截圖。
  上面的畫面取自 Editor 的實際 Game View。
- 尚未驗收 WebGL、手機、跨機區網、弱網、重連或同房重賽。
- 80 單位是目前規則上限，尚未作為效能或 FPS 保證。
- 現階段為三張固定兵種卡；完整八張牌與手牌循環尚未接入。
