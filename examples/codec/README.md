# Codec 與座標量化

依需求選修：先理解 [Shared World](../shared-world/README.md) 的快照序列化與收送，再區分 frame ack、輸入 ack 及缺基準恢復。本課需雙端編碼／解碼接線，不需先修 Replay、AOI 或歷史補償。選課見 [學習路線](../README.md)，協定與每個接收者的基準如何加入見 [組件接入指南](../../docs/COMPONENT_GUIDE.md)。

雙擊框架 [Start.cmd](../../Start.cmd)，選 **8. Codec 與座標量化**，啟動後開啟 [http://127.0.0.1:18104/](http://127.0.0.1:18104/)。保留終端，Ctrl+C 停止。

從 `gamenetkit` 目錄也可執行：

```powershell
./examples/Run.ps1 -Example Codec
```

## 先照這樣操作

1. 預設完整 JSON，實際訊息與還原後 JSON 的大小相同。
2. 切換「差分編碼」，勾「自動移動」。先收到完整基準，後續可以看到較小的差分快照。
3. 選「量化到 0.1 單位」，比較輸出精度與資料大小；原始權威位置仍保留完整精度。
4. 展開進階區，按「模擬遺失解碼基準」，觀察缺基準、完整恢復及後續差分計數。
5. 開第二個分頁，各自選編碼／精度，確認不影響另一位玩家的選擇。

WASD／方向鍵可手動移動。原本預測、校正、遠端平滑、AOI 與標記新增／移除／重生都保留在進階區。完整基準有 Base64 與 envelope 成本，可能比原始 JSON 大；介面會如實顯示「增加」，不保證每份訊息都變小。

## 加入的組件與接線

| 組件 | 接線 |
| --- | --- |
| [Go codec.Encoder／Quantize](../../server/codec/delta.go) | [encoding.go](server/encoding.go)：每位接收者獨立 encoder，以已確認的基準編碼；只量化輸出投影 |
| [C# FrameDecoder](../../packages/unity/codec/Runtime/FrameDecoder.cs) | [CodecChannel.cs](client/Assets/Scripts/CodecChannel.cs)：解碼後驗證整份應用狀態，成功才回報 ACK |
| 已有同步／呈現組件 | [ExampleSession.cs](client/Assets/Scripts/ExampleSession.cs)：延遲交付、可靠控制、輸入、預測與接收清單 |

接到自己的應用時，順序是權威狀態 → 接收者投影 → 可選量化 → JSON → Encode(payload, decodedAck)。客戶端解碼成功且應用驗證完成後才確認 frame；缺少基準回 ACK 0，伺服器改送完整內容。輸入 seq／ack 與編碼 frame ack 各有責任，不能混用。

改編碼或精度會增加連線內 stream 版本，重建該接收者的基準；舊 stream 的 frame／ACK 忽略。另一位玩家的 encoder、權威 world、物件 ID／life 及傷害規則都不受影響。重新連線則清 decoder、量測與舊 epoch。

## 量測與精度

實際訊息大小計 Go → C# 快照的 UTF-8 JSON bytes，包含 Base64 和外層 envelope；還原後 JSON 是同一份解碼 payload 的大小。近 2 秒平均包含收到但因缺基準而未套用的快照；不含 ACK、其他控制回覆、WS／TCP 標頭或本機瀏覽器呈現通道。

座標精度提供原始／0.01／0.1。量化在 AOI 選取之後，只改傳輸副本中玩家、物件與同步中心的座標，最大單軸誤差不超過 step/2；ID、生命、輸入確認與權威模擬不量化。畫面上的本機預測仍立即回應，收到量化權威位置後由原有校正策略處理。

SDK 每條 encoder／decoder 各保存最多 64 份基準，每份還原 payload 上限 16 KiB；本課固定世界上限使封裝後訊息仍在目前傳輸限制內。完整協定、界線與 reset 見 [PROTOCOL](PROTOCOL.md)、[MODULES](../../docs/MODULES.md#codec-wire)。

## 執行與驗證

使用 Go、含 ASP.NET Core 的 .NET 8 SDK，測試另需 Node。預設使用本機 Unity 安裝附帶的 dotnet，可用 `-DotNet` 指定；不需要 Unity Editor 授權或 NuGet 還原。

```powershell
./examples/codec/scripts/Run.ps1 -Http 127.0.0.1:18104 -BackendPort 18159
./examples/codec/scripts/Test-Codec.ps1
```

測試使用 18162／18163 與 `.local/codec/tests`。涵蓋真實 Go/C# 差分往返、基準確認與恢復、stream 隔離、量化只改副本、其他接收者不受影響、AOI／物件生命及重連。正式頁面來源測試用 DOM／Canvas 替身連到真正服務；另外已完成雙 Windows Player 的連線／移動，以及實際 WebGL 的差分大小、量化誤差及缺基準恢復操作。證據與平台界線見 [VALIDATION](../../docs/VALIDATION.md)。

## Unity 版

另附 [Unity 專案](client) 與 [當課場景接線](client/Assets/Scripts/Unity/ExampleClient.cs)。開啟專案、Windows／WebGL 建置與操作方式統一見 [Unity 教學說明](../unity-common/README.md)。預設瀏覽器入口維持不變。
