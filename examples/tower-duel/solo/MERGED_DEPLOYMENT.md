# 同一網站、可快速移除的單人模式

入口：[雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)。同一頁選擇「單人練習」、「自由測試」或「雙人對戰」。雙人表單可返回模式選單，離房也回到選單。舊單人網址以 HTTP 308 轉到主網址，保留路徑和查詢參數。

## 服務與隔離

| 路徑／項目 | 負責者 |
| --- | --- |
| `/api/*`、`/ws`、`/healthz` | 原本 `tower-duel.service`，loopback 18110 |
| `/solo/*` | 可移除的 `tower-duel-modes.service`，loopback 18112 |
| 網頁與模式選單 | `current/modes-public` 的 WebGL |
| 純雙人版備用網頁 | 同一 release 的 `current/public` |
| 共用版本目錄 | `/opt/rumblebound/tower-duel/releases/<release>` |

三種模式共用同一個 Go 對戰後端和規則。外掛僅透過既有協定加入電腦玩家；主程式沒有引用外掛。正式 WebSocket 直接連原對戰服務，無須經過單人服務。舊 `tower-duel-solo-api`／`tower-duel-solo-web` 已停止並停用，保留舊版本產物供還原。

控制 API 保留同來源驗證、HttpOnly／Secure／SameSite Strict cookie 與房間所有權檢查。共用 Caddy 主設定和另一款 Rumblebound 遊戲服務不需修改。

2026-11-15（台灣時間）首次合併版本為 `20261115T111620Z-8cde69a2`，後續 `20261115T122552Z-87b0ae67` 加入 [冰霜腳下中心判定](../FROST_RANGE_FIX.md)；戰鬥特效升級見 [素材重用與驗證](../art/COMBAT_FX.md)。協定 3、規則 `eight-cards-3`。六種兵的部署時間均為 1000 ms，包含較慢移速、攻擊週期與正常時間固定回能。Unity 數值表使用毫秒，共用規則 SHA-256 為 `6fc638d8b0056df77090abcc46a7d2eaba13d7b58ff2f946db5a017ef2590dcb`。

部署前後共享 Caddy 主設定 SHA-256 均為 `111ecb53796c432c6eccc4019042cddfd024b49109289fef7427a010fb9a21f7`，Caddy 容器啟動時間未變，Rumblebound API 健康。核心與模式服務均 active，舊單人兩個服務均 disabled。公開設定與 308 轉址證據見 `artifacts/tower-duel/merged/public-config.json`，遠端服務檢查見 `production-after.txt`。

## 建置與更新

從儲存庫根目錄執行，正常 Player 不編入外掛；模式版使用 `TOWER_DUEL_SOLO` 編譯旗標：

```powershell
./examples/tower-duel/scripts/Build.ps1 -Target WebGL
./examples/tower-duel/solo/Build.ps1 -Target WebGL
./examples/tower-duel/solo/Deploy-Merged.ps1 -IdentityFile C:/Users/H/.ssh/id_rsa
```

可對兩個建置指令傳入 `-UnityCli <unity.exe>`。本機本次使用 `.local/unity-cli-pacing/unity.exe` 1.0.0。`-PackageOnly` 只產生 `.local/deploy/tower-duel-merged/<release>/merged.tar.gz`。

合併版更新使用 `Deploy-Merged.ps1`，同時攜帶核心服務、模式服務、兩份 WebGL、地圖與規則。它核對建置版本與 SHA-256，檢查兩個服務後再重載路由；安裝失敗會嘗試還原舊版本、服務與路由。原本 `scripts/Deploy.ps1` 留給純雙人站流程使用。

## 快速移除單人／自由測試

不用重新建置，從本機執行：

```powershell
./examples/tower-duel/solo/Disable-Modes.ps1 -IdentityFile C:/Users/H/.ssh/id_rsa
```

或在主機執行：

```sh
sh /opt/rumblebound/tower-duel/current/disable-modes.sh
```

腳本驗證純雙人版產物，切換同網址到 `public`，驗證／重載 Caddy，再停用模式服務。原對戰服務不重啟，並核對 PID 不變。切換失敗會還原路由和模式服務；重複執行亦可。瀏覽器重新整理後取得純雙人版；已開啟的舊頁面仍保留原先載入的程式。

這個命令停用可選模式，保留 release 供還原。若要恢復三模式，重新執行合併部署。原始碼可再刪除 `solo/`、`client/Assets/TowerDuelSolo/` 及 `TowerDuelSolo.meta`，核心仍按原流程建置。舊 `Remove-Deployment.ps1` 只處理歷史獨立站，不能用來停用合併站的模式服務。

## 驗證方式

```powershell
node examples/tower-duel/solo/Test-Modes.cjs https://tower-duel.172.233.67.181.sslip.io artifacts/tower-duel/merged/public-menu
node examples/tower-duel/solo/Test-Web.cjs https://tower-duel.172.233.67.181.sslip.io artifacts/tower-duel/merged/public-solo
node examples/tower-duel/scripts/Test-Web.cjs https://tower-duel.172.233.67.181.sslip.io artifacts/tower-duel/merged/public-multiplayer
```

`Test-Modes` 以真正滑鼠／觸控選擇雙人、返回選單、建立並退出好友房，再進入自由測試。一般雙人測試使用 `?mode=multiplayer` 開啟原表單。`test-disable-modes.sh` 在獨立 fixture 驗證快速移除、重複執行、Caddy／健康檢查失敗還原，並檢查對戰執行檔、服務與共用代理未變；不對正式站切換模式。

本次本機與正式站均已通過 `Test-Modes`、`solo/Test-Web` 及完整雙人 `scripts/Test-Web`。正式站涵蓋桌面／手機選單、好友房退出後切換自由測試、真實電腦出兵、部署等待、所有權、重連／重整、六種部隊、兩種法術、七種畫面尺寸與重賽；各端 runtime errors 為空。結果放在 `artifacts/tower-duel/merged/public-{menu,solo,multiplayer}/result.json`。本機等待房間退出回歸、Go companion 測試與 vet 也通過；Unity 資產檢查 322 個檔案，零錯誤、零警告。
