# 我現在該用哪個指令？

本頁是目前入口的統一索引。所有 PowerShell 命令從 gamenetkit 根目錄執行；各腳本可用 `Get-Help <路徑> -Full` 查看用途、前提、產物與參數。

| 指令 | 用途 |
| --- | --- |
| 雙擊 `Start.cmd` 或 `./examples/Run.ps1` | 操作教學範例選單。 |
| `./examples/Run.ps1 -Example Rumblebound` | 啟動完整遊戲範例。 |
| `./examples/Build-Unity.ps1 -Project Minimal -Target Windows` | 建置選定範例的 Unity Player。 |
| [學習路線](../examples/README.md)、[入門與接線](GETTING_STARTED.md) | 決定先看什麼，讀懂 Go／Unity 基本資料流。 |
| [組件接入指南](COMPONENT_GUIDE.md)、[模組契約](MODULES.md) | 在自己的專案安裝組件，接好更新、重設與呈現。 |
| [框架驗證](../tests/README.md) | 修改 server／packages 後檢查回歸。 |
| [跨範例驗證](../examples/tests/README.md) | 編譯或執行多個 Unity 範例。 |
| [維護與支援](../tools/README.md) | 建置 native helper、清理快取或理解內部工具鏈。 |

## 依角色選擇

操作範例從 `Start.cmd` 開始。開發自己的遊戲以 SDK 與接線文件為主。維護框架使用 `tests/scripts`；維護某個範例使用它自己的 `scripts` 或 `tests`。部署指令位於 Rumblebound 範例內，需明確提供主機與網域。

## 命名與位置

`Start.cmd` 是 Windows 的選單捷徑，直接呼叫 `examples/Run.ps1`。`Run` 啟動範例，`Build` 產生可執行檔，`Test` 驗證行為，`Deploy` 更新部署。

| 位置 | 何時使用 |
| --- | --- |
| `examples/Run.ps1`、`examples/Build-Unity.ps1` | 操作或建置範例。 |
| `examples/<範例>/scripts` 或 `tests` | 開發該範例時，使用專屬操作與驗證。 |
| `examples/tests` | 修改會影響多個範例的共用程式後驗證。 |
| `tests/scripts` | 修改 SDK 或傳輸實作後驗證。 |
| `tools` | 編譯 native helper 或清理產物。 |
| `tools/internal` | 由其他腳本載入的共用程式；通常不需手動執行。 |

在自己的遊戲引用 SDK 時，依入門文件接線即可。測試腳本是維護入口，不需複製到正式遊戲或在每次啟動前全部執行。

## 全部 PowerShell 指令

需要有效授權／預先建置的項目，以及暫移來源或操作遠端的項目，請先閱讀對應腳本的完整說明。

### `examples/Build-Unity.ps1`

範例建置：建置指定 Unity 範例的 Windows 或 WebGL Player。

前提：有效 Unity 授權、釘選 Editor 與目標平台模組。

行為與產物：寫入所選專案的 Builds 與根目錄 artifacts。

### `examples/codec/scripts/Run.ps1`

Codec 與量化：啟動Codec 與量化教學的本機服務。

前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。

行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。

### `examples/codec/scripts/Test-Codec.ps1`

Codec 與量化：驗證跨語言 codec、差分與基準恢復，以及網頁操作接線。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/history-lagcomp/scripts/Run.ps1`

歷史與延遲補償：啟動歷史與延遲補償教學的本機服務。

前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。

行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。

### `examples/history-lagcomp/scripts/Test-History.ps1`

歷史與延遲補償：驗證歷史時間窗、回溯命中與網頁操作接線。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/interest/scripts/Run.ps1`

AOI 興趣範圍：啟動AOI 興趣範圍教學的本機服務。

前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。

行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。

### `examples/interest/scripts/Test-Interest.ps1`

AOI 興趣範圍：驗證每個接收者的 AOI 篩選、範圍進出與網頁接線。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/interpolation/scripts/Run.ps1`

插值：啟動插值教學的本機服務。

前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。

行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。

### `examples/interpolation/scripts/Test-Interpolation.ps1`

插值：驗證實際 C# 插值、取樣模式與網頁控制接線。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/minimal-unity/tests/Test-Player.ps1`

Minimal 驗證：以真正 Minimal Windows Player 驗證 Go 服務連線。

前提：先建置 Minimal Windows；需要釘選 Go。

行為與產物：暫啟本機 Go 服務與 Player，結果寫入 artifacts。

### `examples/minimal-unity/tests/Test-Startup.ps1`

Minimal 驗證：驗證首份有效快照前不送輸入，以及終止狀態後停止送出。

前提：釘選 Go 與 .NET 8 SDK。

行為與產物：編譯實際 MinimalClient 與測試替身，使用真實 WebSocket；不代表 Player 渲染。

### `examples/modules/client/Run.ps1`

終端教學：執行選定的 C# SDK 組件教學。

前提：Go 教學需 Go；C# 教學需 .NET SDK。

行為與產物：輸出終端教學結果，不啟動遊戲畫面。

### `examples/modules/server/Run.ps1`

