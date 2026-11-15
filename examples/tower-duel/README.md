# 雙橋對決 · Tower Duel

使用 GameNetKit 的 1v1 即時出兵推塔範例。Go 擁有戰局，Unity URP 呈現
KayKit 場景，玩家親自決定出兵時間和落點。

目前有八張固定牌、四張手牌循環、下一張預覽、雙橋交戰、正常時間與延長賽、
結算與同房重賽，以及 30 秒斷線恢復。六種部隊沿用同風格 KayKit 角色與動畫，
火球／冰霜使用原創低面數特效，另有八張精緻插畫卡面、基本音效與靜音。

鏡頭採 60 度斜俯視，卡牌以固定直式比例縮放，牌區高度固定為螢幕的 18%；
主堡、橋面與牌區頂邊維持固定的垂直構圖，視窗寬度只改變左右可見的場景，不會拉遠鏡頭或增加上下留白。
平板與寬畫面額外顯示連續草地、河流與自然樹叢，可部署區與遊戲規則相同。
最新視窗與場景修正見 [RESPONSIVE_VIEW_VALIDATION.md](RESPONSIVE_VIEW_VALIDATION.md)。八張牌版本的實際驗收見
[P0_VALIDATION.md](P0_VALIDATION.md)；舊版紀錄見 [VALIDATION.md](VALIDATION.md)。
Android／iOS 實機與真人戰術平衡、樂趣仍待驗收。
重連排程與法術共用 SDK 時間軸的改動及測試見 [SDK_REUSE_VALIDATION.md](SDK_REUSE_VALIDATION.md)。
後續 WebGL 重建、公開站重連／特效驗證與 client-only 部署見 [WebGL SDK 部署驗證](WEBGL_SDK_DEPLOYMENT_VALIDATION.md)。

目前版本新增跟手虛化卡面、抬高落點、部署格線、拆塔擴區、血量數字、連續能量與滿能量提醒。
操作規則與「急流短橋＋遺跡長道」構想見 [部署與地圖設計](DEPLOYMENT_AND_MAP_DESIGN.md)。
本輪測試與畫面記錄見 [部署操作驗收](DEPLOYMENT_VALIDATION.md)。
後續已修正桌面血條過大、卡面原位跟手、模型切換過晚與最近合法格吸附，見 [拖牌修正驗收](DRAG_REFINEMENT_VALIDATION.md)。
此版本地圖雜湊已改變，已一起重建並部署客戶端與伺服器。

與《皇室戰爭》的功能差異、優先級及後續可實作項目見
[功能差異與路線圖](FEATURE_GAP_ROADMAP.md)。

## 線上試玩

[開啟雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)。兩位玩家開啟同一網址，
一人建立雙人房間，另一人輸入六位房間碼加入，雙方按「準備對戰」。
也可用兩個視窗自行測試。2026-11-15（台灣時間）已上線士兵攻擊前搖與前搖死亡取消，
保留等待面板退出房間、最近合法格吸附、桌面細血條、原卡槽跟手與連續能量；版本 `20261115T024452Z-bf8955f5`。
協定 3、規則 `eight-cards-1`。主要 WebGL 建置檔合計約 17.5 MiB。
網頁會自動使用同一網域的 WSS 連線，不需要填伺服器位址。
等待對手時可按「複製房間碼」，成功後按鈕會顯示「已複製房間碼」。
若瀏覽器未能自動複製，會顯示可選取的代碼欄位，提供手動複製及再次嘗試。
建立或加入房間後，可按等待面板的「退出房間」返回大廳；已準備或倒數時仍可退出，退出後重整不會自動回到舊房間。
本輪退出與重新加入驗證見 [等待房間退出驗證](ROOM_EXIT_VALIDATION.md)。
本次前搖版本的建置、部署及公開站驗證見 [攻擊前搖部署驗證](ATTACK_WINDUP_DEPLOYMENT.md)。

