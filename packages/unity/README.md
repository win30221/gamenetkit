# Unity 客戶端 SDK

這裡是 12 個本機 UPM 套件，不是可以直接開啟的 Unity 專案。先執行範例請看 [Shared World](../../examples/shared-world/README.md)；最短可讀專案是 [minimal-unity](../../examples/minimal-unity/README.md)。倉庫建置使用的 Editor 版本以 [toolchain.json](../../toolchain.json) 為準。

## 安裝到自己的專案

假設 Unity 專案放在 `examples/my-game/client`，將以下兩項合併到它的 `Packages/manifest.json` 的 `dependencies`，保留既有 Unity 套件：

```json
"com.realtime.core": "file:../../../../packages/unity/core",
"com.realtime.transport": "file:../../../../packages/unity/transport"
```

`file:` 相對於 Unity 專案的 **Packages 目錄**；搬到其他位置必須重新計算。亦可在 Unity Package Manager 使用「Add package from disk」選取各套件的 package.json。這是本機來源，沒有設定公開套件 registry；安裝選用套件前先提供 core。

使用自訂 `.asmdef` 的應用，在 `references` 加入 `Realtime.Core`、`Realtime.Transport`。然後依 [Minimal 接線教學](../../docs/GETTING_STARTED.md#4-看懂-go-與-unity-如何接線) 建立 MessageTransport、開始連線、每幀排空收件、解析快照與呈現，離開時 Dispose。套件不會自行建立角色、場景或遊戲協定。

## 按需求選套件

| 目錄／套件 ID 後綴 | assembly | 用途 |
| --- | --- | --- |
| core | Realtime.Core | 時間軸、歷史、連線生命週期與替換契約 |
| transport | Realtime.Transport | 原生 WebSocket、WebGL bridge 與選用原生 WT |
| interpolation | Realtime.Interpolation | 遠端快照時間軸與緩衝 |
| prediction | Realtime.Prediction | 本機預測與權威確認後重播 |
| correction | Realtime.Correction | 校正的顯示偏移策略 |
| clock | Realtime.Clock | 伺服器時間估計 |
| replication | Realtime.Replication | 完整物件清單與生命差異 |
| actions | Realtime.Actions | 動作暫時呈現、確認與逾時 |
| codec | Realtime.Codec | 快照差分解碼與基準恢復 |
| diagnostics | Realtime.Diagnostics | 網路時間及流量量測 |
| simulation | Realtime.Simulation | 應用訊息延遲、抖動與丟棄 |
| playback | Realtime.Playback | 離線播放時間軸 |

套件 ID 為 `com.realtime.<後綴>`。十個選用套件各只依賴 core；transport manifest 不依賴 core。安裝某套件後，應用 assembly 也要引用其 assembly。第一個擴充練習見 [加入插值](../../docs/COMPONENT_GUIDE.md)，完整 API／停用／移除契約見 [MODULES](../../docs/MODULES.md)。

WebGL 主機須提供 `/transport.js`（來源為 `transport/Browser/transport.js`）與應用的 WebSocket 路由。桌面 WebTransport 另需 [native helper](../../native/webtransport/README.md)；基本 WebSocket 不需要它。

修改 SDK 後在根目錄執行 `./tests/scripts/Test-UnitySdk.ps1`；需要有效 Unity 授權。自己的遊戲仍需重新建置並確認收包、移動、斷線清理；純 SDK 測試不能代替 Player 驗收。
