# Rumblebound 操作與驗證

所有命令從 gamenetkit 根目錄執行；完整前提、產物與副作用見 [指令導覽](../../../docs/COMMANDS.md)，參數用 `Get-Help <路徑> -Full` 查看。

| 指令 | 什麼時候使用 |
| --- | --- |
| [Run.ps1](Run.ps1) | 啟動本機 API 與診斷主機；通常透過 `examples/Run.ps1 -Example Rumblebound` 呼叫。 |
| [Deploy.ps1](Deploy.ps1) | 將已建置的完整遊戲部署到指定 VM；`-PackageOnly` 僅打包。 |
| [Export-Playback.ps1](Export-Playback.ps1) | 將已完成的伺服器 journal 匯出為觀看用播放檔。 |
| [Test-Battle.ps1](Test-Battle.ps1) | 修改房間／對戰流程或 C# 接線後，驗證真實 Go API 與 C# 客戶端。 |
| [Test-Browser.ps1](Test-Browser.ps1) | 修改網頁橋接或資源路由後，驗證 JS、API 與建置資源；DOM／Canvas 使用替身。 |
| [Test-BattlePlayer.ps1](Test-BattlePlayer.ps1) | 建置 Windows 後，以兩個實際 Player 驗證房間、戰鬥、重連及重播；可測 WT。 |
| [Test-CombatPlayer.ps1](Test-CombatPlayer.ps1) | 修改操作與戰鬥後，以實際 Player 驗證移動、追擊、攻擊、技能及鏡頭。 |
| [Test-TouchPlayer.ps1](Test-TouchPlayer.ps1) | 修改觸控接線後，驗證 Player 的多指控制；實體手機需另行驗收。 |
| [Test-HudLayout.ps1](Test-HudLayout.ps1) | 修改介面後，產生桌面／觸控 HUD 渲染圖供檢視。 |
| [Test-Replay.ps1](Test-Replay.ps1) | 修改錄製或規則後，驗證 journal、離線重建與篡改拒絕。 |
| [Test-BattleRemoval.ps1](Test-BattleRemoval.ps1) | 在隔離來源樹驗證僅保留 core／transport 的對戰流程。 |
| [Test-OptionalRemoval.ps1](Test-OptionalRemoval.ps1) | 進階：暫移共用 Unity 套件後真正建置，最後還原；需關閉 Editor 並序列執行。 |
| [Test-Deployment.ps1](Test-Deployment.ps1) | 部署後驗證明確指定的 HTTPS 站台；會建立測試遊戲。 |

日常啟動不需先跑全部測試。一般流程修改先跑 Test-Battle；Player、介面、觸控、部署與移除契約有各自的驗證範圍。

隔離移除測試的 C# 主機位於 `../tests/removal/Program.cs`，由 Test-BattleRemoval.ps1 使用。