## 本機試玩

使用根目錄 `toolchain.json` 指定的 Unity 6000.6.8f1 和 Go 1.27.3。
目前協定為 3、規則版為 `eight-cards-1`，舊 Player 與伺服器需一起重建，
避免協定或規則雜湊不符。
已建置時，從儲存庫根目錄執行：

```powershell
./examples/tower-duel/scripts/Run.ps1 -Clients 2
```

WebGL 已建置時，也可執行 `Run.ps1 -Web`，以瀏覽器開啟
`http://127.0.0.1:18110/`；本機可使用 HTTP，公開部署使用 HTTPS／WSS。

第一個視窗按「建立雙人房間」，第二個輸入六位房間碼加入。雙方按
「準備對戰」後開始。按住兵種卡拖到己方半場，放開部署；也保留點選卡牌後點地的操作。
拖回牌區或按 Esc 會取消；拖到河道、建築、尚未開放的敵方半場或場外時，模型與落點會吸附最近可部署格。
綠圈代表可部署，能量不足等狀況顯示紅圈。劍士小隊會選擇三個落點全部合法的最近格，才會扣一次費用並生成。
拖曳時先有半透明卡面跟手，進入可部署格後切換成半透明兵種模型；落點往指標上方偏移，避開手指。
部隊吸附格心，選牌時可見合法／非法格線；法術維持自由瞄準與真實作用半徑，拖出戰場時落點停在邊界。
摧毀敵方前塔後，開放該側河對岸四單位深的前線部署區，兩座都倒下後連成整條前線。
可在已有士兵的位置重複下兵，生成後會由伺服器處理圓形碰撞與互相推擠；建築、河道及邊界仍不可部署。
出兵落點決定較近的橋，同一小隊共用一條路線；同路前塔倒下後繼續攻主堡。
士兵預設朝同路前塔／主堡前進；偵測範圍內有敵兵時，將敵兵與塔一起依碰撞邊緣距離比較，朝最近目標前進直到能攻擊，距離相同優先塔。
因此塔後較遠的防守兵不會讓尚未攻擊的敵兵略過更近的塔；攻城兵仍只打建築。開始攻擊前搖後維持鎖定，但敵軍死亡或離開偵測範圍就立即解鎖，取消未完成的前搖並重新索敵；沒有範圍內敵軍時繼續推塔。
所有士兵攻擊都先前搖再造成傷害；前搖結束前死亡會取消該次攻擊，弓箭與炸彈也不會射出。
盾衛／弓箭手／劍士／炸彈兵／突擊兵／攻城兵的前搖分別為 8／12／6／15／9／12 tick（每秒 30 tick）；冰霜會延長前搖，原本攻擊間隔維持不變。
塔也會保持攻擊目標；對方仍活著但走出射程時，會等候它重新進入射程。
主堡開局待命，受到傷害或己方任一前塔被摧毀後永久啟動；主堡仍可在待命時被攻擊。
火球與冰霜可指定整個戰場；成功出牌才扣費及補下一張，取消不換牌。
主堡摧毀即結算，雙方按「同房再戰」後開始新局，或離開回大廳。
連線中斷時模擬持續，30 秒內可自動恢復原席位與手牌；主動離房立即退出。
伺服器在原終端機以前景執行，按 Ctrl+C 停止。

開發時也可單獨執行 `Run.ps1`，在 Unity 開啟
`client/Assets/Scenes/TowerDuel.unity` 並按 Play；另一位使用 Windows Player。
雙方畫面都會把自己的陣營放在近端。

## 建置及測試