終端教學：執行選定的 Go SDK 組件教學。

前提：Go 教學需 Go；C# 教學需 .NET SDK。

行為與產物：輸出終端教學結果，不啟動遊戲畫面。

### `examples/modules/Test.ps1`

終端教學驗證：依序執行八個 C# 與四個 Go 組件主題。

前提：釘選 Go 與 .NET 8 SDK。

行為與產物：在終端輸出各主題結果，沒有 Unity 畫面。

### `examples/network-recovery/scripts/Run.ps1`

網路恢復：啟動網路恢復教學的本機服務。

前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。

行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。

### `examples/network-recovery/scripts/Test-NetworkRecovery.ps1`

網路恢復：驗證弱網、診斷、時鐘與斷線重連的教學流程。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/objects-actions/scripts/Run.ps1`

物件與動作確認：啟動物件與動作確認教學的本機服務。

前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。

行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。

### `examples/objects-actions/scripts/Test-ObjectsActions.ps1`

物件與動作確認：驗證完整物件清單、同 ID 重生與動作確認流程。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/prediction/scripts/Run.ps1`

預測與校正：啟動預測與校正教學的本機服務。

前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。

行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。

### `examples/prediction/scripts/Test-Prediction.ps1`

預測與校正：驗證輸入預測、權威校正與網頁控制接線。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/replay/scripts/Run.ps1`

錄製播放：啟動錄製播放教學的本機服務。

前提：釘選 Go 與 .NET 8 SDK；Unity 模式需先建置對應 Player。

行為與產物：依參數啟動本機服務或已建置 Player；Ctrl+C 停止。

### `examples/replay/scripts/Test-ReplayLesson.ps1`

錄製播放：驗證快照錄製、離線播放與播放控制的教學流程。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/rumblebound/scripts/Deploy.ps1`

Rumblebound：封裝或部署完整遊戲至指定 VM。

前提：釘選 Go、已建置 WebGL；實際部署另需 SSH、目標主機與網域。

行為與產物：預設會上傳並更新遠端服務；-PackageOnly 僅產生本機部署包。

### `examples/rumblebound/scripts/Export-Playback.ps1`

Rumblebound：驗證遊戲 journal 並匯出離線播放 JSON。

前提：釘選 Go 與已完成的 journal。

行為與產物：建立新的輸出檔；既有目的檔會被拒絕。

### `examples/rumblebound/scripts/Run.ps1`

Rumblebound：啟動完整遊戲的本機 API 與診斷主機。

前提：釘選 Go 與 .NET SDK；Unity 模式需先建置對應 Player。

行為與產物：服務保持執行至 Ctrl+C；-BuildOnly 僅編譯本機主機與 API。

### `examples/rumblebound/scripts/Test-Battle.ps1`

Rumblebound：驗證完整 Go API 與正式 C# 流程、SDK、重播。

前提：釘選 Go 與 .NET SDK。

行為與產物：預設建立隔離本機 API；-Origin 可改為指定現有服務。

### `examples/rumblebound/scripts/Test-BattlePlayer.ps1`

Rumblebound：用真正雙 Windows Player 驗證完整遊戲與 journal。

前提：先建置 Rumblebound Windows；需要 Go 與 .NET SDK。

行為與產物：建立隔離服務與 Player；預設驗證 WS，可用 -WebTransport 驗證 WT。

### `examples/rumblebound/scripts/Test-BattleRemoval.ps1`

Rumblebound：在隔離來源樹驗證只保留 core／transport 的完整遊戲。

前提：釘選 Go 與 .NET SDK。

行為與產物：建立不含十個選用套件的隔離樹，執行真實本機服務與 C# 流程；不移動正式來源。

### `examples/rumblebound/scripts/Test-Browser.ps1`

Rumblebound：驗證正式 JavaScript、靜態資源與本機遊戲 API。

前提：Node.js、Go、.NET SDK 與已建置的 Rumblebound WebGL。

行為與產物：DOM／Canvas 使用測試替身；可選 -WebTransport，並非人工瀏覽器畫面驗收。

### `examples/rumblebound/scripts/Test-CombatPlayer.ps1`

Rumblebound：驗證實際 Player 的戰鬥、移動與操作。

前提：先建置 Rumblebound Windows，並準備釘選 Go。

行為與產物：預設建立隔離本機 API；-Review 額外產生 HUD 渲染圖。

### `examples/rumblebound/scripts/Test-Deployment.ps1`

Rumblebound：驗證指定 HTTPS 站台的資源、安全標頭與遊戲流程。

前提：需明確指定 -Origin、網路、釘選 Go 與 .NET SDK。

行為與產物：會向指定站台發出請求並建立測試遊戲；不會部署。

### `examples/rumblebound/scripts/Test-HudLayout.ps1`

Rumblebound：產生桌面或觸控版 HUD 的實際 Player 渲染圖。

前提：已建置的 Windows Player、圖形環境與可用遊戲服務。

行為與產物：預設連本機 18093；-Origin 可指定站台，輸出 artifacts/hud-review。

### `examples/rumblebound/scripts/Test-OptionalRemoval.ps1`

