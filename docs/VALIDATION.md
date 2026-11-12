# GameNetKit 驗證

目前版本為 Go 1.27.3、Unity 6000.6.8f1。所有命令從 gamenetkit 根目錄執行，使用 PowerShell 7；另需 Node.js 22 以上、Python 3，以及有效 Unity 授權與 Windows／WebGL 模組。工具版本與自訂路徑見 [MIGRATION](MIGRATION.md)。

## 原始碼與 Go

```powershell
python ./tests/scripts/Check-Dependencies.py
python -B -m unittest discover -s ./tests/dependencies -v
./tests/scripts/Test-GoAndBrowser.ps1
```

`Test-GoAndBrowser.ps1` 執行 14 個 Go module 的 test／vet，以及 browser transport、共用網頁與 Shared World 的 Node 測試。依賴檢查覆蓋 SDK／遊戲邊界、相對套件來源與現行範例。

## Unity 與整合

```powershell
./tests/scripts/Test-UnitySdk.ps1
./examples/Build-Unity.ps1 -Project Minimal -Target Windows
./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Windows
./examples/tests/Test-UnityCompilation.ps1
./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Web
./examples/rumblebound/scripts/Test-Battle.ps1
./examples/rumblebound/scripts/Test-Browser.ps1 -WebTransport
./tests/scripts/Test-Transport.ps1
./examples/rumblebound/scripts/Test-Replay.ps1
```

參考組件編譯、Editor 匯入、Player 建置與實際執行分別記錄。CLI／授權／網路失敗代表未取得測試結論；舊產物不作為新版建置證據。驗證會建立本機 artifacts、cache 及 Player。

## 2026-11-06 遷移驗證

| 項目 | 結果 | 證據／範圍 |
| --- | --- | --- |
| Go 1.27.3 | 通過 | `GOTOOLCHAIN=local`；14/14 module 的 test／vet，見 `artifacts/migration/go1.27.3-validation.json` |
| Node 測試 | 通過 | Transport 22/22、Shared World 4/4 |
| Python 依賴守門 | 通過 | 原始碼依賴檢查與 8/8 邊界反例 |
| Go 相依 | 通過 | 13 份 go.sum 與遷移前相同，第三方版本未擴大升級 |
| Replay CLI | 通過 | 新目錄錄製 92 ticks，獨立程序重建、匯出與拒絕覆寫；修改輸入被定位並拒絕 |
| PowerShell 工具 | 通過 | 47 支語法與腳本相對路徑檢查、13 項啟動清單；清理 WhatIf 零刪除 |
| Unity 來源 | 通過 | 12 個專案版本／本機套件引用有效；601 份原有 meta／GUID 完整 |
| Unity SDK | 通過 | 真正 6000.6.8f1 Editor 執行四組 SDK 測試；`artifacts/sdk-tests.log` |
| Unity 專案匯入 | 通過 | 12/12：九個教學匯入報告加 SDK、Minimal 與完整遊戲實際匯入；`artifacts/migration/unity-imports.json` |
| 原生／WebGL 參考編譯 | 通過 | 七課與完整遊戲 application／Unity／Editor 組件，包含 WebGL 傳輸分支 |
| Windows 建置 | 通過 | Minimal 與 RumbleboundExample；`artifacts/minimal-Windows.log`、`artifacts/example-Windows.log` |
| WebGL 建置 | 通過 | 實際產生 data／framework.js／loader.js／wasm；`artifacts/example-Web.log` |
| 原生遊戲與傳輸 | 通過 | 真正 Go／C# 流程、WS 與 HTTP/3 pinned TLS、串流／datagram／隔離與恢復 |
| 雙 Windows Player | 通過 | WS／WT 各兩個真正 Player：房間、戰鬥、重連、練習、錄製與播放；六份 journal 由獨立 Go 程序驗證 |
| 瀏覽器來源／API | 通過 | 22 項正式 JS 測試、真實 API 與建置資源／缺建置診斷；DOM／Canvas 使用替身，不代表人工瀏覽器畫面或手機驗收 |

遷移前結果另存 [歷史驗證](archive/VALIDATION-2026-11-03.md)。

## 工具入口整理（2026-11-06）

已移除重複的 `Start-Game.ps1`、`tools/Start-Demo.ps1`；十個 Rumblebound 專用工具移至 `examples/rumblebound/scripts`，部署範本移至 `examples/rumblebound/deploy`。入口與保留理由見 [工具索引](../tools/README.md)。

本次驗證：47 支 PowerShell 語法及靜態腳本相對引用、13 項啟動選單、不適用參數拒絕、Python 依賴守門及 8 個反例、搬移後重播錄製／離線重建／篡改拒絕、播放匯出、core-only 隔離來源的真正 C#／Go 流程，以及 `Deploy.ps1 -PackageOnly` 均通過。部署包僅在本機產生，未連線 VM。程式與素材的 601 份原有 meta／GUID 不變。
## 指令依用途分區（2026-11-06）

啟動與 Unity 建置放在 `examples`；SDK 驗證放在 `tests/scripts`；單一範例的測試跟隨範例，跨範例驗證放在 `examples/tests`；共用工具鏈放在 `tools/internal`。`tools` 的手動入口僅保留 native helper 建置與工作區清理。`Start.cmd` 直接呼叫 `examples/Run.ps1`，已移除 `Start.ps1`。

所有 47 支 PowerShell 腳本都可用 `Get-Help <路徑> -Full` 查看用途、前提、產物與範例；完整索引見 [指令導覽](COMMANDS.md)。

