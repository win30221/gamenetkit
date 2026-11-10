# Rumblebound 完整對戰接線

這是遊戲 application 的協定；SDK 不認識英雄、房間或傷害。正式入口是 `scripts/Run.ps1`／`examples/Run.ps1 -Example Rumblebound`：公開同源頁面預設 18093，私有 Gin API 預設 18179。API 入口 `server/cmd/battle/main.go` 委派 `server/launcher`；重製期間的 protocol 1 過渡入口已移除。

## 身份、房間與載入

1. `GET /api/config` 取得 protocol 5、content、原規則、mapJSON／SHA-256、伺服器組件清單，以及選用 WT／Google 設定。先驗證規則與地圖，再發布給視圖。
2. `POST /api/session/guest` 建立臨時身份。後續 HTTP 使用 `Authorization: Bearer <session>`；不把 credential 放進 URL。每個網頁視窗獨立保存自己的身份，避免 cookie 將兩個玩家合併。
3. 房間 API 為 `/api/room/create`、`join`、`current`、`team`、`hero`、`assign`、`capacity`、`ready`、`start`、`ticket`、`leave`。一般房間由房主開始；私有對戰開始後 roster 固定，結束後回房間。
4. 練習使用 `/api/practice/start`、`mode`、`leave`，沿用相同對戰接線與權威模擬。
5. ticket 只能使用一次，綁定身份與 match。每次連線取新的 ticket；恢復原席位會取得新 generation，input seq／actionID 從 welcome 確認基準續增。

連線 `/ws`，或啟用時使用 config 的 WebTransport URL，第一則訊息為：

```json
{"type":"hello","protocol":5,"content":"g1-practice-spacing-1","matchID":"match-from-ticket","ticket":"one-use-ticket","net":{"codec":"json","interest":"all","radius":12,"quantize":false,"history":false,"lag":"off"}}
```

welcome 帶玩家完整狀態、自己的 playerID、generation、match 與地圖識別。呈現器完成原素材／場景配置後才送 loaded；所有玩家完成載入後由伺服器發布 start，不能先控制尚未載入的角色。對戰結束發布 `result`；它可與最後一份快照同 tick，也可作結束後重接的第一份狀態，必須完整驗證結算且只提交一次。

```json
{"type":"loaded","protocol":5,"content":"g1-practice-spacing-1","matchID":"match-from-ticket","mapHash":"validated-map-sha256","generation":1}
```

## 輸入與權威快照

世界以 30 tick/s 模擬，快照 15/s。客戶端約 30/s 傳送方向、瞄準、已確認 frame 及顯示時間；歸屬來自連線，不接受任意 playerID 或傷害。遊戲使用共用規則的碰撞、推擠、攻擊、冷卻、死亡與復活。

```json
{"type":"input","protocol":5,"content":"g1-practice-spacing-1","matchID":"match-from-ticket","generation":1,"seq":1,"x":0,"z":1,"facingX":1,"facingZ":0,"frameAck":0,"viewTick":2,"observedTick":4}
```

攻擊／衝刺在 input 上加上 action、actionID、actionTick、aimX／aimZ；法師另帶 hasGroundTarget／targetX／targetZ。待確認動作重傳相同 ID，伺服器只執行一次。快照 actionAck／actionCode 結束待確認動作；預先呈現只影響動畫／提示，不計算權威傷害。

快照包括 players、projectiles／projectileEnds、combatEvents、groundCasts／groundImpacts，以及 match 的據點、隊伍分數、phase／result。先完整驗證 envelope、世代、序號、幾何與容量，才提交到狀態、SDK replica、移動策略或 frame ACK。自己的位置使用預測／校正，其他玩家使用選定的 timeline；兩邊都讀取 GameProjection／GameMovement 的採樣結果。

差分模式使用 frame envelope（id／base／data）。只有完整還原並驗證的快照可 ACK；缺基準時 ACK 歸零要求完整幀，舊 frame 不回退確認。量化與 AOI 作用於每個接收者的投影，不改權威世界。可靠 wire 上限由 SDK 管理，原始 codec payload 另有 16 KiB 上限與原 JSON fallback。

ping／pong 必須匹配 generation、match、protocol／content 與未消耗的 echo，RTT 使用同一個本機單調時鐘。history／melee lag compensation 由伺服器限制查詢時間，歷史查詢不修改當前世界。

## 呈現主機與 Unity

網頁 `/play` 是薄介面到實際 C# GameFlow／SDK 的橋接。它接收有界操作命令、發布已採樣座標；瀏覽器不另寫預測／插值。`/ws` 是 Unity WebGL 使用的原始代理；`/api/*` 為同源代理。UI 控制訊息上限 8 KiB、佇列 16、最多八個介面視窗，各自持有獨立流程。

Google 設定存在時，瀏覽器／WebGL 使用共用登入 bridge。開始／交換端點保留 Origin、HttpOnly attempt cookie 與一次性 nonce；只有交換後的遊戲 session 交給 GameFlow。原生 Unity 使用訪客；此範例的身份儲存仍是臨時儲存，不是帳號資料庫。

## 恢復與重播

意外中斷使用 SDK ReconnectPolicy，重新取 ticket，維持身份與席位。手動中斷或停用策略取消後續重試；過期 HTTP／socket epoch 不能覆寫目前流程。恢復期間凍結最後畫面並停止控制。接管其他仍在線的自己必須明確勾選 takeover。

客戶端錄製格式 `rumblebound-view-v1` 保存已驗證的權威快照，受 AOI／量化影響；跨 generation 停止。完整伺服器輸入 journal 由共用 Go domain 錄製，驗證後匯出 `rumblebound-sim-v1`。兩者使用同一 SDK PlaybackClock 播放，最大 32 MiB／120000 frames／一小時。載入前驗證所有幀；錯誤檔案不部分替換目前播放。離線播放要求先離開即時房間，不啟動輸入、登入或重連。

網頁重播檔案透過 `/viewer/<random-view-id>/replay` 上傳／下載；每視窗串行處理、有界大小，在擁有 GameFlow 的同一迴圈提交，不把大檔塞入 UI WS。
