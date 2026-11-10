# Minimal：讀懂最短 Go＋Unity 接線

Minimal 展示最短的「輸入 → Go 模擬 → 快照 → Unity 方塊」路徑。Unity 只安裝 core 與 transport；沒有預測、插值、認證或自動重連。伺服器在每個 `/ws` 連線內建立獨立世界，開兩個客戶端不會互相看見。

第一次接觸先到 [examples 學習路線](../examples/README.md)，用 Shared World 看兩名玩家互動；接著回本頁讀最短程式接線。只讀程式可直接跳到第 4 節，不必先完成 Unity 建置。要親自執行 Minimal，再依第 1～3 節準備與啟動。

讀完後回 [Shared World 的接線順序](../examples/shared-world/README.md#接線順序) 理解多人世界 owner，再用 [組件接入指南](COMPONENT_GUIDE.md) 練習加第一個組件。Minimal 是接線基礎，選單中的位置不代表它是進階課。

Go 入口使用 Gin 註冊 HTTP／WS 路由、`coder/websocket` 處理 WS，以及標準庫 `log/slog` 記錄日誌；框架負責固定 tick 與傳輸適配。完整通用套件選擇見 [DEPENDENCIES](DEPENDENCIES.md)。

## 1. 準備工具與目錄

以下命令使用 Windows 上的 PowerShell 7（`pwsh`），起始目錄均為 **`gamenetkit`**；例如目前工作區可先執行：

```powershell
Set-Location 'C:\path\to\gamenetkit' # 換成實際下載位置
```

若已搬移資料夾，請改成搬移後的位置。保留框架內部目錄配置，套件與範例依相對路徑互相引用。

| 前提 | 核對方式與用途 |
| --- | --- |
| Go 1.27.3 | [minimal-server/go.mod](../examples/minimal-server/go.mod) 指定此版本；`Go-Env.ps1` 會選擇專案 `.local/toolchains/go1.27.3/bin/go.exe` 或對應版本的系統安裝 |
| Unity 6000.6.8f1 | 由 [ProjectVersion.txt](../examples/minimal-unity/ProjectSettings/ProjectVersion.txt) 指定；需完成授權啟用 |
| Unity 安裝位置 | 工具依專案版本解析 Unity，支援 `.local/toolchains/unity/6000.6.8f1` 與標準 Hub 安裝位置 |
| 建置支援 | Unity Hub 安裝目標所需的 Windows／WebGL Build Support |
| 首次套件還原 | Go module 與 Unity 套件尚未快取時，需要網路存取 |
| 本機連接埠 | Minimal 使用 `127.0.0.1:18091`；啟動前須未被其他程序占用 |

執行完整驗證工具另需 Node.js 22 以上與可執行 Python 3 的命令；單純啟動 Minimal 不需這兩者。各測試前提見 [VALIDATION](VALIDATION.md)。

先檢查 Go 與 Unity 路徑：

```powershell
. ./tools/internal/Go-Env.ps1
& $Go version
Resolve-GameNetKitUnityEditor
```

[Go-Env.ps1](../tools/internal/Go-Env.ps1) 在目前 PowerShell 設定 `$Go`、`GOPATH`、`GOMODCACHE` 與 `GOCACHE`。快取預設放在專案自己的 `.cache`，不依賴舊資料夾或父目錄。每個需要 `$Go` 的新終端都要重新 dot-source 此腳本。

[Build-Unity.ps1](../examples/Build-Unity.ps1) 使用共同的 Unity／.NET 工具解析，依專案指定版本建置；自訂安裝位置與環境變數見 [工具版本與遷移](MIGRATION.md)。所有命令均可從新專案根目錄執行。

## 2. 啟動 Windows 範例

### 終端 A：啟動伺服器

從 `gamenetkit` 開始：

```powershell
. ./tools/internal/Go-Env.ps1
Set-Location ./examples/minimal-server
& $Go run -buildvcs=false .
```

看到 `Minimal framework example` 日誌與 `url=http://127.0.0.1:18091` 後，保持此終端執行。**伺服器工作目錄必須是 `examples/minimal-server`**，因為 Web 資源掛載使用相對路徑。結束時按 `Ctrl+C`。

### 終端 B：建置並開啟客戶端

另一個 PowerShell 從 `gamenetkit` 開始：

```powershell
Invoke-WebRequest 'http://127.0.0.1:18091/healthz' | Select-Object StatusCode, Content
./examples/Build-Unity.ps1 -Project Minimal -Target Windows
& ./examples/minimal-unity/Builds/Windows/Minimal.exe
```

健康檢查預期為 HTTP 200、內容 `ok`。客戶端出現方塊後，按住左右方向鍵移動，放開即停止。位置由伺服器快照直接呈現，因此可看出快照更新的步階。

建置由 [MinimalBuild.cs](../examples/minimal-unity/Assets/Editor/MinimalBuild.cs) 建立空場景；[MinimalClient.cs](../examples/minimal-unity/Assets/MinimalClient.cs) 在載入後自行建立相機、方塊與連線物件。建置日誌為 `artifacts/minimal-Windows.log`，成功入口會寫出 `RF_MINIMAL_BUILD PASS`。

### 可選：執行 Windows 冒煙測試

先關閉終端 A 的手動伺服器，並確認 Windows 客戶端已建置。在 `gamenetkit` 執行：

```powershell
./examples/minimal-unity/tests/Test-Player.ps1
```

[Test-Player.ps1](../examples/minimal-unity/tests/Test-Player.ps1) 會自行編譯並啟動伺服器，再執行 Windows 客戶端的 `-rf-minimal-smoke` 模式。此模式持續向右約四秒，要求收到的 `x > 1` 且時間軸 `RenderTick > 0`；通過訊號為 `RF_MINIMAL PASS`，日誌在 `artifacts/minimal-smoke.log`。腳本結束會清理它啟動的程序；它不代替 Web 驗證。

## 3. 啟動 Web 範例

終端 A 使用上一節完全相同的伺服器命令。終端 B 從 `gamenetkit` 建置：

```powershell
./examples/Build-Unity.ps1 -Project Minimal -Target Web
Invoke-WebRequest 'http://127.0.0.1:18091/healthz' | Select-Object StatusCode, Content
Start-Process 'http://127.0.0.1:18091/unity/'
```

瀏覽器網址是 [http://127.0.0.1:18091/unity/](http://127.0.0.1:18091/unity/)，不是 Windows `.exe`，也不是直接開啟建置目錄的 `index.html`。點選 Unity 畫布取得焦點，再按左右方向鍵。

| 資源 | 實際位置／端點 |
| --- | --- |
| Web 建置 | `examples/minimal-unity/Builds/WebGL/` |
| Web 建置日誌 | `artifacts/minimal-Web.log` |
| Unity 網頁 | 伺服器將上述建置目錄掛載於 `/unity/` |
| 瀏覽器傳輸模組 | `/transport.js` 對應 `packages/unity/transport/Browser/transport.js` |
| WebSocket | `ws://127.0.0.1:18091/ws` |

Minimal 選用 WebSocket，沒有 WebTransport／TLS 的啟動設定。連線網址寫在 `MinimalClient.Start()`，目前只適用同一台電腦上的本機示範；換主機或 HTTPS 部署需由應用調整網址與伺服器設定。

WebGL bridge 會從伺服器根路徑載入 `/transport.js`，Minimal 與 sample 共用同一份 SDK runtime。傳輸原樣交付文字，遊戲 ping/pong 由應用適配器處理；bridge 仍共用單連線狀態，部署子路徑與多連線限制見 [ARCHITECTURE](ARCHITECTURE.md)。

## 4. 看懂 Go 與 Unity 如何接線

### 本機套件引用

Minimal Go 應用的 [go.mod](../examples/minimal-server/go.mod) 使用：

```go
require github.com/win30221/gamenetkit/server v0.0.0

replace github.com/win30221/gamenetkit/server => ../../server
```

`replace` 以 **go.mod 所在目錄**為基準，指向框架 `server/`。建立自己的 Go module 時，保留 SDK module 名稱並依新位置調整 `replace`，不必複製 SDK 原始碼或改 SDK import。

Unity 的 [Packages/manifest.json](../examples/minimal-unity/Packages/manifest.json) 使用：

```json
{
  "dependencies": {
    "com.realtime.core": "file:../../../packages/unity/core",
    "com.realtime.transport": "file:../../../packages/unity/transport",
    "com.unity.modules.jsonserialize": "1.0.0"
  }
}
```

UPM 的 `file:` 相對路徑以專案的 **`Packages` 目錄**為基準；此範例從 `examples/minimal-unity/Packages` 回到框架根目錄，再進 `packages/unity`。另建 Unity 專案時應按實際位置調整路徑。Minimal 沒有安裝選用套件；完整索引見 [MODULES](MODULES.md)。

### 伺服器：先握手，再交給單一模擬迴圈

讀取 [minimal-server/main.go](../examples/minimal-server/main.go) 的 `/ws` handler：

1. 接受 WebSocket，建立 `transport.WebSocket`，並在三秒內以 `ReadHello` 讀取 hello。
2. 為此連線建立 `commands`、`updates` 與 `loop.Run`。模擬迴圈是位置 `x`、方向 `drive`、`tick` 的寫入者。
3. 讀取 goroutine 以 `ReadInput` 收 JSON，反序列化為應用自己的 `input` DTO，再送進命令佇列。`Apply` 只接受 `[-1, 1]` 的 `x`。
4. 每秒 30 tick，每 tick 執行 `x += drive / 30`；每兩 tick 送一份快照，約每秒 15 份。寫入 goroutine 將快照送回此連線。
5. 讀取結束後取消 context，等待模擬迴圈退出，再釋放連線。

目前應用訊息範例如下，每行各是一份獨立的 WebSocket 文字訊息；JSON schema 由 Minimal 定義，並非框架統一遊戲格式：

```text
{"type":"hello"}
{"type":"input","x":1}
{"type":"snapshot","tick":2,"x":0.06666666666666667}
```

hello 是開線首包；Minimal 伺服器只讀取並捨棄首包，沒有驗證 hello schema 或進行認證。`input.x` 是持續方向而非絕對位置，`snapshot.x` 是權威位置。`tick` 是此連線世界的模擬序號，不是毫秒時間。Minimal 沒有 input sequence／ack，也沒有跨連線房間狀態。

### Unity：收包 → 時間軸 → 呈現 → 送輸入 → 釋放

以 [MinimalClient.cs](../examples/minimal-unity/Assets/MinimalClient.cs) 為接線入口，不必複製整支程式：

| 入口 | 呼叫順序與應用責任 |
| --- | --- |
| `Start()` | 建立場景物件，呼叫 `BeginAttempt()` 取得 epoch，再用 `Open(epoch, url, hello, hash, target, false)` 開 WS；Minimal 的 hash 為空字串，target 是接收 WebGL callback 的 GameObject 名稱 |
| `OnTransportMessage(string)` | WebGL bridge 的 callback，將字串交給 `Receive(connection.Epoch, json)`；原生傳輸自行接收並排入相同收件佇列 |
| `Update()` 收包 | 先以 `TryRead(out message)` 排空目前 epoch 的訊息，再交給 `OnWire(message.JSON)`；Unity 物件操作留在主執行緒 |
| `OnWire()` | 應用解析 `PointState`，收到 `error` 時清空狀態並關線；有效 snapshot 須 tick 非負且 x 有限，才保存最新狀態，並以 `timeline.Observe(tick, now)` 通知時間軸 |
| `Update()` 呈現 | 每幀 `timeline.Advance(now)`，再將最新 `state.x` 套到方塊位置 |
| `Update()` 送輸入 | 收到第一份有效快照後，約每秒 30 次讀方向鍵，在 `!connection.Sending` 時呼叫 `Send(json)`；放開鍵時持續送 `x:0` |
| `OnDestroy()` | 呼叫 `connection.Dispose()`，關閉目前連線並清空收件佇列 |

Minimal 的 `LatestSnapshotTimeline` 只追蹤最新 tick，`Advance()` 不外推，也不自動保存快照或更新 Transform。要加入插值，應用仍須保存可取樣的快照並依 `RenderTick` 呈現；逐步實作見 [組件接入指南](COMPONENT_GUIDE.md)，API 見 [MODULES](MODULES.md)。

`Incoming.ReceivedMS` 是傳輸收包時取得的 `MessageTransport.ClockMS` 時間，單位為毫秒。Minimal 為簡化接線，在處理快照時使用 `Time.realtimeSinceStartupAsDouble` 秒；做量測或自適應時間軸時，需明確選定同一單調時鐘並換算單位，不能直接混用兩者。詳見 [ARCHITECTURE](ARCHITECTURE.md)。

`BeginAttempt()` 會先關閉舊連線並更新 epoch；`TryRead()` 只交付目前 epoch 的訊息。自己的應用若加入重連，還要清理舊世界快照、輸入與時間軸；Minimal 本身沒有此流程，關閉後需重新啟動客戶端。

## 5. 常見問題與下一步

| 現象 | 先檢查 |
| --- | --- |
| 找不到 Unity.exe | 核對釘選版本與目標 Build Support；用 `Build-Unity.ps1 -UnityEditor 'C:\實際位置\Editor\Unity.exe'` 或 `GAMENETKIT_UNITY_EDITOR` 指定 Editor |
| 建置停在授權初始化 | Unity Hub 登入與授權狀態，接著讀取 `artifacts/minimal-Windows.log` 或 `artifacts/minimal-Web.log`；僅啟動程序不代表建置完成 |
| `/healthz` 無法連線 | 終端 A 是否仍執行，是否占用 18091，Go 命令是否報錯 |
| `/unity/` 404／空白 | 是否先完成 Web 建置、伺服器是否從 `examples/minimal-server` 執行，瀏覽器開發工具是否報 `/transport.js` 或建置資源載入錯誤 |
| 方塊存在但不移動 | 頁面／視窗焦點、伺服器是否存活、WS 是否開啟，以及 Console 是否收到 `error`；Minimal 未將傳輸錯誤顯示成 UI |
| `Test-Player.ps1` 回報占用 | 先停止手動伺服器；該腳本需要自行占用 18091 |
| 搬移後套件找不到 | Go `replace` 與 Unity `file:` 是否仍指向框架套件；保留內部目錄時不需改路徑 |

完成本頁後，應能沿著 input → Go tick → snapshot → Unity Render 指出每一步的程式位置，也知道 Minimal 每個連線各有獨立世界。接著回 [Shared World](../examples/shared-world/README.md) 看多人共用世界，再依 [組件接入指南](COMPONENT_GUIDE.md) 加入需要的能力。[ARCHITECTURE](ARCHITECTURE.md) 與 [MODULES](MODULES.md) 可在接線時查所有權、重設邊界與 API；完整整合見 [Rumblebound](../examples/rumblebound/README.md)。

