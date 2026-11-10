# Codec 教學協定 v1

基本世界與 AOI 沿用 [Interest 協定](../interest/PROTOCOL.md)。本課內部服務 18159，本機 C# 教學主機 18104；快照另含 codec 摘要。

## 設定與確認

```json
{"type":"codec","mode":"delta","step":0.1}
```

mode 為 json／delta，step 只允許 0／0.01／0.1，三個欄位必填，拒絕額外／重複／null 欄位。新連線先用 json、step 0、stream 1；設定有變更才增加 stream，重設該接收者的 encoder／ack／frame ID；相同設定不重設。

```json
{"type":"frame-ack","stream":2,"id":1}
```

id 為 0～9007199254740991，stream 為非零 uint32。伺服器只接受目前 stream、delta 模式、且不超過已產生 frame 的 ack；舊正數 ack 不回退，0 明確要求完整基準。其他 stream 或未產生的 frame ack 忽略。

## 快照

json 模式仍直接傳完整 snapshot；welcome 一律是完整 JSON。內層 codec 摘要包含 mode、step、maxError 與 stream。maxError 是當次輸出所有座標的最大單軸量化誤差，step 0 時為 0。

delta 模式的外層範例（這份是完整基準，data 為有效快照 JSON 的 Base64）：

```json
{"id":1,"data":"eyJ0eXBlIjoic25hcHNob3QiLCJwcm90b2NvbCI6MSwidGljayI6MjQsImdlbmVyYXRpb24iOjEsInBsYXllcnMiOlt7ImlkIjoxLCJ4IjotMiwieiI6MCwiYWNrIjoxLCJpbnB1dFRpY2siOjIwfV0sIm9iamVjdHMiOlt7ImlkIjoibGFuZG1hcmstNiIsIm93bmVyIjowLCJsaWZlIjoxLCJ4IjotMiwieiI6MCwia2luZCI6ImxhbmRtYXJrIn0seyJpZCI6ImxhbmRtYXJrLWh1YiIsIm93bmVyIjowLCJsaWZlIjoxLCJ4IjowLCJ6Ijo3LCJraW5kIjoiZ2xvYmFsIn1dLCJpbnRlcmVzdCI6eyJtb2RlIjoiYW9pIiwicmFkaXVzIjozLCJtYXJnaW4iOjAuNzUsImNlbnRlclgiOi0yLCJjZW50ZXJaIjowLCJ0b3RhbFBsYXllcnMiOjIsInRvdGFsT2JqZWN0cyI6MTMsIm93bmVkT2JqZWN0cyI6MH0sImNvZGVjIjp7Im1vZGUiOiJkZWx0YSIsInN0ZXAiOjAsIm1heEVycm9yIjowLCJzdHJlYW0iOjJ9fQ==","stream":2,"protocol":1,"type":"frame","tick":24,"base":0}
```

base=0 為完整 payload；base>0 是相對已確認 frame 的二進位差分。內層必須為 snapshot，protocol、tick、codec.mode=delta 與 stream 必須符合外層。二進位格式由正式 SDK 提供，見 [Codec Wire](../../docs/MODULES.md#codec-wire)。

每個接收者的 encoder／decoder cache 各自保存最多 64 份不可變基準。編碼器只參考對端已解碼的 ack，且差分有足夠效益才採用；不保證每份 envelope 都比 JSON 小。

## 解碼邊界與恢復

C# 先檢查外層 stream／frame 身份，再用實際 FrameDecoder 還原；完整應用 envelope、玩家、物件、AOI、codec 摘要驗證通過後，才安排 ACK 與呈現交付。舊 stream／frame 忽略，非法資料屬協定失敗。stream 變更會重設 decoder 的 ID 基準，世界 tick 仍嚴格前進。

MissingBaseline 不套用任何不完整資料，保留最後有效權威畫面，回 ACK 0；收到完整基準後繼續差分。教學按鈕只丟棄接收端 codec cache，保留同 stream 的應用 frame 高水位，避免舊 frame 重播；遇到下一份差分才實際觸發缺基準與恢復。清量測不清 decoder，重連則清所有基準／量測並依 transport epoch 隔離舊回呼。

## 量化與量測

量化只處理投影副本中的座標與 AOI 中心，選取與權威模擬維持原精度。每位接收者可有不同 step；ID、life、tick、輸入 ack 不量化。最大誤差為 step/2，客戶端保留原有預測與校正策略。

wireBytes 計實際收到的 Go 快照 JSON 字串，包含外層與 Base64；jsonBytes 計成功還原 payload 的 UTF-8 大小。2 秒下行平均包含缺基準訊息，排除 frame-ack／其他控制、傳輸標頭與本機預覽通道。各 frame 的 wireBytes／jsonBytes 與顯示狀態一起交付，弱網延遲不混配其他快照的大小。
