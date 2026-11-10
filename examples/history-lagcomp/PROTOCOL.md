# History 教學協定 v1

基本 hello／input／玩家與物件生命沿用 [物件教學](../objects-actions/PROTOCOL.md)，另加入共享移動目標。30 Hz 模擬、15 Hz 快照；內部服務 18169、本機教學主機 18105。

## 快照擴充

```json
{"target":{"life":1,"hp":5,"x":-5,"z":3},"history":{"first":0,"last":0,"count":1,"maxRewind":15}}
```

這兩個欄位加入 welcome／snapshot；target 每秒沿 x 方向移動 6 單位，在 [-5,5] 折返，z 固定 3，半徑 0.35。玩家初始 z=-5。life>0、HP 1～5；每次致死／重設會增加生命、恢復 HP 與位置並清掉歷史。

history 每 2 ticks 記錄（重設可在任意 tick 建立第一份），最多 90 份；first／last 由 SDK Buffer.Bounds 回報真實範圍，count 為目前資料數。快照的目標與歷史摘要在應用 envelope 驗證成功後才一起套用。C# 目標 ReplicaStore 在 life 改變時替換基準；TargetTrack 在新生命起點夾住呈現 tick，不沿用舊生命座標。

## 唯讀歷史查詢

```json
{"type":"history","id":1,"tick":30}
```

```json
{"type":"history-result","protocol":1,"id":1,"tick":30,"found":true,"target":{"life":1,"hp":5,"x":1,"z":3},"history":{"first":0,"last":60,"count":31,"maxRewind":15}}
```

讀取 Buffer.At 並由應用內插 bracket；超出保留區間回 found=false 且無 target。查詢不受命中補償 0.5 秒窗口限制，不會修改 live 位置／HP。客戶端只保留符合目前要求與生命的查詢結果，改參數時合併等待中的操作，最多一個 in-flight，兩秒期限。

## 射擊與結果

```json
{"type":"shot","id":2,"tick":48,"observed":50,"life":1,"x":0,"z":3,"compensate":true}
```

所有欄位必填，id 為 1～9007199254740991，tick／observed 為同上界的非負整數；life>0，aim x／z 有限且在 ±8，compensate 是 bool。拒絕 null、重複／額外欄位、客戶端射擊起點／傷害／指定其他玩家等內容。

伺服器將 observed 限在這條連線已提供快照的 tick 上限，再由 Bounded.Target 驗證 requested≤observed≤now 且 now-requested≤15。讀不到目前生命的歷史或 life 不符則拒絕。現在／回溯兩種模式都驗證時間與生命，差別是採用哪個位置作命中幾何。

射擊起點使用伺服器的當下玩家位置，方向指向 aim；範圍 16 單位，射線到目標中心的垂直距離不超過 0.35 視為命中。應用在 history 位置副本計算，不搬動 live 目標。接受的 shot 以目前或歷史 verdict 決定是否扣 live HP。

回覆仍是 action-result，kind=shot；外層 accepted 表示射擊要求合法，內層 shot.hit 才是實際命中。shot 另含 currentHit、historicalHit、compensated、shotTick／usedTick、life、origin／aim／current／past 座標與 reason，供畫面比較。拒絕原因為 outside_window、unobserved_tick、life_changed、missing_history。

所有動作共用遞增 ID 與每玩家 64 筆結果快取。相同 ID 不再扣血，快取存在時重送原結果。新生命不重設玩家的動作去重；新連線才建立新玩家基準。

```json
{"type":"reset-target"}
```

重設為本教學共享操作，所有玩家看到相同新生命；只有 type 欄位。生命邊界清掉 server history，客戶端清目標取樣與過期查詢呈現；已接受的舊 shot 回覆只是當次結果，不會再次修改世界。