本次搬移後重新通過：47 支語法／靜態相對路徑與說明、13 項選單、Go／Unity 工具鏈解析、清理 WhatIf 零刪除、Python 邊界檢查與 8 個反例、14 個 Go 模組 test／vet、Transport 22 與 Shared World 4 項 Node 測試、八個 C#／四個 Go 終端主題、Minimal 真實 WS 啟動及現有 Windows Player、真實 WS／HTTP3 傳輸、真正 Unity Editor 的四組 SDK 測試，以及七課／完整遊戲的原生與 WebGL 參考組件編譯。601 份原有 meta／GUID 完整。

遊戲網頁的缺建置提示同步改為 `examples/Build-Unity.ps1`，已用實際本機主機驗證正常資源、缺建置 503 與診斷入口；相關 JS 22 項測試及真實 C#／Go 流程通過。部署腳本另以 `-PackageOnly` 驗證，只在本機產生套件。

本次未重新建置全部 Unity Players，也未重跑會暫移正式來源的移除測試或遠端部署；本節的參考編譯、現有 Player 與網頁替身測試不代表全部 Player、人工瀏覽器畫面或實體手機驗收。

## 逐檔用途排查（2026-11-06）

完整結果與處置見 [FILE_AUDIT](FILE_AUDIT.md)。本次移除 7 份已確認的過期／重複／生成檔，搬移 6 份產生器、範例測試主機與歷史文件。所有 Unity 素材及 601 份原有 meta／GUID 保留。

| 驗證 | 結果與範圍 |
| --- | --- |
| Codec 產生器 | 新位置實際執行，輸出與既有 codec.json 的 SHA-256 相同。 |
| Go 模組 | 14 個模組 test／vet 通過；go list 解析 34 個 package，逐檔確認來源／測試歸屬。 |
| 條件與入口 | rf_no_replay 的 sim 測試通過；47 支 PowerShell 語法／相對路徑／說明與 13 項選單通過。 |
| Node 日常回歸 | 30 項通過：transport 22、共用手機網頁 4、Shared World 4。 |
| 終端教學 | 現行八個 C#、四個 Go 主題均通過，退休的舊入口與重複課程不再保留。 |
| 完整遊戲 | Test-Battle.ps1 的真正 C#／Go 房間、對戰、恢復、診斷與重播通過；API 由 cmd/battle 唯一入口建置。 |
| 搬移的 C# 主機 | Minimal Start/Update 真實 WS 與 Unity 替身測試通過；Rumblebound 的 core-only 隔離來源 C#／Go 流程通過。 |
| Shared World | 真實 JS／Go 與雙 C# ClientWebSocket 流程通過；保留的 NativeClient.csproj 實際建置為零錯誤、零警告。 |
| Unity SDK | 真正 Editor 的 SDK、NetworkTools、SevenModules、Playback 四組測試通過。 |
| Python 邊界 | 依賴守門與八個反例通過。 |
| 靜態資源與文件 | JSON、本機 UPM 路徑、自有 asmdef、meta 配對、GUID、相對 JS 引用與 Markdown 本機連結均有效。 |

本次只整理檔案歸屬、過期內容、測試入口與文件，未修改遊戲規則或刪除呈現資源；未重新建置／人工驗收全部 Unity Players 或執行遠端部署。

## SDK 優化（2026-11-10）

七項優化、基準比較及測試範圍見 [SDK 優化紀錄](SDK_OPTIMIZATION_2026-11-10.md)。`./tests/scripts/Test-SdkNative.ps1` 可在無 Editor 授權時執行相同的純 C# SDK 契約測例；它不代替 Unity Player 或 WebGL 驗收。

## 2026-11-11 Rumblebound 手機輸入與 HUD

- Windows 與 WebGL 已重新建置成功；WebGL 成品包含 `OnBattleRoomCodeInput` 原生輸入橋接。新增文字與方向箭頭通過字型覆蓋檢查，字型來源 SHA-256 維持原授權 manifest。
- `node --test examples/rumblebound/tests/battle/room-input.mjs examples/rumblebound/tests/battle/replay-picker.mjs examples/rumblebound/tests/battle/network-settings.mjs`：26 項通過。其中房間輸入覆蓋貼上後正規化、空白、取消、重複提交、替換請求、Escape 與 Canvas 全螢幕退出；使用模擬 DOM，並非手機鍵盤實測。
- 真正 Windows Player 的戰鬥驗證通過；桌面 HUD 12 張、觸控 HUD 13 張實際 GPU/UI 畫面通過中央交戰區、控制區尺寸、控制區互斥與提示不遮擋按鈕檢查。觸控尺寸為 568×320、640×360、844×390、989×446、1280×720；三角色瞄準、普攻與技能也有畫面。證據在本機 `artifacts/hud-review/`、`artifacts/hud-review-desktop.log`、`artifacts/hud-review-touch.log`。
- 多指 Player 測試通過點按自動瞄準、拖曳技能、移動／放開／衝刺、施法後不抬左手續走、pointer capture 遺失與開選單重設。測試在不同技能之間轉向，避免固定北向路線撞上大型地圖北側牆；同一次施法前後仍保留同一手指與方向。證據：`artifacts/touch-player.log`、`artifacts/combat-player.log`。
- 本輪瀏覽器實際點擊驗收受 Work settings 的 Local browser use 停用阻擋；iPhone／Android 軟鍵盤、貼上、瀏海安全區與旋轉仍需裝置端確認。上述原生 Player 渲染及模擬 DOM 測試不能取代這些項目。
- HUD 變更減少比分卡片重建及暫存集合配置；本輪沒有執行效能 Profiler，不宣稱 FPS 提升幅度。