```powershell
./examples/tower-duel/scripts/Build.ps1
./examples/tower-duel/scripts/Build.ps1 -Target WebGL
./examples/tower-duel/scripts/Test-Server.ps1
./examples/tower-duel/scripts/Test-Client.ps1
./examples/tower-duel/scripts/Test-Players.ps1
./examples/tower-duel/scripts/Test-Players.ps1 -Rounds 3 -ExerciseRecovery
./examples/tower-duel/scripts/Test-Players.ps1 -WeakNetwork -ExerciseRecovery -AttackerSeat 1
./examples/tower-duel/scripts/Test-Load.ps1
node examples/tower-duel/scripts/Test-Web.cjs http://127.0.0.1:18110 artifacts/tower-duel/web-test
node examples/tower-duel/scripts/Test-Network-Web.cjs http://127.0.0.1:18110 artifacts/tower-duel/network-web
node examples/tower-duel/scripts/Test-Deployment-Feedback.cjs http://127.0.0.1:18110 artifacts/tower-duel/deployment/feedback
node examples/tower-duel/scripts/Test-Room-Exit.cjs http://127.0.0.1:18110 artifacts/tower-duel/room-exit/local
```

`Build.ps1` 使用本機 Unity CLI 與獨立專案，產物為
`client/Builds/Windows/TowerDuel.exe`。也可直接使用 Unity 選單
`Tower Duel > Build Windows development player`。

WebGL 產物位於 `client/Builds/WebGL`，使用 release IL2CPP、WebGL 2、Gzip、
128 MB 起始／1024 MB 上限記憶體及 Medium stripping，保留明確拋出的例外。
`Tower Duel > Apply Web release settings` 可重套設定；建置時也會套用。
啟動批次 Editor 時必須指定 `-buildTarget WebGL`；建置腳本會為自行啟動的 Editor 指定。

沿用已裝好 Rumblebound／Caddy 的 Linux x64 主機部署：

```powershell
./examples/tower-duel/scripts/Deploy.ps1 -VmHost 172.233.67.181 -Domain tower-duel.172.233.67.181.sslip.io -IdentityFile C:/Users/H/.ssh/id_rsa
```

`-PackageOnly` 僅產生 `.local/deploy/tower-duel/<release>/game.tar.gz`，包含雜湊清單。
正式部署使用 `/opt/rumblebound/tower-duel/releases`、獨立 `tower-duel.service` 與
loopback 18110；在既有 Caddy 加入站台，以 SIGUSR1 重載設定。安裝失敗會還原舊版與代理設定。
部署前按共用規則驗證 WebGL 版本標記；公開站健康檢查同時比對協定、地圖／規則雜湊、卡牌數與版本標記。
這個部署腳本需要可用的既有 Rumblebound 代理，並非全新 VM 的安裝器。
只修正網頁外框時可加 `-StaticOnly`：先確認 API、協定與 WebGL build 檔案均相同，
再原子切換版本，不重啟遊戲服務；若這些檔案有改變則拒絕此模式。
只更新 WebGL 客戶端可使用 `-ClientOnly`：允許 client build 改變，仍要求 API、地圖、規則、
代理設定與 SDK 傳輸檔完全相同，保留正在執行的伺服器。

`Test-Players.ps1` 啟動隔離的 Go 伺服器及兩個真正 Windows Player，透過
UI 事件建立／加入房間、準備、選牌與點擊落點。一方持續輪流使用八張卡，
另一方各使用一次，檢查交戰、死亡、跨橋、主堡摧毀、六座塔血量與勝負一致。
它也檢查六種角色的骨骼動畫、死亡姿勢、投射物、法術及完整場地視野，
並檢查血量數字、部分能量填充與滿能量提示，保存報告到 `artifacts/tower-duel/player-test`。隱藏視窗的數值驗收與
可見視窗的畫面／滑鼠操作驗收分開記錄；隱藏視窗可能無法擷取畫面。
自動操作僅在開發版明確帶入 `-td-auto` 時啟用，不會影響正常試玩。
`-Rounds 3` 驗證連續三局，`-ExerciseRecovery` 驗證中途斷線，
`-WeakNetwork` 加入訊息層延遲、抖動、丟棄與重複；工具不模擬 IP 層丟包。

