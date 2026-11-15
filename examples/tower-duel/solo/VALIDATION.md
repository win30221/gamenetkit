# 單人測試外掛驗證

日期：2026-11-15（台灣時間）。Unity 6000.6.8f1、Go 1.27.3、Windows。

## 已通過

- 原後端 `scripts/Test-Server.ps1`：`go test ./...`、`go vet ./...`。
- 獨立 companion：五個測試組及八張卡 × 兩席的決策子案例。真實原後端執行檔驗證一般建房／加入、費用確認、重複 action 去重、非法落點拒絕、電腦部署、靶場不出兵、破堡結算、同房重賽、原席位恢復和退出釋放房間。
- 重賽測試使用暫存 1 HP 塔／零塔傷害 fixture；未修改正式地圖或規則。
- 獨立 Windows 測試 Player 建置成功，零建置錯誤；原場景與原 Windows Player 不重建。
- Unity `.meta`／GUID 檢查：312 個檔案，零錯誤、零警告。
- PowerShell 腳本語法檢查通過；可攜啟動器透過 Windows PowerShell 執行，不需 PowerShell 7。

## 真正 Windows Player

以下兩次均使用獨立啟動器，自動啟動原後端、companion 和一個 Unity Player，完成後三個程序均已退出。

| 模式 | 後端確認出牌 | 成功恢復原局 | 已觀察己方部隊 | 已觀察電腦部隊 | 幀數 |
| --- | ---: | ---: | --- | --- | ---: |
| 電腦對戰 | 3 | 1 | 是 | 是 | 855 |
| 自由測試 | 3 | 1 | 是 | 否 | 851 |

兩次均由後端推進至 tick 426。報告：

- `artifacts/tower-duel/solo/20261115-064933-practice.json`
- `artifacts/tower-duel/solo/20261115-064933-sandbox.json`
- `artifacts/tower-duel/solo/backend-tests.jsonl`

隱藏視窗的這兩次執行沒有生成可用截圖；這是數據與實際程序驗證，沒有宣稱完成可見視窗的人工拖牌驗收。

## 原遊戲隔離

`solo/Build.ps1 -VerifyIsolation` 另行建置正常 Player，不提供 `TOWER_DUEL_SOLO`。建置成功，實際 `Assembly-CSharp.dll` 包含 `TowerDuelClient`，不包含 `TowerDuelSoloPlugin`。

結果為 `artifacts/tower-duel/solo-isolation/isolation-result.json`：

```json
{"passed":true,"normalPlayerIncludesSolo":false}
```

原本的 client、session、HUD、UXML、server、協定與 package manifest 都未加入外掛引用。外掛來源只有 `solo/` 與 `client/Assets/TowerDuelSolo/`（含同層 `.meta`），刪除方式見 README。

## 已知日誌限制

自動結束 Player 時，現有 `DuelPresentation.Dispose()` → `Reset()` 仍記錄一次已銷毀物件的 `NullReferenceException`；發生在結束清理階段，兩次程序退出碼均為 0，對局與重連測試完成。這次保留原呈現清理程式，沒有把修正混入測試外掛。

本文件記錄 Windows 階段；後續已另行部署獨立單人 WebGL 站，見 [WEB_DEPLOYMENT.md](WEB_DEPLOYMENT.md)。原多人 WebGL 網站維持原版本。
