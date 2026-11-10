# 工作目錄合併（2026-11-10）

正式工作目錄：`C:\Users\H\Documents\GitHub\win30221\gamenetkit`。後續編輯、測試與 Git commit 都應從此處執行。

## 發現與處理

- 11/6 的專案遷移、Go／Unity 工具鏈升級、模組名稱替換與工具重整，以及 11/9 的文件更新，均在 GameNetKit 目錄完成。
- 11/10 的 SDK 優化誤在舊目錄 `C:\Users\H\Documents\RealtimeFramework` 進行，該儲存庫的 commit 為 `c8863ca73feef09d5b4793c163dae87f45034229`，共 33 個檔案變更。
- 本次以 GameNetKit 現有內容為主，以舊目錄的前一個 commit 作共同基準，三方合併 SDK 修改，保留目前 GitHub Go module 路徑、Go 1.27.3／Unity 6000.6.8f1、啟動入口、工具位置與較新的文件。
- 新增的純 C# SDK runner 放在 `tests/scripts/Test-SdkNative.ps1`，沿用現有 Toolchain 解析，未引入舊版固定 Editor 路徑。
- 舊目錄的三項退休 sample 檢查修正（dependency checker、其反例、server-removal 入口）在新版已由移除退休 sample 的相依與檢查取代，保留現有新版實作。
- 保留目標原有 `.git`；其分支在本次作業前尚無 commit，因此本次提交同時納入目標原先未追蹤的專案原始碼與文件。

## 合併後驗證

驗證均從正式 GameNetKit 目錄執行，日誌位於 `artifacts/reconciliation-*.log`。

- `tests/scripts/Test-GoAndBrowser.ps1`：14 個 Go module 的 test／vet 與 Node 回歸通過。
- Python dependency guard 及 8 項反例測試通過。
- `tests/scripts/Test-SdkNative.ps1`：純 C# SDK 契約測試通過；編譯僅有 JSON fixture 欄位的未指派警告。

- `tests/scripts/Test-Transport.ps1`：實際 WS／HTTP3 與 22 項 JS transport／bridge 測試通過。
- `examples/rumblebound/scripts/Test-Battle.ps1`：實際 API／C# 客戶端戰鬥、身份／重連與 Go 匯出重播流程通過。
- `examples/rumblebound/scripts/Test-BattleRemoval.ps1`：隔離樹中實體移除十個選用 Unity 套件後，雙人與備援流程通過。

`tests/scripts/Test-GoModuleRemoval.ps1 -Module all`：五個 Go 選用 package 實體移除後，SDK／game／server 測試與 vet 通過，來源已還原。未將舊目錄的驗證視為本目錄通過的證據；本次未重建 Unity Editor／Player／WebGL。

## 本機封存

舊專案完整保存在 `artifacts/legacy-RealtimeFramework-20261110`，包含舊 Git 歷史與快取；合併前目標中被修改的檔案保存在 `artifacts/reconciliation-before-20261110`。這些本機封存依既有 `.gitignore` 排除，不加入 Git。舊路徑僅留下搬遷說明，避免再次誤改。

Git 檢查：本次合併檔案的 staged whitespace check 通過。整個初次提交的檢查會列出目標原有 Unity 生成的 .meta／ProjectSettings 空白，以及兩份既有文件的結尾空行；這些原有檔案保持不變。
