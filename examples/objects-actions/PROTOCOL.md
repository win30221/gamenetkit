# ObjectsActions 協定 v1

這是獨立教學協定。Go 以 30 Hz 更新、15 Hz 傳完整世界；座標限制 ±8、速度 4、輸入 500 ms 過期。玩家最多 8、標記每人最多 4。WebSocket 位於內部服務 `/ws`，瀏覽器 `/lesson` 由本機 C# 主機轉接並執行 SDK。

## 加入與完整清單

```json
{"type":"hello","protocol":1}
```

```json
{"type":"welcome","protocol":1,"playerID":1,"tick":0,"tickRate":30,"snapshotRate":15,"generation":1,"players":[{"id":1,"x":-2,"z":0,"ack":0,"inputTick":0}],"objects":[]}
```

```json
{"type":"snapshot","protocol":1,"tick":24,"generation":1,"players":[{"id":1,"x":-2,"z":0,"ack":1,"inputTick":1}],"objects":[{"id":"object-1","owner":1,"life":1,"x":-0.5,"z":1.5}]}
```

`players`、`objects` 都是完整清單，空清單用 `[]`。缺少的物件應移除。客戶端先驗證兩份清單再套用 SDK；重複 ID、非法歸屬／座標／生命編號屬協定錯誤；同代舊 tick 忽略。本課 generation 固定 1，每條連線重新建立客戶端狀態。物件 ID 在本次伺服器執行內遞增；同 ID 重生只增加 `life`，由 SDK 回報 removed＋added。玩家離線會移除自己的標記。

## 輸入與動作

```json
{"type":"input","seq":1,"x":1,"z":0}
```

`seq`／動作 `id` 各自遞增，為 1～9007199254740991；軸為 [-1,1]。`ack` 是最新採用輸入序號，`inputTick` 是它首次參與的 tick（採用時世界 tick＋1），供既有預測重播。推移教學仍接受只含 `type` 的 `bump`。

```json
{"type":"action","id":1,"kind":"spawn"}
```

動作只允許 `type`、`id`、`kind` 三個欄位，不接受客戶端指定 owner 或目標 ID。`kind`：spawn 新增；remove 移除最後一個自己的標記；rebirth 同 ID 重生；deny 示範拒絕；timeout 示範不改世界、不回覆。所有欄位必填，拒絕 null、重複或額外欄位及未知 kind。

伺服器排程 21 ticks 後決定。每人最多 8 個待執行動作，超額回覆 busy；完成結果快取最多 64 筆。相同／較小 ID 不重新執行；若仍有已完成快取則重送原回覆。此機制限本連線、不是跨重連交易保證。可靠控制佇列滿時終止連線，不默默丟失結果。

## 結果與呈現

```json
{"type":"action-result","protocol":1,"id":1,"kind":"spawn","accepted":true,"reason":"","objectID":"object-1","x":-0.5,"z":1.5}
```

```json
{"type":"action-result","protocol":1,"id":2,"kind":"deny","accepted":false,"reason":"demo_rejected","objectID":"","x":0,"z":0}
```

拒絕 reason 可為 demo_rejected、capacity、empty、busy。動作回覆與世界快照分別處理，允許任一個先到：回覆決定暫時效果，權威快照決定物件是否存在。相同回覆不重播效果；未知 ID 忽略。待確認 1.4 秒後收回暫時效果並標記 expired，4 秒內晚到的結果仍可更新原動作，超過保留時間的回覆忽略，權威清單仍照常套用。

離線或開始新連線時清除清單、動作、延遲佇列與呈現，並以 transport epoch 隔離舊回呼。timeout 教學不改世界；實際系統的逾時只代表結果未知，應依權威資料或業務查詢處理，不可直接當成失敗後無條件重送新動作。