Rumblebound：實際移除選用 Unity 套件後建置完整遊戲。

前提：有效 Unity 授權；需關閉該專案 Editor 並序列執行。

行為與產物：會暫移共用套件與修改 manifest／asmdef，結束時還原；屬進階移除契約驗證。

### `examples/rumblebound/scripts/Test-Replay.ps1`

Rumblebound：驗證 SDK／遊戲 journal、真實錄製、離線重建與篡改拒絕。

前提：釘選 Go。

行為與產物：建立 artifacts/replays 與驗證報告，會用測試副本驗證預期失敗。

### `examples/rumblebound/scripts/Test-TouchPlayer.ps1`

Rumblebound：驗證實際 Player 的多指控制流程。

前提：已建置的 Windows Player 與可用遊戲服務。

行為與產物：預設連本機 18093，可用 -Origin 指定站台；不代表實體手機驗收。

### `examples/Run.ps1`

範例操作：顯示範例選單，或啟動指定的教學／遊戲。

前提：PowerShell 7、Go；部分範例需要 .NET SDK，Unity 模式需先建置 Player。

行為與產物：啟動本機服務；Ctrl+C 停止服務。

### `examples/shared-world/scripts/Test-SharedWorld.ps1`

共用世界：驗證共用世界的加入／離開、玩家輸入與客戶端隔離。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立隔離本機服務，結果寫入 .local／artifacts；完成後關閉自己啟動的程序。

### `examples/tests/Test-UnityCompilation.ps1`

跨範例驗證：編譯教學與完整遊戲的 Unity、Editor、原生／WebGL 條件分支。

前提：釘選 Unity 參考組件；先匯入或建置 Rumblebound，還原 URP／Newtonsoft 套件。

行為與產物：只做參考組件編譯，不代表 Editor 匯入或 Player 執行；產物存 .local/unity-lessons。

### `examples/tests/Test-UnityPlayers.ps1`

跨範例驗證：驗證七個教學範例的雙 Windows Player 與 Go 服務。

前提：先建置所選範例的 Windows Player；需要釘選 Go。

行為與產物：為每個範例建立隔離服務與兩個 Player，日誌存 artifacts。

### `tests/scripts/Test-GoAndBrowser.ps1`

框架驗證：執行 14 個 Go 模組 test／vet，以及傳輸、共用網頁與 Shared World 的 Node 測試。

前提：釘選 Go 與 Node.js 22 以上；首次還原需要網路。

行為與產物：執行 SDK 與各範例 Go 測試，失敗時中止；不啟動 Unity Editor。

### `tests/scripts/Test-GoModuleRemoval.ps1`

進階框架驗證：驗證選用 Go 模組移除後的 SDK 與應用備援。

前提：釘選 Go；需序列執行，避免其他程序同時編譯來源。

行為與產物：會暫移 server 選用模組並在 finally 還原；日誌存 artifacts。

### `tests/scripts/Test-Transport.ps1`

框架驗證：驗證真實 WS／HTTP3 傳輸與 JavaScript browser bridge。

前提：釘選 Go、.NET 8 SDK 與 Node.js。

行為與產物：建立本機測試服務與 helper 子程序；完成後清理自己啟動的程序。

### `tests/scripts/Test-UnitySdk.ps1`

框架驗證：在真正 Unity Editor 執行獨立 SDK 演算法測試。

前提：有效 Unity 授權與釘選 Editor。

行為與產物：批次啟動 tests/sdk-unity，日誌為 artifacts/sdk-tests.log。

### `tools/Build-NativeTransport.ps1`

SDK 建置：編譯原生桌面 WebTransport helper。

前提：釘選 Go 工具鏈。

行為與產物：寫入 native/webtransport/Builds；可指定目標 OS／架構。

### `tools/Clean-Workspace.ps1`

維護：列出或清理白名單內的可重建快取與舊產物。

前提：PowerShell 7；實際清理前需結束 Unity 與測試。

行為與產物：實際執行會刪除白名單產物；先用 -WhatIf 檢視。

### `tools/internal/Go-Env.ps1`

內部支援：選用釘選 Go 並設定目前 PowerShell 的 Go 快取環境。

前提：由 Go 啟動、建置與測試腳本匯入。

行為與產物：設定目前程序的環境變數與 $Go；不寫入系統環境。

### `tools/internal/Toolchain.ps1`

內部支援：讀取 toolchain.json，提供 Go／Unity／.NET 路徑解析函式。

前提：由其他腳本 dot-source 匯入。

行為與產物：定義共用根目錄與解析函式；通常不需手動執行。

## Python 依賴守門

`python ./tests/scripts/Check-Dependencies.py` 檢查 SDK／範例依賴邊界；`python -B -m unittest discover -s ./tests/dependencies -v` 執行守門規則的反例測試。兩者都不啟動 Unity 或遊戲服務。

## 重新產生測試資料

[Codec fixtures](../tests/fixtures/README.md) 說明何時更新 Go／C# 共用資料，以及如何執行 `tests/fixtures/generate-codec/main.go`。一般啟動或跑測試不需先重新產生。
