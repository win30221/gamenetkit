# 專案檔案用途與清理

2026-11-06：逐檔盤點 1827 份非快取檔案。完整清單見 [逐檔用途與處置 CSV](audit/files-2026-11-06.csv)，可依路徑、分類或處置篩選。日常執行入口見 [指令導覽](COMMANDS.md)。

範圍包含 Go／C# 原始碼、測試、腳本、文件、Unity 設定、套件與素材。排除 .git、.local、.cache、Library、Temp、Logs、Builds、UserSettings、artifacts、bin／obj／out 等生成或個人目錄；另移除誤留於 client/Data 的兩份已忽略 Burst 輸出。CSV 本身與本頁也列入索引。

## 已處理

| 原位置 | 處置與原因 |
| --- | --- |
| server/cmd/codecfixtures/main.go | 移至 tests/fixtures/generate-codec/main.go；是跨語言測試資料產生器。新位置的輸出與原 fixture 完全一致。 |
| examples/modules/client/Program.cs | 移除已退休的合併課程入口；現行 Run.ps1 分別編譯八個課程 Main。 |
| examples/modules/client/PredictionLesson.cs、DiagnosticsLesson.cs | 移除僅被舊入口引用的前版；現行課程為 PredictionCorrectionLesson、DiagnosticsSimulationLesson。 |
| examples/modules/server/history_lagcomp.go | 移除未被任何入口呼叫的平行版本；main.go 與教學文件使用 history.go/historyLesson。 |
| examples/rumblebound/server/main.go | 移除重複的 launcher 別名；所有支援腳本使用 server/cmd/battle/main.go，文件同步。 |
| examples/rumblebound/client/Data/Plugins/lib_burst_generated.cpp、.wasm | 移除兩份 Unity Burst 生成物；不在 Assets，也已列入 .gitignore。 |
| tests/battle-removal/Program.cs | 移至 examples/rumblebound/tests/removal，跟隨對應範例。 |
| tests/minimal-startup/Program.cs | 移至 examples/minimal-unity/tests/startup，跟隨對應範例。 |
| 三份舊工作紀錄 | 集中至 docs/archive/worklogs；保留原脈絡，更新本機文件連結。 |
| examples/browser-common/mobile-view.test.mjs | 保留並加入 Test-GoAndBrowser.ps1，補上共用網頁的四項日常回歸。 |

合計移除 7 份檔案、搬移 6 份檔案；沒有刪除 SDK 公開契約、正式測試、Unity 素材或授權。

## 保留理由與檢查依據

- Go：用真正 go list 解析 14 個模組／34 個 package；來源與測試逐檔歸屬，移到 tests 的 generator 以明確單檔指令執行。replay_unavailable_test.go 由 rf_no_replay tag 啟用，屬移除契約測試。
- C#：依 asmdef、Editor 目錄、腳本明確來源清單及測試入口分類。RemovalProgram.Main 是測試入口；PreserveAttribute 是 Minimal 測試環境的 Unity API 替身。BattleBootstrap 由 Unity 的 RuntimeInitializeOnLoadMethod 自動呼叫；GroundCastPresentation.cs 包含實際使用的呈現器與公開 GroundTargeting 輔助 API，未因個別類別缺少直接呼叫而刪除整份來源。
- Unity：601 份 meta 均有對應檔案或資料夾，沒有重複 GUID、遺失 sidecar 或無法解析的資源 GUID；Unity 內建及已匯入 registry 套件的 GUID 另行識別。所有本機 UPM 引用與自有 asmdef 參考均有效。
- 素材：Resources 動態載入、prefab／材質／動畫依賴與來源素材用途一併考慮。缺少直接文字引用不作為刪除依據；來源、授權與 GUID 保留。
- 重複內容：38 組相同內容已分類；獨立 Unity 專案的標準設定與各教學自己的回歸案例保留。教學避免依賴其他課程，因此相同測試內容有各自的編譯與驗證範圍。
- 文件與腳本：本機 Markdown 連結、JSON、相對 JavaScript 引用、PowerShell 語法與靜態腳本路徑均檢查；歷史文件與原素材來源註記不作為現在的操作指令。

本次涵蓋全部索引檔案的用途、結構與靜態引用，並針對可疑項目檢查實際入口及測試；不等於逐一人工驗收所有遊戲畫面。執行結果與範圍記錄於 [VALIDATION](VALIDATION.md)。