`Test-Load.ps1` 以測試專用、停用傷害的 fixture 保留 80 個角色，量測兩端呈現與快照。
`Test-Client.ps1` 在空白 Unity Play Mode 場景執行真正的 session／SDK 收件佇列與法術呈現測試，
涵蓋重連退避與期限、原席位與未確認出牌恢復、取消、快照停頓、特效起訖及重賽重設。
它保留恢復憑證，完成後還原場景；執行前需停止 Play Mode 並儲存場景。報告位於
`artifacts/tower-duel/client-tests/result.json`。
`Test-Web.cjs` 需要既有 `playwright-core` 與 Chrome；可用 `PLAYWRIGHT_CORE`、
`CHROME_PATH` 指定位置。它使用真實滑鼠／觸控事件、頁面重整恢復、完整對局與重賽，
並保存手機、平板及寬畫面的截圖。需要弱網時，先啟動 `server/cmd/weaknetproxy`。
它也會關閉遊戲 WebSocket，確認不重整頁面即可恢復原局與手牌。
`Test-Network-Web.cjs` 使用正式 WebGL 的受控 WebSocket 快照、實際畫面像素與 Web Audio 播放呼叫，
驗證重連退避、原席位恢復、法術不提前命中、重複快照、到期移除及重賽重設；不替代真實伺服器對局。

Go 測試另涵蓋部署範圍、編隊全部成功或全部拒絕、能量不足、回能與計時、
延長賽、重複請求、舊 action 重播、同 tick 平手、
士兵互打、兩席房間與嚴格訊息解析。它們與雙 Player 驗收分開記錄。

## 同區網試玩

```powershell
./examples/tower-duel/scripts/Run.ps1 -Listen 0.0.0.0:18110
```

另一台電腦開啟同一份 Windows 建置，在大廳的伺服器欄輸入
`http://<伺服器區網 IP>:18110`。Windows 本機及公開 WebGL/WSS 已驗收，區網連線
另需系統允許該埠。這是訪客好友房，尚未接入帳號或公開配對。

## 結構與資產

- `client`：受版本控制的 Unity 專案；不依賴 `artifacts` 的美術驗證工程。
- `server`：Go 應用，引用 SDK `loop`、`transport`。
- `protocol`：共用地圖與 [訊息協定](protocol/README.md)。
- `art`：KayKit 來源、CC0 授權、場景組裝與 URP 美術驗證流程。
- [角色製作記錄](art/CHARACTERS.md)：原始檔雜湊、武器掛點、朝向、面數與動畫匯入方式。
- [八張牌美術與來源](art/EXPANSION_ASSETS.md)：新增部隊、火球／冰霜、卡面及音效。
- [插畫卡面](art/CARD_ART.md)：八張原創生成圖、完整提示詞與雜湊；場內模型留待後續升級。
- Unity 引用 SDK `core`、`transport`、`interpolation`；無客戶端傷害預測。
- 重連排程使用 core `ReconnectPolicy` 的 30 秒期限與指數退避；原席位、手牌與動作去重由遊戲協定處理。
- 士兵與法術共用 SDK `FixedTimeline.RenderTick`，使用傳輸抵達時間及 `SnapshotBuffer` 取樣，法術不再自行外推 tick。
- 中文字型衍生自 Noto Sans TC，保留 OFL 與來源雜湊，完整 CJK 字元可用於名稱；
  `scripts/Prepare-Font.py` 可用 fontTools 4.61.1 與指定原始字型重建。

盾衛、弓箭手、劍士小隊各 3 費；炸彈兵、突擊兵、火球各 4 費，攻城兵 5 費，冰霜 2 費。
炸彈兵造成範圍傷害，突擊兵移動蓄力衝鋒，攻城兵只攻擊建築。
數值集中在 `protocol/rules.json`，建置腳本同步到 Unity，雙端握手比對規則雜湊。

