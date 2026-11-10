# Go 伺服器 SDK

此 Go module 提供可被遊戲服務引用的函式庫。遊戲的 main、HTTP 路由、身份、房間與規則放在各範例中。

## 如何引用

本目錄沒有 `main`，不能用 `go run .` 啟動。先從框架根目錄執行 `./examples/Run.ps1 -Example SharedWorld` 操作範例。

自己的應用若位於 `examples/my-game/server`，在其 `go.mod` 合併以下項目（保留自己的 module 與 go 宣告）：

```go
require github.com/win30221/gamenetkit/server v0.0.0
replace github.com/win30221/gamenetkit/server => ../../../server
```

`replace` 相對於應用的 go.mod；應用在其他位置時須調整。接著 import `github.com/win30221/gamenetkit/server/loop` 等需要的 package，在應用目錄執行 `go mod tidy`。此處使用倉庫內來源，不假設 v0.0.0 已發布至遠端。

完整可執行接線見 [Minimal](../docs/GETTING_STARTED.md#4-看懂-go-與-unity-如何接線)：應用接受連線、解析自己的 input，將命令交給 loop 的單一 owner，再序列化 snapshot。選用功能見 [Go 組件接入](../docs/COMPONENT_GUIDE.md#5-go-組件放進模擬與輸出流程)。

## 套件分工

| 目錄 | 負責什麼 |
| --- | --- |
| netcore | 傳輸、編碼、歷史、AOI 與重播的共用契約。 |
| loop | 單一 owner 的固定步長執行。 |
| transport | WebSocket 與可替換的收送介面。 |
| codec | 選用的快照差分與量化。 |
| history | 選用的有界歷史資料。 |
| lagcomp | 選用的回溯時間授權政策。 |
| interest | 選用的 AOI 篩選。 |
| replay | 選用的 journal 錄製及完整性驗證。 |

最短服務入口見 [minimal-server/main.go](../examples/minimal-server/main.go)，完整遊戲入口見 [cmd/battle/main.go](../examples/rumblebound/server/cmd/battle/main.go)。各 *_test.go 驗證同目錄的 SDK 契約；整體指令見 [框架驗證](../tests/README.md)。

codec 測試資料產生器已歸到 [tests/fixtures](../tests/fixtures/README.md)。套件 API 與選用契約見 [MODULES](../docs/MODULES.md)。
