# Replay 教學協定 v1

世界與物件動作沿用 [第 5 課協定](../objects-actions/PROTOCOL.md)：Go 30 Hz 模擬、15 Hz 完整快照，最多 8 玩家／每人 4 標記。此課的內部服務預設 18139；瀏覽器透過 C# 教學主機 18100 的 `/lesson` 操作與繪圖。

## 錄製控制

同一條已加入的 SDK WebSocket `/ws` 上送出：

```json
{"type":"record","command":"start"}
```

```json
{"type":"record","command":"stop"}
```

只接受 type、command 兩個必填欄位，command 為 start 或 stop；拒絕重複、null、額外欄位、客戶端路徑及指定其他 owner。錄製只由目前連線的玩家控制，內容為同一世界的完整權威清單。

```json
{"type":"record-state","protocol":1,"status":"recording","recordingID":"0123456789abcdef0123456789abcdef","frameCount":1,"duration":0,"reason":""}
```

```json
{"type":"record-state","protocol":1,"status":"ready","recordingID":"0123456789abcdef0123456789abcdef","frameCount":36,"duration":2.3333333333333335,"reason":""}
```

status 為 recording、finalizing、ready 或 error。錄製中約每秒回報進度，duration 由最後與第一份快照的 tick 差／30 換算。開始時即記錄初始完整快照，之後每 2 ticks 擷取一次，最多 451 份／900 ticks。重複 start 在錄製或完成處理期間只回報目前狀態；重複 stop 不重新建立檔案。玩家離線會結束自己的錄製。

超過同一服務程序 16 次錄製回報 recording_limit；寫入、容量或完整性失敗回報 recording_failed。狀態屬可靠控制訊息，佇列滿會結束連線。

## journal 與播放 JSON

Go SDK journal 包含 header、step 事件與完成 footer。應用識別為 `replay-lesson`、model 為 `authoritative-frames-v1`；definition 保存 tickRate、錄製玩家 ID 與初始快照。InitialHash 與每個 StateHash 是對該完整快照 JSON byte 的 SHA-256。SDK 驗證序列、完成標記與整檔 digest；應用另驗證識別、hash、tick、歸屬、生命及清單上限。

模擬 owner 只擷取並非阻塞加入有界工作佇列（128 份），背景 worker 建立／寫入／完成 journal，再以隔離資料驗證整份檔案。擷取或 journal 佇列溢位時錄製失敗，不能發布有效前綴。單筆上限 16 KiB、journal 及播放輸出各上限 8 MiB。ready 只會在所有檢查通過後送出。

內部 `GET /recordings/<32位小寫hex ID>` 只提供本次服務已完成驗證的 JSON，未知或未完成錄製回覆 404；不接受檔案路徑。C# 主機相同路徑提供下載附件，並以此端點載入播放資料。

```json
{"version":1,"application":"replay-lesson","tickRate":30,"localPlayerID":1,"sourceDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","frames":[{"type":"snapshot","protocol":1,"tick":10,"generation":1,"players":[{"id":1,"x":-2,"z":0,"ack":0,"inputTick":0}],"objects":[]}]}
```

frames 是嚴格前進 tick 的完整快照，第一份可為任意有效 tick；全部都包含錄製者。sourceDigest 識別原始已驗證 journal。客戶端驗證及複製整份資料成功後才替換舊播放檔，任一 frame 失敗保留舊檔。這個 digest 是來源識別，客戶端播放 JSON 本身沒有數位簽章。

## 播放呈現與即時隔離

實際 `PlaybackClock` 使用 30 Hz tick 時間：Load／Play／Pause／Seek／Advance 控制選取 frame；回放顯示位置在相鄰快照間取樣。向前播放套用經過的清單；跳轉先清除離線 replicas，再用目標完整快照建立基準，同 ID 新生命正確替換。物件新增／移除以快照邊界決定。

瀏覽器控制只送本機 C# 主機：play 切換播放／暫停、seek 以相對起點秒數指定且暫停、speed 為速度（SDK 接受 0.25～4，頁面提供 0.5／1／2）、view=live 返回即時畫面。播放時移動、物件動作及新錄製由主機拒絕，原本即時 session 持續 pump 並送零輸入。兩份 replica 與時鐘各自擁有狀態，播放時間不會送回權威服務。

新網頁教學 session 清除舊回放；即時 session 自身斷線只清除即時快照、動作與錄製狀態，已載入的離線檔可繼續播放。既有 transport epoch 隔離舊連線回呼。
