# Interest 教學協定 v1

權威模擬與基本輸入沿用 [物件同步教學](../objects-actions/PROTOCOL.md)，30 Hz 更新、15 Hz 快照；此課擴充中立地標與每接收者的完整投影，預設內部服務 18149、本機教學主機 18101。

## 範圍選擇

```json
{"type":"interest","mode":"aoi","radius":3}
```

```json
{"type":"interest","mode":"all","radius":3}
```

只有 type、mode、radius 三個必填欄位。mode 為 all 或 aoi，radius 為有限的 2～6；拒絕 null、重複／缺少／額外欄位，以及指定中心、其他玩家或常駐旗標。伺服器以這條已加入連線的玩家位置作中心。

新連線先使用 all／半徑 3，之後套用客戶端保存的選擇。變更只影響本接收者；模式或半徑改變時重建 selector，下一份正常 tick 快照回報新設定。未變更的重複設定不重設進出歷史。

## 每接收者的完整快照

```json
{"type":"snapshot","protocol":1,"tick":24,"generation":1,"players":[{"id":1,"x":-2,"z":0,"ack":1,"inputTick":20}],"objects":[{"id":"landmark-6","owner":0,"life":1,"x":-2,"z":0,"kind":"landmark"},{"id":"landmark-hub","owner":0,"life":1,"x":0,"z":7,"kind":"global"}],"interest":{"mode":"aoi","radius":3,"margin":0.75,"centerX":-2,"centerZ":0,"totalPlayers":2,"totalObjects":13,"ownedObjects":0}}
```

welcome 使用同樣清單與 interest，另含本機 playerID、tickRate=30、snapshotRate=15。generation 固定 1，每條新連線重新建立基準。自己始終包含在 players，金色常駐地標始終包含在 objects。

objects.kind 為 landmark（owner=0、life=1）、global（固定 landmark-hub，owner=0、life=1、座標 0／7）或 marker（owner>0、每人最多 4 個）。靜態地標為 x=-6／-2／2／6，z=-5／0／5 的 12 個位置，另有 1 個 global；總物件上限 45，玩家上限 8。

interest.center 與該快照自己的權威位置一致；totalPlayers／totalObjects 為未篩選的世界數量，只傳數量；ownedObjects 為自己在權威世界的標記數，包含範圍外標記，供既有物件動作操作。players、objects 都是這個接收者當下的完整清單，空間外實體不傳入陣列。

AOI 可能選中一個標記，但沒有選中它遠處的 owner。客戶端可在 aoi 模式接受這種引用，仍驗證物件類型、ID、座標、生命與所有者欄位；all 模式要求 owner 在玩家清單。完整兩份清單及 interest 驗證成功後才套用 ReplicaStore；無效資料不部分更新。

## 篩選與生命週期

每位玩家各有一份真正的 `interest.Radius`，候選 key 將玩家與物件分開。自己的玩家及 global 設為 Always；其他候選依距離選取。首次進入距離 ≤radius，已接收的候選在距離 ≤radius+0.75 時保留；容量 53，涵蓋完整世界上限。

all 直接輸出完整權威清單；aoi 只改傳輸投影。快照中不再出現的 ID 從客戶端接收 store 移除，權威世界保留；重入會重新加入同 ID／目前 life。life 改變仍依 SDK 產生 removed＋added。客戶端的接收變更記錄不能當作權威 Instantiate／Destroy 次數。

遠端玩家離開接收清單即停止呈現；重新進入時記錄新的起始 tick，平滑只使用重入後的快照，避免沿用離開前的舊位置。離線清除所有 scope、replica、輸入／動作與呈現歷史，transport epoch 隔離舊回呼；重新連線後可再次送出保存的模式／半徑。

## 物件動作與量測

spawn／remove／rebirth 沿用前課的動作確認與歸屬規則。移除及重生始終以伺服器的完整權威物件決定，即使該標記當下不在自己的接收清單也能操作。動作回覆屬可靠控制訊息，與 AOI 篩選的世界快照分開；動作成功不直接把圈外標記塞回接收清單。

C# 主機回報的 snapshotBytes 是最新已套用 Go JSON 快照的 UTF-8 byte 數，包含 interest metadata，排除 WS／TCP framing、控制回覆與本機瀏覽器呈現通道。它可用於比較本課 all／aoi 的實際應用 payload。
