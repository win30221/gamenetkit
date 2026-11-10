# 文件檢查與使用入口修正

本次檢查於 2026-11-09 完成。範圍是 `rg --files -g '*.md'` 列出的文件，涵蓋根目錄、docs、examples、server、native、packages、tests 與 tools；忽略的快取、Library、建置產物不作現行教學來源。歷史文件與第三方素材說明保留原文，不改寫成現行操作指南。

## 已修正

| 問題 | 處理 |
| --- | --- |
| 首頁有大量索引，但第一次操作所需工具、結果及停止方式不集中 | 新增 Shared World 最短流程、兩視窗完成條件、工具需求與資料夾地圖 |
| SDK 資料夾缺乏直接接入說明 | 補 Go require／replace 與 Unity manifest／asmdef、路徑基準、應用責任 |
| docs、native、Unity SDK、Minimal 與 browser-common 缺入口 | 新增各自 README，說明可否直接啟動及下一步 |
| Minimal 留有舊 Build／Test 名稱和固定 Unity 路徑描述 | 改為實際 Build-Unity／Test-Player 名稱與 UnityEditor 覆寫方式 |
| Minimal 收包／送輸入說明未反映目前程式 | 補上首份有效快照前不送輸入、error 關線與資料驗證條件 |
| 教學指令綁定作者電腦路徑 | 改用根目錄相對命令，必要的絕對路徑明確標示替換位置 |
| Shared World／Interpolation 把舊授權阻擋描述為目前狀態 | 改連至具日期與範圍的 VALIDATION，保留不同驗證層級的區別 |
| Rumblebound 聲稱自動開頁、仍保留舊 sample | 改為手動開網址，說明現行 game 位置 |
| 本機 artifacts 被當成可攜文件連結 | 改為本機證據路徑說明，提供可隨倉庫取得的驗證摘要連結 |

## 本次驗證

- 全部 Markdown 本機檔案連結檢查：排除程式碼區塊、inline code 與外部網址後，目標路徑均存在。這不代表外部網站或所有標題錨點已驗證。
- 全部 PowerShell 腳本語法解析：零錯誤；未因此執行部署、清理或移除契約腳本。
- `./examples/Run.ps1 -List`：13 個入口正常列出；Go 解析為 1.27.3，.NET SDK 路徑解析成功。
- `./examples/shared-world/scripts/Test-SharedWorld.ps1 -NativeClient`：`RF_SHARED_WORLD PASS` 與 `RF_NATIVE_SESSION PASS`。正式 JS session 與兩個 C# session 使用真實本機 WebSocket，確認共用玩家清單、輸入歸屬、停止、離線、重連與舊 epoch 隔離。

本次僅修改文件，未重新建置全部 Unity Player，也未進行人工瀏覽器畫面、實體手機或遠端部署驗收。Shared World 測試使用 Node 與 C# 主控台，不能視為渲染驗收。各版本既有平台證據仍以 [VALIDATION](VALIDATION.md) 的日期與範圍為準。

目前工作樹原先即全部顯示為未追蹤；本次未提交、推送或刪除既有專案檔案。
