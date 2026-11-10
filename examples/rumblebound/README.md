# Rumblebound：完整對戰範例

遊戲規則、模擬、協定和 Unity 素材集中在本目錄的 `game/`。本專案目前仍以相對路徑依賴倉庫內的 `server/`、`packages/unity/` 與根目錄工具；搬到獨立倉庫時須一併提供這些依賴並調整路徑。

這個 example 把原對戰遊戲重新接到框架：訪客身份、私人房間、分隊選角、準備與載入、三種角色的攻擊／衝刺、碰撞推擠、據點得分、練習 AI、結算與原身份重連。自己的移動使用預測與校正，其他玩家使用遠端插值；進階選項預設收起。

## 直接啟動

本地統一使用 `./examples/Run.ps1 -Example Rumblebound`；重新建置 WebGL 使用 `./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Web`。VM 部署使用 `./examples/rumblebound/scripts/Deploy.ps1 -VmHost <主機> -Domain <網域>`，參數與維護指令見 [部署說明](../../docs/DEPLOYMENT.md)。

在框架根目錄執行：

```powershell
./examples/Run.ps1 -Example Rumblebound
```

選單第 10 項啟動服務後，手動開啟 [完整遊戲](http://127.0.0.1:18093/)，使用原角色、地圖、動畫、特效與介面；根網址直接載入，不需 `/unity/`。第一次先執行 `./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Web`。尚未建置時會顯示建置指令，完成後重新整理即可進入。

開兩個瀏覽器視窗進入遊戲，以訪客身份建立／加入私人房間、分隊選角並準備，再由房主開始對戰。單人可進入練習場。

[Canvas 網路診斷介面](http://127.0.0.1:18093/diagnostics/) 保留簡化場地及下述進階組件、AI 與錄製操作，供觀察 C# 流程及 SDK 資料。這是獨立的診斷入口。

Unity 正式遊戲使用 60×60 地圖與拉近的玩家跟隨鏡頭，橫向滿版呈現附近交戰；小地圖顯示角色、據點與目前視野，按住／拖曳可查看遠處，放開回到角色。設定中的「鏡頭距離」可調近遠並保存在本機。舊 sample 已移除；現行規則與素材都在本範例的 game 目錄。

電腦：右鍵點地面繞障礙物移動，綠色圓圈顯示實際最終落點；A 後左鍵點地面啟用移動攻擊（橘色落點），途中遇到射程內敵人就停下普攻，目標死亡或離開射程後續走，右鍵／S／衝刺可取消；右鍵點敵人追到普攻射程後持續攻擊，點地面取消追擊，衝刺會取消原本的移動與追擊指令。Q／E 按住預覽、移動滑鼠瞄準，放開施放技能／衝刺，S 停止，空白回正鏡頭，Esc 選單。

鏡頭預設視野 8、俯視高度 26，再略拉遠；設定範圍擴為 5～10；仍只顯示附近交戰。大型地圖 movementScale=0.8，三角色一般移速為 4.8／4／4.4，衝刺速度維持原設定。設定中的鏡頭距離可自行調整。

按住哪個操作才顯示對應範圍，未操作時隱藏。手機普攻／技能開始按住時只顯示射程，拖曳超過門檻後才顯示選敵／方向／落點；放手隱藏。電腦右鍵／A 為普攻範圍、Q 技能、E 衝刺。普攻朝向使用攻擊目標並保留出手方向，與技能瞄準分離。弓箭手鎖定普攻呈現普通飛行箭矢；技能保留火焰箭尾、短拖尾與命中火花，彈體飛行期延長到 30 ticks，完整射程約 12.63 單位。三角色技能 CD 為弓箭手 3 秒／坦克 4 秒／法師 5 秒，普攻與衝刺冷卻不變。

手機：左側搖桿移動，右側分成普攻、技能與衝刺。普攻輕點一次、按住依攻速重複，拖曳可指定方向上的敵人；技能輕點輔助瞄準最近敵人的目前位置、拖曳手動選方向／落點，放開施放。法師自動選敵會把爆炸半徑計入可命中距離，超過施法距離時以邊緣落點判斷能否碰到目標，選擇可命中的最近敵人；無可命中目標就不施放。手動法師目標超出射程時徑向限制到最外圍，落點仍須合法。衝刺沿目前移動方向，未移動時沿最後移動方向。手機範圍外的敵人由玩家自行靠近，持續推著搖桿時會短暫停步完成普攻前搖，出手後續走。

普攻是鎖定單一敵人的獨立傷害，出手前驗證射程、掩體、目標生命、存活與保護狀態。移動命令或衝刺可取消未出手的普攻；出手後移動保留傷害與完整攻速冷卻。三角色原有弓箭、扇形揮砍及火球落點用作獨立技能，技能與普攻分開計時，技能仍可閃避。雙方都由同一模擬 owner 決定傷害與冷卻。

實際觸控逐幀依 fingerId 追蹤移動／普攻／技能的不同手指。選單、死亡、斷線、背景與遺失手指會停止控制；尋路方向即使每幀改變，網路輸入也限制為最多 60 Hz，自己的預測仍逐幀更新。

直向手機會提示橫放，提供「橫向全螢幕」入口；旋轉後自動顯示遊戲。瀏覽器不允許全螢幕或橫向鎖定時，仍可手動橫放手機，必要時關閉系統旋轉鎖定。一般桌面窄視窗不會被橫屏提示擋住。手機版保留安全邊界並隱藏 Unity 頁尾，增加可用場地。Windows 可加 `-rf-touch-controls` 顯示相同控制供測試。

練習或對戰中按「選單」，上方可直接切換「練習選單／設定／框架選項」三個分頁。各頁底部固定提供「繼續練習」（多人為「繼續遊戲」），一般設定調整後可一步回到原局。框架設定有修改時，按「套用並繼續練習」，重連及載入完成後會自動回到遊戲；也可選「只套用設定」留在面板觀察。設定沒有變更時，繼續操作不會重新連線。

Esc 在遊戲內開啟選單，在選單內則返回遊戲；有尚未套用的框架變更時，會提供套用、捨棄或留在設定的選擇。切換分頁會保留變更與捲動位置。登入、大廳及房間的設定仍使用「返回」回到來源畫面。

遊戲外由「設定 → 框架技術選項」進入，可以比較自己的預測／校正、其他玩家平滑、時鐘、codec、AOI、量化、歷史／近戰補償與動作預先呈現。直接點選按鈕，金色外框表示目前選擇；滑桿旁會顯示數值。上行與下行各有延遲、抖動（0～300 ms）及丟包率（0～100%），原本的「收件延遲」仍只影響下行。這是應用訊息層模擬，控制訊息只延遲、離散動作會重送，不等同 TCP／QUIC 封包丟失。按畫面底部「套用設定」保存目前執行中的設定，重新開啟面板會讀取已套用內容。

選擇「附近物件」後才能調整可見半徑。「等待權威」會使用直接校正；開啟預測後才可選平滑校正。啟用近戰延遲補償會一併啟用歷史狀態；關閉歷史狀態也會關閉近戰補償。「即時診斷」下的開關立即生效，清除解碼基準與模擬中斷用來觀察恢復。

房主可在隊伍卡調整容量，展開「房間管理」移動成員隊伍。大廳選「單人練習」可組合待命／巡邏／搶點、攻擊、閃避與保持距離；練習中按 Esc 開啟「練習選單」，切換行為沿用目前練習場，不會重新建立比賽。

工具自動載入 Go，編譯實際 C# 客戶端主機與 Go API。公開頁面使用 18093，私有 API 使用 18179。`Ctrl+C` 停止這次啟動的服務。修改程式後重新執行工具；瀏覽器可按 `Ctrl+F5`。

需要比較 WebTransport 時，用 `./examples/Run.ps1 -Example Rumblebound -WebTransport`，再於進階設定選 WebTransport 並套用。工具建置 HTTP/3 helper、使用 18493 UDP 與短效憑證 pin；不安裝系統信任憑證。Canvas 介面由實際原生 C# SDK 連上 WT；加上 `-Unity` 時工具也會將 helper 路徑傳入 Player。獨立部署原生 Unity 時，把對應 helper 放進 `Assets/StreamingAssets` 再建置；Unity WebGL 走瀏覽器 SDK bridge。

接收佇列維持 64 筆上限；畫面停頓累積過多訊息時，以一次受控失敗清除舊 delta 基準並重新連線，不從 WebGL callback 反覆拋例外，也不任意漏掉 delta 依賴。

本輪使用訪客登入。既有 Google 接線保留，但依使用者要求暫緩真實帳號設定與驗收；不需要設定 `RB_GOOGLE_CLIENT_ID`。

## 錄製與播放

在對戰選單或設定中展開「錄製與離線播放」，按開始／停止，再匯出檔案。這份檔案保存此玩家收到的權威快照，包含當時選用的同步範圍與量化。

觀看下載的重播：

1. 離開房間，回到登入畫面或大廳，展開「對戰重播」。
2. 按「選擇重播檔案」，在選檔視窗選擇下載的 `rumblebound.playback.json`。
3. 選好後自動載入，可暫停、逐格、跳轉與調速；按「離開重播」返回。

播放控制集中在底部：拖曳金色時間軸跳轉，直接點選 `0.25×`／`0.5×`／`1×`／`2×`／`4×` 切換速度，金色外框表示目前速度。時間顯示為分、秒與十分之一秒；窄視窗會把倍速選項移到下一列。

Windows Player 與 WebGL 都提供選檔按鈕，不必輸入網址、搬動檔案或上傳到伺服器。網頁讀取使用所選檔案的暫存 Blob URL，完成或取消後會釋放；播放不連線、不送出遊戲輸入。檔案上限 32 MiB，空檔或格式錯誤會顯示提示。需要手動載入時再展開「進階：網址或路徑」；WebGL 的進階欄位只接受 HTTP／HTTPS 網址，不能直接讀取 `file:///`。

伺服器完整輸入錄製與離線重建是另一條路徑，使用共用遊戲層的 journal／verify／export。它能驗證模擬事件與狀態；客戶端快照錄製只用於觀看。既有工具見 [Replay 說明](../../docs/REPLAY.md)。

```powershell
./examples/Run.ps1 -Example Rumblebound -ReplayDirectory '.local/rumblebound/replays'
./examples/rumblebound/scripts/Export-Playback.ps1 -File '.local/rumblebound/replays/實際檔名.replay.jsonl' -Output 'artifacts/my-match.playback.json'
```

## Unity

Unity 6000.6.8f1 專案在 `client`，開啟 `Assets/Scenes/Battle.unity` 按 Play。先啟動上面的服務；Unity 使用同一個 HTTP origin。保存的場景由 `BattleBootstrap` 建立流程、API、原版 UI Toolkit 選單與 HUD，原角色動畫／地圖／特效／音效由共用呈現套件提供。登入、大廳、房間與練習設定使用同一套原版主題、字型和按鈕圖；對戰恢復置中的完整場地，不再有左側簡易面板。

```powershell
./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Windows
./examples/Run.ps1 -Example Rumblebound -Unity
```

`-Unity` 會檢查 battle 建置標記，避免啟動舊版方塊 Player。原生 Player 可用 `-rf-server http://127.0.0.1:其他埠` 覆寫服務。WebGL 建置使用 `-Target Web`，完成後從 [Unity 網頁版](http://127.0.0.1:18093/unity/) 進入；自動使用同源 API／WS。Windows 與 WebGL 重播優先使用選檔按鈕；其他原生平台保留進階路徑／網址載入，尚未提供系統選檔接線。原生匯出路徑會顯示在面板。

修改 Unity C# 或 SDK 後，先重新建置 Windows／WebGL，再重新啟動；`examples/Run.ps1` 會重建網頁用的 C# 主機與 Go API，Unity Player 需另外執行 `examples/Build-Unity.ps1`。

原版 UI 還原前已通過 Windows／WebGL 建置、真正雙 Player 的完整與精簡組件流程、原生 UI／3D 呈現，以及實際瀏覽器房間、操作、自然結算、WT 重連與錄製播放；當時的 WebGL 也驗證過面板上方的 WASD、文字輸入隔離與長網址版面。七課建置與 14 個 Windows Player 也已通過；具體覆蓋範圍、日誌及未測平台集中於 [VALIDATION](../../docs/VALIDATION.md)。

原版 UI 還原後另完成兩平台建置、Windows 真實視窗操作與排版檢視；本次最新 WebGL 畫面複驗受瀏覽器工具停用阻擋，與前述舊介面證據分開記錄。當時本機畫面紀錄在 `artifacts/art-parity-review/REVIEW.md`（不隨 Git 提供）；可攜的驗證摘要見 [VALIDATION](../../docs/VALIDATION.md)。

## 程式從哪裡看

| 位置 | 責任 |
| --- | --- |
| [server/battle/application.go](server/battle/application.go)／[cmd/battle](server/cmd/battle/main.go) | 服務組裝、Gin、身份、房間與單一 owner 模擬生命週期 |
| [遊戲層](game/README.md) | 規則、地圖、身份、房間、戰鬥、AI、結果與伺服器重播 |
| [GameFlow](client/Assets/Scripts/Battle/GameFlow.cs) | 身份、HTTP 任務、房間、席位、載入、SDK 自動重連與過期回覆隔離 |
| [BattleMenuNavigation](client/Assets/Scripts/Battle/BattleMenuNavigation.cs)／[Unity 導覽接線](client/Assets/Scripts/Battle/Unity/BattleHud.Navigation.cs) | 遊戲內分頁、一步繼續、Esc、未套用變更提示與返回焦點 |
| [BattleSession](client/Assets/Scripts/Battle/BattleSession.cs) | 真正 SDK 傳輸、快照驗證、codec ACK、輸入與動作序號、時鐘、預測／插值組裝 |
| [GameProjection](client/Assets/Scripts/Battle/GameProjection.cs) | 網頁／Unity 共用的採樣位置與斷線畫面 |
| [BattleNetcodeModules](client/Assets/Scripts/Battle/BattleNetcodeModules.cs) | 選用組件的唯一具體型別註冊處 |
| [BattleReplay](client/Assets/Scripts/Battle/BattleReplay.cs) | 快照錄製、完整檔案驗證與 SDK 播放時鐘 |
| [Unity 接線](client/Assets/Scripts/Battle/Unity)／[共用美術](game/unity-art/README.md) | 輸入、API、介面、採樣位置顯示、動畫特效與音效 |
| [browser-host](browser-host)／[web](web) | Unity 預設入口、同源 API／WS 代理、`/diagnostics/` 的 C# 流程主機與薄 Canvas 診斷介面；JavaScript 不重寫同步演算法 |

本 example 已移除重製期間的 protocol 1 方塊入口、重複 session／網頁與舊 Unity 場景。基本同步教學集中在 `examples/shared-world`。唯一 API 入口為 `server/cmd/battle/main.go`，從 `examples/rumblebound/server` 執行 `go run ./cmd/battle` 啟動私有完整遊戲 API（18179），一般遊玩使用上面的 `examples/Run.ps1`，工具會同時組裝公開頁面與 API。協定見 [PROTOCOL](PROTOCOL.md)，學習單一組件請從 [組件 API 課程](../modules/README.md) 開始。

## 驗證

每支指令的用途、前提與輸出見 [scripts 操作索引](scripts/README.md)。日常啟動不需先執行全部測試。

```powershell
./examples/rumblebound/scripts/Test-Battle.ps1
./examples/rumblebound/scripts/Test-Browser.ps1
./examples/rumblebound/scripts/Test-BattleRemoval.ps1
./examples/tests/Test-UnityCompilation.ps1
./examples/rumblebound/scripts/Test-BattlePlayer.ps1
```

網頁流程測試使用實際服務與 DOM／Canvas 替身；Unity 參考編譯與資產檢查不取代真正 Player 驗收。

新版戰鬥驗收：`./examples/rumblebound/scripts/Test-CombatPlayer.ps1 -Review` 自建隔離 API，啟動真正 Unity Player 驗證繞障礙物、追擊、普攻取消／冷卻、獨立技能、衝刺、多指與鏡頭，並產生桌面／手機 HUD 渲染圖。`-Origin` 可改測 VM；實體手機觸控另需裝置端驗收。

