# 本機容量與網路模組維護

在框架目錄，以 PowerShell 7 執行：

```powershell
# 先查看清理清單，不刪除內容。
./tools/Clean-Workspace.ps1 -IncludeBuildCache -IncludeOldBuilds -WhatIf
# 清理已確認的產物。
./tools/Clean-Workspace.ps1 -IncludeBuildCache -IncludeOldBuilds
```

基本清理包含 Unity `Library/Temp/Obj/Logs`、工具測試主機、暫存瀏覽器設定檔、空的模組移除檢查備份，以及舊部署包。部署包預設保留最新一份壓縮檔；可用 `-KeepDeploymentPackages 2` 保留兩份。壓縮檔對應的展開 `bundle` 可重新解壓，會一併清除。

`-IncludeBuildCache` 額外刪除 Go 編譯快取，保留已下載的 `gomod` 依賴。`-IncludeOldBuilds` 額外清除舊原生 Player 與選用模組移除驗證建置，保留所有 WebGL 教學，以及 `examples/rumblebound/client/Builds/Windows`。需要被清除的 Windows 教學 Player 時，重新執行 `examples/Build-Unity.ps1 -Project <名稱> -Target Windows`。

工具只使用明列的產物路徑，刪除前檢查工作目錄邊界、檔案系統連結與 Git 已追蹤檔案；不清除原始碼、美術來源、金鑰與憑證、`UserSettings`、未列入清單的自訂 replay 目錄及目前執行中程式的目錄。請先結束 Unity Editor、測試及建置工作。下一次開啟 Unity 需要重新匯入素材；Go 的首次編譯也會較久。完整遊戲原有的可執行建置仍保留，修改 SDK 後需依原建置流程重新產生 Player 才會包含新程式碼。

## 2026-11-03 的實作調整

- Transport 新增可回報拒收的 `TrySend`；`Send` 的忙碌／未連線拒收明確失敗。WebGL bridge 回報實際送出路徑的失敗；應用仍以伺服器確認判定操作生效。
- 原生 WebSocket 每條連線重用有界接收緩衝，保留分片 UTF-8、精確 32 KiB 與空最終分片驗證。接收佇列在既有鎖內使用 `Queue`。
- AOI 超量時保留全部 `Always` 物件，其餘依半徑／遲滯篩選，再按距離和 ID 選擇至容量上限。正常數量維持原有行為。
- Delta decoder 重用暫存陣列，歷史基準仍獨立持有內容；ReplicaStore 重用 staging 字典，保留完整清單驗證與 `Values` 引用穩定。
- Replay queue 攜帶 tick/kind，移除背景寫入為讀取摘要而執行的第二次 JSON 解析。
- 模組移除驗證在恢復來源後清除空 stash，避免累積空的備份目錄。

同一 `.NET 8`、`/optimize+` 配置量測，暖機 128 次後各執行 2,000 次，三輪配置數一致：

| 情境 | 修改前 | 修改後 |
| --- | ---: | ---: |
| 1,024-byte 連續 delta 解碼 | 19,560 B/次 | 3,152 B/次 |
| 64 個物件含深複製的快照套用 | 9,744 B/次 | 5,120 B/次 |

仍有 Base64、結果陣列、字串與模型複製配置。這是 SDK 局部配置量，不能換算為整體 Player FPS 或 GC 改善比例。Replay 被移除的背景 JSON 解析另測得約 950–1,506 ns、144 B、3 次配置／記錄，也不代表端到端錄製比例。

原生 WS、實際 HTTP/3 WT、browser transport／bridge、Go 與 .NET SDK 行為測試已執行。Unity 批次測試因授權服務連線逾時，未執行到測試；不將 .NET 適配執行結果當成 Unity Player 驗收。

Minimal 另以 `examples/minimal-unity/tests/Test-Startup.ps1` 編譯實際 Client 的 Start/Update 與真實 WebSocket，驗證首份有效快照前不送輸入，以及終止錯誤後停止送出；只有 Unity 畫面與輸入 API 使用測試替身。WebGL bridge 另驗證載入中拒收後可重試，以及錯誤回呼同步重連時的 epoch 隔離。

本次清理後，已盤點工作檔約由 25.1 GB 降至 2.3 GB（Git 約 17 MB 另計，不計連結至工作目錄外的工具）；C 槽可用空間由 29,603,053,568 增至 52,147,535,872 bytes，增加約 22.5 GB。未刪除 Git 已追蹤來源，保留的本地遊戲服務仍回應 HTTP 200。
