# 可移除的單人測試外掛

網頁入口：[雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)。同一頁選「單人練習」、「自由測試」或「雙人對戰」；舊單人網址自動轉到這裡。單人模式只需一個瀏覽器視窗，不需房間碼，自由測試的對手不出兵，塔仍正常防守。

三種模式共用**原版 Go 遊戲後端**；電腦玩家程式仍是可移除的獨立服務。出牌、扣能量、手牌循環、移動、碰撞、攻擊前搖、傷害與勝負全部由後端計算。建置、上線和一個命令切回純雙人版的方式見 [合併站部署](MERGED_DEPLOYMENT.md)。

節奏調整版 `eight-cards-3` 的練習電腦會從前塔後方（己方深度 10.5）集結進攻，
讓玩家先看見部隊再判斷應對；敵兵進入河道附近或己方半場時，仍會在深度 3.5 即時部署防守。
電腦沿用原本的牌費與回能規則。詳見 [節奏調整與驗證](../PACING_VALIDATION.md)。
玩家與電腦的部隊出場後皆先等待 1000 ms 部署，各兵種時間獨立設定；詳見 [部署時間](../DEPLOYMENT_TIME_VALIDATION.md)。

Windows 本機測試入口也保留，可離線使用：

- 雙擊 [Start-Solo.cmd](Start-Solo.cmd)：對戰電腦。
- 雙擊 [Start-Sandbox.cmd](Start-Sandbox.cmd)：對手不出兵，塔仍正常防守，方便測試出兵和動畫。
- 按「離開」可回大廳，使用外掛加入的按鈕開始另一局；結算後按「再練一場」。
- 關閉遊戲後，啟動器會停止自己開啟的後端和電腦玩家程式。

## 網頁建置與部署

在儲存庫根目錄執行：

```powershell
./examples/tower-duel/solo/Build.ps1 -Target WebGL
./examples/tower-duel/solo/Run-Web.ps1
```

本機網頁為 `http://127.0.0.1:18113/`，WebGL 產物在 `examples/tower-duel/Builds/SoloWeb/WebGL`。原本 `client/Builds/WebGL` 不會覆蓋。

部署到現有主機：

```powershell
./examples/tower-duel/solo/Deploy-Merged.ps1 -IdentityFile C:/Users/H/.ssh/id_rsa
```

先依主專案流程建置 `client/Builds/WebGL` 作為純雙人備用版。`-PackageOnly` 只產生 `.local/deploy/tower-duel-merged/<release>/merged.tar.gz`。部署有檔案雜湊驗證、健康檢查及失敗還原：

| 項目 | 位置 |
| --- | --- |
| 共用遊戲後端 | `tower-duel.service`，loopback 18110 |
| 控制 API 與電腦 | `tower-duel-modes.service`，loopback 18112 |
| 合併版本與 current | `/opt/rumblebound/tower-duel/` |
| 舊單人站代理檔 | 轉址到主網站 |

代理檔使用既有 Caddy 的 import，不修改共用代理主檔。公開測試房間的控制 API 使用相同來源檢查與 HttpOnly／Secure 工作階段 cookie，不同瀏覽器不能關閉別人的測試房間。重新整理及短暫斷線沿用原遊戲的恢復協定。舊獨立部署紀錄保留於 [WEB_DEPLOYMENT.md](WEB_DEPLOYMENT.md)。

## Windows 本機建置

在儲存庫根目錄執行（使用原專案釘選的 Unity／Go）：

```powershell
./examples/tower-duel/solo/Build.ps1
```

產物獨立放在 `examples/tower-duel/Builds/Solo`，可整個資料夾搬到另一台 Windows 電腦使用；已建置的版本不需另外安裝 Go、Unity 或 PowerShell 7。建置不重建場景、不更新原本 Windows Player，也不修改原來的建置腳本。

## 如何隔離

