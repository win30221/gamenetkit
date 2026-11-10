# GameNetKit 工具版本與遷移

2026-11-06：專案從 RealtimeFramework 遷至 `C:\Users\H\Documents\GitHub\win30221\gamenetkit`。目標既有 Git 儲存庫保留，來源提交歷史未匯入。來源目錄保留作備份；本機筆記與 replay 保存在目標的 ignored 目錄。

## 工具版本

[toolchain.json](../toolchain.json) 集中指定 Go 1.27.3 與 Unity Editor 6000.6.8f1。版本依 [Go 官方下載](https://go.dev/dl/) 與 [Unity 正式版發布](https://unity.com/releases/editor/whats-new/6000.6.8f1) 核對；不使用 alpha／beta Editor。

PowerShell 工具透過 [Toolchain.ps1](../tools/internal/Toolchain.ps1) 解析工具，並確認 Go 版本。Go 可安裝於專案的 `.local/toolchains/go1.27.3`、系統 PATH 或 Program Files；Unity 優先使用專案 `.local/toolchains/unity/6000.6.8f1`，否則解析 Hub 的對應版本。12 個 Unity 專案的 ProjectVersion 與集中版本需一致。

| 環境變數 | 用途 |
| --- | --- |
| `GAMENETKIT_GO` | Go 執行檔 |
| `GAMENETKIT_UNITY_EDITOR` | Unity.exe |
| `GAMENETKIT_UNITY_DATA` | Editor/Data 目錄 |
| `GAMENETKIT_DOTNET` | 包含所需 SDK 與 ASP.NET Core 的 dotnet.exe |
| `GAMENETKIT_CACHE` | Go 快取根目錄 |

既有 `-DotNet`／`-UnityData` 參數仍可覆寫；Unity 建置工具提供 `-UnityEditor`。Go 快取預設位於專案 `.cache`，明確設定 `GAMENETKIT_CACHE` 時會覆寫三個 Go cache 路徑；未設定時尊重既有 GOPATH／GOMODCACHE／GOCACHE。移動專案不會自動借用父目錄快取。

## 程式與資料

14 個 Go module 使用與子目錄一致的 `github.com/win30221/gamenetkit/...`；本機範例保留相對 replace。Unity 套件 ID `com.realtime.*`、C# namespace、素材 GUID 與網路協定維持既有契約。

原始碼、素材、meta、manifest、套件 lock、文件及啟動腳本完整遷移。Library、Temp、Go cache 與舊 Player 可重新產生；本機金鑰與測試憑證不加入 Git。舊筆記與 replay 另行保留。

仍引用已移除 sample 的驗證工具已改接目前 example，失效的舊 C# 測試主機另存於 `docs/archive/legacy-tools`。舊結果保存在 [遷移前驗證紀錄](archive/VALIDATION-2026-11-03.md)，本次結果見 [VALIDATION](VALIDATION.md)。

## 本機安裝與修復紀錄

本機 Go 1.27.3 安裝於專案 `.local/toolchains/go1.27.3`，所有啟動／建置／測試腳本優先選用它；系統原本的 Go 1.27.1 保留。因此在一般終端直接輸入 `go version` 可能仍顯示系統版本；使用 `. ./tools/internal/Go-Env.ps1` 後以 `& $Go version` 核對專案工具鏈。

Unity 6000.6.8f1 安裝於 `.local/toolchains/unity/6000.6.8f1`，已在 Unity Hub 註冊為預設版本，Web 與 Windows IL2CPP 模組辨識正常；本次 Windows Player 使用 Mono。

本次官方安裝器在較深路徑遺漏 URP 檔案；短路徑重新安裝恢復了缺檔，再還原到正式位置，共補回 787 個官方檔案、替換 3 個大小不一致檔案。Unity 官方也曾指出此類錯誤可能與 Windows 路徑長度有關：[官方說明](https://discussions.unity.com/t/errors-when-creating-a-new-unity-6-3-urp-project-the-type-or-namespace-name-materialreferencechanger-could-not-be-found/1701487/28)。日後重新安裝建議先選較短位置，或用 `GAMENETKIT_UNITY_EDITOR` 指定短路徑安裝。修復後 SDK、Windows／WebGL 建置與實際雙 Player 均通過；臨時安裝副本已清除。

Git 來源歷史未匯入，原始工作樹保留；本次沒有自動 commit 或 push。

後續工具清理已統一啟動入口，移除兩個重複腳本，並將範例專用工具與部署設定移入 Rumblebound。現行位置與用途見 [工具索引](../tools/README.md)。
