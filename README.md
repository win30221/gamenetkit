# GameNetKit — Go＋Unity

可獨立搬移的 Go 伺服器與 Unity 即時連線框架。遊戲提供模擬規則、訊息格式與場景物件；框架提供固定步長執行、傳輸與可選的網路模組。目前套件版本為 0.1.0，尚未承諾跨版本 API 穩定。

## 第一次使用

先準備 PowerShell 7 與專案釘選的 Go 1.27.3（版本來源為 [toolchain.json](toolchain.json)）。工具不會自動安裝 Go；找不到或版本不同時，依 [工具設定](docs/MIGRATION.md) 指定 `GAMENETKIT_GO`。以下命令在下載後的 `gamenetkit` 根目錄執行：

```powershell
./examples/Run.ps1 -Example SharedWorld
```

保持終端開啟，手動開兩個 [多人方塊網頁](http://127.0.0.1:18092/) 視窗，各按「連線」。點場地後用 WASD／方向鍵移動：藍色是自己、橘色是對方；兩邊都應看見移動。一邊斷線後，另一邊的對方方塊會消失。完成後在終端按 `Ctrl+C` 停止。

這條入門路徑只需 Go、PowerShell 與瀏覽器，不需 Unity 或 .NET。其他教學的瀏覽器頁面多由 .NET 主機執行真正 C# session；要用它們才準備 .NET 8 SDK。Unity 客戶端則另外需要釘選 Editor、授權及目標平台建置支援。完整遊戲網頁需要先建置 WebGL。

教學腳本預設從 Unity 安裝取得 .NET SDK。若只安裝獨立 .NET 8 SDK，先在同一終端設定 `$env:GAMENETKIT_DOTNET = 'C:\實際安裝位置\dotnet.exe'`，再啟動教學；需包含 ASP.NET Core。安裝在 PATH 上並不會自動取代這個預設解析方式。

## 接進自己的遊戲

1. 讀 [Minimal 接線](docs/GETTING_STARTED.md)，理解輸入 → Go 固定 tick → 快照 → Unity 呈現。
2. 依 [Go SDK](server/README.md) 和 [Unity SDK](packages/unity/README.md) 引用本機套件；自己的 DTO、身份、房間與規則留在應用。
3. 多人遊戲對照 [Shared World](examples/shared-world/README.md) 的單一世界與 session 生命週期，再依 [組件接入指南](docs/COMPONENT_GUIDE.md) 加入插值、預測或其他能力。

需要一份 schema 產生 Go／C# 型別時，見選用 [Protobuf 範例](examples/protobuf/README.md)：官方生成器、runtime 與獨立 binary WebSocket 路徑；Unity 目標驗收狀態見範例報告。

## 資料夾用途

| 目錄 | 內容與入口 |
| --- | --- |
| [server](server/README.md) | Go SDK 函式庫；不是可直接 `go run .` 的遊戲服務 |
| [packages/unity](packages/unity/README.md) | 14 個本機 UPM 套件（含選用 Protobuf adapter／runtime）；不是 Unity 專案 |
| [examples](examples/README.md) | 可執行的服務、Unity 專案、網頁與 API 課程 |
| [native](native/README.md) | 桌面 WebTransport helper；使用 WebSocket 時不需要 |
| [tests](tests/README.md) | SDK、傳輸及依賴邊界驗證 |
| [tools](tools/README.md) | 工具解析、helper 建置與生成物清理 |
| [docs](docs/README.md) | 接線、架構、部署、維護與驗證文件 |

`.local`、`.cache`、`artifacts` 及各專案的 `Builds`／`Library` 是本機產物，不隨 Git 提供；新下載的專案必須自行安裝工具及建置所需 Player。

## 更多啟動方式與文件

完整遊戲使用 `./examples/Run.ps1 -Example Rumblebound` 本地啟動；Web 建置使用 `./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Web`，VM 部署使用 `./examples/rumblebound/scripts/Deploy.ps1`，見 [部署說明](docs/DEPLOYMENT.md)。

**直接開始：雙擊 [Start.cmd](Start.cmd)，輸入編號選範例。** 選單會列出用途與啟動網址，不必記 `-Example` 名稱。也可在專案外層的 PowerShell 執行 `./gamenetkit/examples/Run.ps1`；只看清單用 `./gamenetkit/examples/Run.ps1 -List`。

想先操作畫面，在選單選「1. 多人方塊」。需要理解最短 Go＋Unity 接線時，再看 [入門指南](docs/GETTING_STARTED.md) 的 Minimal：Go 1.27.3、Unity 6000.6.8f1，Windows／PowerShell 工作流程。Minimal 只使用 Unity core＋transport，展示單一方塊的輸入與快照；**每個連線有獨立世界**。

學習入口為 [examples 學習路線](examples/README.md)：先操作 Shared World，再讀 Minimal 最短接線，接著學多人世界與核心組件；Replay／AOI／Codec／History 依需求選修，最後到 Rumblebound 看整合。選單編號只是啟動入口。要在自己的遊戲加功能，讀 [組件接入指南](docs/COMPONENT_GUIDE.md)：包含從 Shared World 加入插值的逐步修改，以及每個組件的安裝、呼叫、重設與驗證入口。遷移範圍見 [工具版本與遷移](docs/MIGRATION.md)，實際驗證範圍見 [VALIDATION](docs/VALIDATION.md)。

在 `gamenetkit` 目錄可先建置 Windows 客戶端：

```powershell
./examples/Build-Unity.ps1 -Project Minimal -Target Windows
```

再依入門指南開兩個終端，分別啟動伺服器與 `examples/minimal-unity/Builds/Windows/Minimal.exe`；Web 建置及網址也在同一份指南。

| 文件 | 用途 |
| --- | --- |
| [檔案用途與清理](docs/FILE_AUDIT.md) | 逐檔用途、保留依據與整理結果 |
| [指令導覽](docs/COMMANDS.md) | 依操作／開發／驗證／建置／維護選擇入口 |
| [框架驗證](tests/README.md) | 測試用途、環境與執行範圍 |
| [維護工具](tools/README.md) | helper 建置、清理與內部支援 |
| [工具版本與遷移](docs/MIGRATION.md) | 版本釘選、自訂工具路徑與搬移內容 |
| [本機容量與維護](docs/MAINTENANCE.md) | 安全清理快取、舊建置與本地部署包；網路模組配置量測 |
| [GETTING_STARTED](docs/GETTING_STARTED.md) | 工具前提、Windows／Web 啟動與最小接線教學 |
| [Examples](examples/README.md) | 學習順序、共用世界與各組件的加入介紹 |
| [組件接入指南](docs/COMPONENT_GUIDE.md) | 從 Shared World 加入插值；各組件的前置條件、套件／assembly、接線與完成檢查 |
| [插值操作教學](examples/interpolation/README.md) | 直接用瀏覽器切換關閉／固定／自適應，觀察方塊效果與實際接線 |
| [預測與校正](examples/prediction/README.md) | 延遲下比較等待權威與立即移動，操作伺服器推移、直接／平滑校正 |
| [即時連線與恢復](examples/network-recovery/README.md) | 弱網、診斷、時鐘與自動重連的綜合操作 |
| [物件同步與動作確認](examples/objects-actions/README.md) | 選單 5：新增／移除／同 ID 重生，以及確認／拒絕／逾時 |
| [錄製與播放 Replay](examples/replay/README.md) | 選單 6：錄下移動與標記變化，再暫停、跳轉、倍速播放 |
| [AOI 興趣範圍同步](examples/interest/README.md) | 選單 7：走近與走遠，比較接收清單、範圍圈與世界物件數量 |
| [Codec 與量化](examples/codec/README.md) | 選單 8：實際資料大小、座標精度及缺基準恢復 |
| [歷史與延遲補償](examples/history-lagcomp/README.md) | 選單 9：讀取歷史位置、時間窗與目前／回溯命中 |
| [ARCHITECTURE](docs/ARCHITECTURE.md) | 責任、狀態所有權、資料流、生命週期與已知限制 |
| [MODULES](docs/MODULES.md) | 全部套件索引、相依與加入／停用／替換／移除契約 |
| [DEPENDENCIES](docs/DEPENDENCIES.md) | Gin、傳輸、限流等通用套件的選擇與應用邊界 |
| [Rumblebound example](examples/rumblebound/README.md) | 房間／選角／對戰／練習 AI／重連／錄製播放，重用原規則與美術 |
| [REPLAY](docs/REPLAY.md) | 伺服器錄製、匯出與離線播放 |
| [VALIDATION](docs/VALIDATION.md) | 驗證命令、前提、通過訊號與實際驗證範圍 |
| [歷史紀錄](docs/archive/README.md) | 第一版抽取計畫、工作紀錄與當時驗證證據 |

SDK 位於 [Go SDK](server/README.md) 與 `packages/unity/`；聚焦教學在 `examples/`。完整重製遊戲使用 `examples/rumblebound`，舊 samples/rumblebound 已移除，遊戲開發統一使用 examples/rumblebound。Windows、WebGL 與實際操作的驗證範圍見 [VALIDATION](docs/VALIDATION.md)。
