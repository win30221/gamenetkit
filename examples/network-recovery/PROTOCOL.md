# 即時連線與恢復的探測協定

移動、inputTick、ack、bump、welcome 與完整 snapshot 沿用 [預測教學協定](../prediction/PROTOCOL.md)。世界仍由服務持有，每個新連線取得新玩家 ID。基本教學與前一課均不因本課增加探測訊息。

客戶端送出一份 WS text JSON：

```json
{"type":"ping","id":1}
```

伺服器將請求排入模擬 owner，回覆：

```json
{"type":"pong","protocol":1,"id":1,"tick":42}
```

id 是正的 JavaScript 安全整數，伺服器不接受額外 tick、玩家 ID、重複鍵或大小寫變體。tick 是 owner 處理該探測時的世界步數，不從網路 goroutine 直接讀取模擬資料。每條連線有四份有界探測回覆佇列，滿時忽略多餘探測，不阻塞世界；所有寫入仍由該 session 的同一寫入者負責。

應用在送入模擬鏈路時記錄 id／本機單調時間；僅在收到相符、未使用、五秒內的 pong 時建立 RTT 樣本。重連清掉配對資料與時鐘，probe id 繼續遞增，傳輸 epoch 另隔離舊 socket。控制訊息不模擬損失，但有界佇列或連線故障仍可能讓探測失敗；沒有回覆不能當成零延遲。

自動重連是本機應用策略，不是新增的伺服器身份恢復協定。斷線移除玩家，重連走一般 hello／welcome，重新建立預測、物件清單與時鐘。
