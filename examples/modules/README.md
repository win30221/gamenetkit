# 組件加入課程

這裡是可隨需求查閱的 API 練習，不必等操作課全部學完。第一次使用先看 [學習路線](../README.md)；想知道如何把練習接進 Unity／Go 專案，讀 [組件接入指南](../../docs/COMPONENT_GUIDE.md)，其中有從 Shared World 加入插值的完整步驟。建議每次採用「操作一課 → 跑同主題 API 練習 → 接入自己的專案 → 驗證重連與回退」。

每課只展示一個問題，直接使用正式 SDK。C# 課程每次只編譯 core、該課所需套件與那一份示範檔；不需要載入其他課程或完整遊戲。Go 課程共用一個小型命令入口，依 `-lesson` 執行所選主題。

這裡提供純資料範例。要實際切換插值並看方塊效果，使用 [插值操作教學](../interpolation/README.md)；基本多人接線見 [Shared World](../shared-world/README.md)，遊戲整合見 [Rumblebound](../rumblebound/README.md)。

| 問題 | 課程 | 加入的能力 |
| --- | --- | --- |
| 遠端位置一格一格跳 | [interpolation](client/README.md#interpolation) | 延後取樣、fixed／adaptive 替換 |
| 自己移動要等回包、修正會跳動 | [操作教學](../prediction/README.md)、[prediction-correction](client/README.md#prediction-correction) | 本機預測、ack 重播、顯示偏移衰減 |
| 不知道伺服器現在走到哪個 tick | [clock](client/README.md#clock) | RTT 中點估計、有界外推 |
| 物件新增、消失與復活難以管理 | [replication](client/README.md#replication) | 完整清單差異、生命與世代 |
| 動作想立即顯示又不能重複播放 | [actions](client/README.md#actions) | 暫時效果、確認／拒絕／逾時 |
| 全量快照佔用太多流量 | [操作教學](../codec/README.md)、[client codec](client/README.md#codec)、[server codec](server/README.md#codec) | 基準確認、差分、缺基準恢復、投影量化 |
| 想知道延遲並重現弱網 | [操作教學](../network-recovery/README.md)、[diagnostics-simulation](client/README.md#diagnostics-simulation) | 量測與應用訊息延遲／丟棄 |
| 想控制離線播放 | [操作教學](../replay/README.md)、[playback](client/README.md#playback) | 播放時鐘、暫停、倍速與跳轉 |
| 想查過去的位置作命中判斷 | [操作教學](../history-lagcomp/README.md)、[history-lagcomp](server/README.md#history-lagcomp) | 歷史保存與查詢時間窗；命中幾何由應用提供 |
| 不必把全世界傳給每個人 | [操作教學](../interest/README.md)、[interest](server/README.md#interest) | 每個接收者的 AOI、遲滯與必要物件 |
| 想錄下可重播的權威事件 | [操作教學](../replay/README.md)、[replay](server/README.md#replay) | journal、完整檔驗證與隔離重播 |

完整跑一遍，從框架根目錄執行：

```powershell
./examples/modules/Test.ps1
```

每課成功會輸出 `RF_CLIENT_LESSON PASS <主題>` 或 `RF_SERVER_LESSON PASS <主題>`，最後是 `RF_LESSONS PASS`。任何示範結果不符即回報失敗；課程的少量檢查用來防止範例與 SDK 脫節，完整契約測試仍在 SDK 測試專案。
