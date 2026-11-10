# 通用套件與框架責任

通用基礎設施優先採用 Go 標準庫、官方 SDK 或維護中的常用套件。gamenetkit 專注即時同步機制；遊戲規則與應用政策仍由 example／sample 決定。套件版本以各應用的 `go.mod`／`go.sum` 為準，按實際需求加入，不一次安裝尚未使用的功能。

## 目前選擇

| 功能 | 使用的套件 | 放置與責任 |
| --- | --- | --- |
| HTTP/API 路由、middleware、panic recovery | `github.com/gin-gonic/gin` v1.12.0 | Minimal、新 Rumblebound example 與既有 sample 的 HTTP 組裝；同步 SDK 不依賴 Gin |
| HTTP 服務、TLS、逾時、關閉、檔案服務 | Go `net/http`、`crypto/tls`、`context` | Gin 作為 `http.Server.Handler`；沿用標準 server 的生命週期與檔案回應能力 |
| WebSocket | `github.com/coder/websocket` v1.8.15 | 應用接受連線，`server/transport` 提供框架 Wire 適配；Gin 不代替 WS 協定實作 |
| QUIC／HTTP3、WebTransport | `github.com/quic-go/quic-go` v0.62.0、`github.com/quic-go/webtransport-go` v0.13.0 | 完整 sample、example 與選用原生 helper；協定套件管理連線與串流，SDK 不承擔應用 TLS／Origin 政策 |
| 訪客登入限流 | `golang.org/x/time/rate` v0.16.0 | sample 每個實際 peer 的 token bucket；應用維持有上限的 peer 清單與閒置清除 |
| Google 身份驗證 | `cloud.google.com/go/auth` v0.24.1 | 共用遊戲身份層驗證 Google token；session、房間席位、一次性 ticket 與權限屬於應用政策 |
| Unity 遊戲 JSON | `com.unity.nuget.newtonsoft-json` 3.2.2 | 完整 example 的 `UnityGameJson` 處理遊戲 DTO；設定禁止型別載入，限制深度。SDK 與純 C# 遊戲層仍注入 parser，沒有 Newtonsoft 相依 |
| 日誌 | Go `log/slog` | 兩個 example 的結構化啟動／錯誤日誌；既有 sample 的 `log` 仍可運作 |
| JSON、設定、隨機憑證、測試 | Go `encoding/json`、`flag`／`os`、`crypto/rand`、`testing`／`httptest` | 使用標準庫；既有嚴格 JSON 大小與欄位檢查保留，不因切換路由器放寬 |

Gin 版本依[官方 v1.12.0 release](https://github.com/gin-gonic/gin/releases/tag/v1.12.0)選定；限流行為依 [x/time/rate 文件](https://pkg.go.dev/golang.org/x/time/rate)。Go module graph 可能因應用使用的較新套件而選取較新的間接相依；不應直接改寫 `go.sum` 來消除差異。

## HTTP 接線

應用以 Gin 註冊真實路由，既有 `http.HandlerFunc` 用 `gin.WrapF` 接入，保留認證、Origin、cookie 與 JSON 驗證。這讓路由與 middleware 採用 Gin，同時沿用已驗證的應用處理邏輯。`http.Server` 負責監聽、TLS、逾時與停止；同步迴圈不接收 `gin.Context`。

GET 的健康與靜態端點也提供 HEAD；未知 API 路徑回 404，已知端點使用不支援的方法回 405。Unity 靜態資源與 webui 不應把錯誤 API 請求吞成首頁。預設不信任 forwarded IP，反向代理部署時由應用明確設定可信任來源，參見 [Gin 官方說明](https://github.com/gin-gonic/gin/blob/v1.12.0/docs/doc.md#dont-trust-all-proxies)。

sample 的 HTTP3 `/wt` 保留原生 handler：`webtransport-go` 的 upgrade 需要 HTTP3 writer 的 `Settingser` 與 `HTTPStreamer` 介面，不能直接用一般 Gin writer 包裝。它仍使用 quic-go 的標準協定實作；一般 HTTP/API 與 WS 入口則經 Gin。

## 限流政策

訪客登入使用每個 IP 最多突發 12 次、平均每分鐘補充 12 次的 token bucket，即每五秒補一個額度。這取代原本的一分鐘固定視窗：跨過舊視窗邊界不會突然再取得 12 次額度。

應用最多保留 256 個 peer，閒置一分鐘的項目可清除；尚未有空間時拒絕新 peer。限流仍以實際連線的 `RemoteAddr` 為準，不以請求自行提供的 `X-Forwarded-For` 分桶。演算法交給 `rate.Limiter`，身分分類、容量與回應政策由 sample 管理。

## 後續新增功能

完整 Rumblebound example 的 Unity JSON 邊界使用 Unity 官方 `com.unity.nuget.newtonsoft-json` 3.2.2，讓省略／null 的巢狀資料保持 null，避免 `JsonUtility` 建立空 inline 物件。範圍限於遊戲 Unity adapter；純 C# 遊戲層仍注入 `IGameJson`，通用 SDK 不引用 Newtonsoft、UnityEngine 或遊戲 DTO。套件版本見 [Unity 官方說明](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html)。

即時接收會重用已完成的 Snapshot 解析結果：未調理的 welcome／snapshot 不在 Deliver 再解析，codec 還原結果也直接用於 envelope 核對與後續驗證。下行調理仍保存原文字，到期才解析交付；身份、世代、tick 與快照內容驗證保持不變。這先減少重複解析，沒有變更 JSON 協定或引入另一套序列化相依。

先使用現有標準庫與已安裝的套件，再依功能需求選擇額外依賴。資料庫、Redis、完整設定框架或 DI 容器會在實際需要時加入；目前沒有這些執行前提。Unity 也優先使用對應版本的官方 package／平台 API，跨層組裝保持明確。

固定 tick、命令順序、預測重播、快照插值、歷史回溯與差分基準屬於框架的同步契約，不因採用 Gin 而交給 HTTP middleware。物件生命、碰撞、攻擊、房間及勝負仍在遊戲應用。模組契約見 [MODULES](MODULES.md)，驗證入口見 [VALIDATION](VALIDATION.md)。
