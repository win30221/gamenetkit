# Go 組件課程

先理解單一模擬 owner 與快照，再依需求挑一課。這些是純資料／檔案示範，沒有 HTTP；有 HTTP 的 Shared World 與 Rumblebound 仍使用 Gin。從框架根目錄執行：

```powershell
./examples/modules/server/Run.ps1 -Lesson history-lagcomp
```

其他名稱是 `interest`、`codec`、`replay`；`all` 跑全部。腳本自行載入 Go 設定、切換目錄，任何示範結果不符即結束失敗。每課有自己的原始碼，入口 [main.go](main.go) 只負責選課。

## 加入自己的服務

從 [組件接入指南的 Go 接線](../../../docs/COMPONENT_GUIDE.md#5-go-組件放進模擬與輸出流程) 先確定 package 應放在權威步進、每人投影、編碼或錄製的哪一段，再讀下列單項 API 範例。AOI、Codec、歷史與錄製可按需求選修，不需要依本頁章節全部加入。

本例 [go.mod](go.mod) 已示範 `require github.com/win30221/gamenetkit/server` 與本機 `replace`；同一 SDK 提供以下 package，不需每課另裝一個 module。實際專案調整 replace 相對路徑，在應用組裝入口建立實作，再以 `netcore` 契約傳給使用端。不要把應用 DTO 或規則搬入 SDK。完整限制見 [MODULES](../../../docs/MODULES.md)。

## history-lagcomp

先用 [歷史位置與延遲補償操作教學](../../history-lagcomp/README.md) 查詢目標過去的位置，再比較目前／回溯命中。

讀 [history.go](history.go)。加入 `server/history` 與 `server/lagcomp`，由應用提供可深複製的 pose。課程保存 tick 10 的 x=0 與 tick 12 的 x=2，先經 Bounded 授權，再由 At 取得夾住 tick 11 的資料，應用插值得到 x=1；過舊要求被拒絕。

接入：模擬步進完成時 Add；收到需歷史位置的動作時驗證身份／時間，再 Target → At → 應用幾何判斷。SDK 不會搬動當下 world，也不決定命中／傷害。新比賽 Clear。停用 history 就不保存，停用 lagcomp 就使用當下判定；兩者不是同一個開關。

## interest

先用 [AOI 操作教學](../../interest/README.md) 切換全部／範圍同步，走近與走遠，查看不同玩家收到的清單與權威世界總數。

讀 [interest.go](interest.go)。加入 `server/interest`，每個接收者建立獨立 Radius。全部三個物件，範圍內 near 與 Always 的 score 會被選中；near 到 5.5 仍保留，到 6.1 才離開，減少邊界反覆進出。

接入：在權威狀態產生後、每個接收者的快照編碼前 Select。只縮小傳輸投影，不刪除權威世界物件。新連線建立新的 selector；停用直接投影全部物件。Always 與容量是應用選擇，不是安全性或可見性判定的替代。

## codec

讀 [codec.go](codec.go)。加入 `server/codec`，每個接收者各有 encoder。課程全量資料的 base64 長度為 640，確認 frame 1 後下一份差分為 12；兩者解碼後資料相同。這組數字是重複測試 payload 的效果，不代表實際遊戲壓縮率。

接入：應用投影／可選量化 → 序列化 → Encode(payload, decodedAck) → 傳輸 envelope。確認必須來自對端成功解碼；缺基準回 ack=0，改送完整 frame。Quantize 示範 1.234 → 1.2，只應套用輸出投影。新連線 Reset；停用直接傳全量未量化 JSON。Unity 解碼接法見 [client codec](../client/README.md#codec)。

## replay

可先用 [錄製與播放操作教學](../../replay/README.md) 看完整權威快照如何寫入 journal、完成驗證，再交給客戶端播放。

讀 [replay.go](replay.go)。加入 `server/replay` 與應用自有的事件格式、初始定義、規則與 state hash。課程錄製加 2、加 3，Close 後驗證完整 journal，在隔離計數器重播得到 5，live 計數器仍為 5。

接入：模擬 owner 在套用事件後 Append，處理回傳 false／Err，結束時 Close；只有完整結束的檔案可作成功紀錄。VerifyFile 完成前不能把未驗證前綴套到 live world。SDK 驗證檔案完整性，應用回呼驗證規則與 state hash。範例只建立並清理自己的暫存檔；停用時不建立 journal。播放畫面與時間控制是另一責任，見 [client playback](../client/README.md#playback)。
