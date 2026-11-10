# 預測教學的應用協定

沿用 Shared World 的單一共用世界、30 Hz 步進、15 Hz 完整快照、最多八人、位置 [-8,8]、速度每秒四單位與輸入 500 ms 有效期。這是本課 DTO，不是 SDK 強制格式。所有訊息為單份 WS text JSON。

連線先送 `{"type":"hello","protocol":1}`，伺服器回 welcome；輸入為 `{"type":"input","seq":1,"x":1,"z":0}`，序號為每條連線遞增的 JavaScript 安全正整數。方向只控制該連線的玩家，斜向正規化。

```json
{"type":"snapshot","protocol":1,"tick":12,"players":[{"id":1,"x":-1.8666666666666667,"z":0,"ack":3,"inputTick":12}]}
```

`ack` 是最後接受的輸入序號，`inputTick` 是該輸入**首次參與步進的 tick**。伺服器先 Apply、再 Step，因此 Apply 當下記錄 `目前 tick + 1`；在第一個使用它的快照中，`tick - inputTick + 1 = 1`。`ack = 0` 表示尚無有效輸入，套用步數為零。過期／重複序號不更新 inputTick 或活動時間。

教學控制 `{"type":"bump"}` 由世界 owner 將該玩家 x 減一並套用界線，不改別人的位置或輸入 ack。多餘欄位（含 playerID）、重複鍵、大小寫變體都拒絕。它代表一個權威外力，不是預測器預先知道的移動命令，因此可用來觀察真實校正。

welcome 仍先於 snapshots，提供 playerID、tickRate、snapshotRate 與完整清單。離線只移除此玩家；重連取得新 ID。應用收到 `{"type":"error","code":"invalid_message"}` 或傳輸錯誤時清除狀態，讓使用者手動重連。
