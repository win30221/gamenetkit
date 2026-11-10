# Shared World 教學協定

這是 Shared World 的教學協定，不是框架的通用遊戲格式，也不隨完整遊戲增加欄位。所有訊息為一個 WS text frame 中的一份 JSON；HTTP 健康端點、`/transport.js`、`/` 診斷頁與 `/unity/` 共用同一個 origin。預設服務為 `http://127.0.0.1:18092`，WS 為 `/ws`。

## 訊息

```json
{"type":"hello","protocol":1}
```

連線後三秒內送出 hello。每條連線只能完成一次加入；伺服器分配 playerID。未提供登入／房間／身份恢復；重新連線會取得新 ID。

```json
{"type":"welcome","protocol":1,"playerID":1,"tick":0,"tickRate":30,"snapshotRate":15,"players":[{"id":1,"x":-2,"z":0,"ack":0}]}
```

welcome 是首份狀態，先於此連線的 snapshot。每個玩家有獨立 ID；整個伺服器共用一個 world 與 tick。最多八位玩家。

```json
{"type":"input","seq":1,"x":1,"z":0}
```

輸入約每秒 30 次，包含放開按鍵後的零方向。seq 為此連線從 1 起、不超過 JavaScript 安全整數 9007199254740991 的遞增整數；過期／重複序號不更新方向或活動時間。x、z 各為有限的 [-1,1] 數值；斜向量由伺服器正規化，移速每秒四單位，位置限制於 [-8,8]。玩家歸屬只由連線決定；輸入沒有 playerID 欄位，額外／未知欄位、大小寫變體與重複欄位拒絕。

```json
{"type":"snapshot","protocol":1,"tick":2,"players":[{"id":1,"x":-1.7333333333333334,"z":0,"ack":1},{"id":2,"x":2,"z":0,"ack":0}]}
```

世界以 30 tick/s 步進，每兩 tick 廣播完整玩家清單，含伺服器接受的最後 input seq（ack）。client 只接受比已套用快照更新的 tick；welcome 可用 tick 0 初始化。清單缺少某個 ID 代表玩家離開，呈現層移除對應物件。此階段直接顯示權威位置，尚未預測或插值。

有效輸入中斷 500 ms 後伺服器將方向歸零，避免背景頁籤／失焦持續移動；client 失焦也應送出零方向。個別連線離線只移除此玩家，不停止世界迴圈。慢 client 的快照佇列有界並保留最新狀態，不能阻塞其他玩家。

```json
{"type":"error","code":"invalid_message"}
```

錯誤以 code 顯示後結束連線。傳輸本身也可能回報 `connection_closed`／`send_failed`；client 應清掉狀態與場景殘留，讓使用者手動重新連線。錯誤代碼不是此階段的穩定公開 API。