```text
一個 Unity 視窗 ── 原有 join / ready / deploy / rematch ─┐
                                                      ├─ 原本 Go 後端
獨立電腦程式 ──── 原有 create / ready / deploy / rematch ─┘
```

`solo/backend` 是獨立 Go module，使用原協定，以一般玩家身份佔第一席；你的 Unity 視窗加入第二席。電腦也必須遵守實際手牌、費用與落點驗證。靶場只停用電腦出牌，保留原戰鬥模擬。

`client/Assets/TowerDuelSolo` 是唯一 Unity 接點。只有獨立測試建置會透過 `BuildPlayerOptions.extraScriptingDefines` 帶入 `TOWER_DUEL_SOLO`；正常 Player 建置不編入外掛。Windows 測試包還必須在啟動時明確收到 `-td-solo-api`；WebGL 測試包使用自己的網站來源，透過外掛內的 `.jslib` 呼叫控制 API。原本 `TowerDuelClient`、session、HUD、UXML、伺服器、協定及套件 manifest 都不引用它。

Windows 控制 API 與遊戲後端使用 loopback 動態埠。網頁版只在 loopback 暴露內部服務，由主網站 HTTPS 提供入口；遊戲 WebSocket 直接連共用對戰後端。

## 移除

先將線上網站切回已備妥的純雙人版，不需重建或重啟對戰服務：

```powershell
./examples/tower-duel/solo/Disable-Modes.ps1 -IdentityFile C:/Users/H/.ssh/id_rsa
```

這會停用 `tower-duel-modes.service` 並切換 WebGL，保留版本產物供還原。接著關閉本機單人遊戲，刪除以下外掛來源即可：

1. `examples/tower-duel/solo/`
2. `examples/tower-duel/client/Assets/TowerDuelSolo/` 及同層 `TowerDuelSolo.meta`

`examples/tower-duel/Builds/Solo/` 和 `Builds/SoloWeb/` 是可一併刪除的獨立產物。既有遊戲來源、場景、後端與協定不需還原或補洞。移除來源後，日後依原有流程建置的 Player 就不包含外掛；已建置的測試包仍帶有編譯後外掛，需丟棄或重新建置。

## 驗證

```powershell
./examples/tower-duel/solo/Test.ps1
./examples/tower-duel/solo/Test.ps1 -Native
./examples/tower-duel/solo/Build.ps1 -VerifyIsolation
node examples/tower-duel/solo/Test-Web.cjs http://127.0.0.1:18113 artifacts/tower-duel/solo-web/local
node examples/tower-duel/solo/Test-Web.cjs https://tower-duel.172.233.67.181.sslip.io artifacts/tower-duel/merged/public-solo
node examples/tower-duel/solo/Test-Modes.cjs https://tower-duel.172.233.67.181.sslip.io artifacts/tower-duel/merged/public-menu
```

第一個命令建置原後端並驗證真實 WebSocket 建房、單人加入、費用與動作去重、非法部署、電腦出兵、靶場、重賽、原席位重連和退出清理。重賽使用暫存的 1 HP 塔／零塔傷害 fixture 加速結算；正式規則檔不會改動。

`-Native` 再啟動兩次真正的單視窗測試包，分別驗證電腦／靶場、三次後端確認的出牌和一次重連，完成後自動關閉遊戲與兩個支援程序。數據報告在 `artifacts/tower-duel/solo`；隱藏視窗不保證能保存畫面。一般試玩不啟用自動出牌測試。

`-VerifyIsolation` 另外建置不帶外掛編譯旗標的正常 Player，檢查實際 `Assembly-CSharp.dll` 含主遊戲而不含外掛型別，結果放在 `artifacts/tower-duel/solo-isolation/isolation-result.json`。

Windows 實測結果與既有退出清理日誌限制見 [VALIDATION.md](VALIDATION.md)；網頁部署與公開站驗收見 [WEB_DEPLOYMENT.md](WEB_DEPLOYMENT.md)。
