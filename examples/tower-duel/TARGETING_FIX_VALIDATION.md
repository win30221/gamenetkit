# 塔與附近敵軍的索敵修正

2026-11-15（台灣時間）。共用 Go 後端已改為預設推塔，偵測到敵軍時依碰撞邊緣距離選擇最近目標，接近至攻擊距離後才開始前搖。

本文件保留該次版本的規則與驗收記錄。後續依使用者要求新增「已鎖定敵軍離開偵測範圍即放棄追擊」，現行行為與部署驗證見 [TARGET_LEASH_VALIDATION.md](TARGET_LEASH_VALIDATION.md)。

## 規則

1. 平時沿出兵決定的橋前往同路前塔；前塔倒下後前往主堡。已在攻擊距離內的敵方建築也可成為目標。
2. 敵軍仍須在卡牌的 `aggro` 半徑內，半徑沿用中心距離；近戰隔河不能越過非橋區索敵。
3. 將合格敵軍、原建築目標及更近的敵方建築一起比較「中心距離 − 自身半徑 − 目標半徑」，選擇碰撞邊緣最近者。距離相同優先建築，敵軍同距離則依較小 ID 穩定選擇。
4. 追近期間每 tick 重新判斷；開始攻擊前搖後沿用原本的存活目標鎖定，直到目標死亡才重新選擇。攻城兵仍只攻建築。

因此尚未開始攻擊的兵，遇到塔後較遠的防守兵時會先打更近的塔；塔前較近的敵軍則會被接近並攻擊。這次沒有加入完整路徑成本或遠程視線遮擋判定，已鎖定的活敵軍仍沿用原本追擊規則。

實作集中於 `server/targeting.go`，從 `simulation.go` 抽出原本的 `target()`。移動、碰撞、攻擊前搖、傷害與快照協定沿用現有後端。單人外掛與多人使用同一份遊戲程式碼，外掛沒有士兵 AI 分支。

## 本機驗證

- 修正前，新增的碰撞邊緣比較與跨路近塔案例會失敗；記錄為 `artifacts/tower-duel/targeting-fix/expected-failure-before.txt`。
- 修正後 `go test -count=1 -json ./...` 的 52 個頂層測試組與 `go vet ./...` 通過。
- 六種士兵 × 兩席 × 左右路 × 五種情境，共 120 個案例：塔後兵、塔前兵、警戒範圍外、無守兵、已開始打塔後才出守兵。包含真實部署合法性、實際移動、首擊目標與塔血量，未關閉塔傷害。
- 額外驗證碰撞邊緣距離、同距離順序、警戒邊界、隔河資格、先追近再出手、首次出手前改目標與跨路戰鬥後的近塔選擇。既有鎖定、過橋與攻擊前搖測試繼續通過。
- 單人 companion 九個測試組與 `go vet` 通過，包含實際 Go 後端的出牌、扣費、恢復、重賽、代理與房間隔離。
- 本機真正 WebSocket 五場通過：雙方左右路塔後守兵四場，塔前守兵一場。只使用正常建房、加入、準備、部署與離開訊息，沒有注入測試快照或改動卡牌數值。

同一台 Windows 電腦、80 兵／300 tick 的既有 `BenchmarkCrowdedNavigation` 單次量測：修正前最慢 tick 27.70 ms、總模擬 100.86 ms；修正後最慢 tick 25.32 ms、總模擬 113.18 ms。兩次最慢 tick 均在 30 Hz 的 33.33 ms 內，未加入每個候選目標各跑一次 A* 的開銷。這是本機短跑量測。

## 線上版本

| 站點 | 版本 | 遊戲服務 |
| --- | --- | --- |
| [多人](https://tower-duel.172.233.67.181.sslip.io/) | `20261115T082626Z-bededd8d` | `tower-duel.service`，18110 |
| [單人](https://tower-duel-solo.172.233.67.181.sslip.io/) | `20261115T082707Z-2a57d698` | `tower-duel-solo-api.service`，18111 |

單人網站、控制 API 與電腦玩家仍由自己的 `tower-duel-solo-web.service`（18112）提供，保留原有獨立移除方式。

部署後已直接核對兩個執行中程序的 `/proc/<pid>/exe` 與部署檔案，遊戲執行檔 SHA-256 相同：

```text
a8160c1af0262d73c675467932f3e11b1d1107d0c9a872c9edeef957dd6f65bd
```

兩站各九個原 WebGL／transport／地圖／規則檔案的雜湊與部署前一致；這次沒有重建 Unity。兩個遊戲服務健康、程序分離；單人安裝期間保留新多人 PID `3160294`，共享 Caddy 容器沒有重啟。

## 公開站驗收

兩個 HTTPS/WSS 站各以正常雙席協定完成五場驗收：雙方左右路塔後守兵四場，以及塔前守兵一場。四種塔後案例都觀察到守兵已進入 `aggro`、攻擊方尚未鎖定時選塔，隨後首擊命中塔並使塔由 380 降至 348 HP，沒有繞到塔側。塔前案例則接近並首擊較近的弓箭手，塔保持 380 HP。

多人第一次公開驗收在第二場準備階段逾時，該執行與單人部署期間的代理重載時間重疊，未取得斷線事件證據。部署結束後，測試補上斷線即時診斷與失敗記錄，重新執行五場全部通過；沒有因此修改戰鬥程式或再次部署。

單人 WebGL 另以 Chrome `157.0.8103.78` 通過實際滑鼠練習與手機觸控自由測試，包含後端接受部署、電腦出牌、斷線恢復、重新整理恢復、房間所有權與離開清理，兩個模式的執行期錯誤均為零。手機為 Chrome 觸控模擬。測試房間 `EZUSCD` 與 `RRB7AK` 均已離開清理。

重跑公開索敵驗證：

```powershell
node examples/tower-duel/scripts/Test-Targeting.cjs https://tower-duel.172.233.67.181.sslip.io artifacts/tower-duel/targeting-fix/public-multiplayer-ws-final
node examples/tower-duel/scripts/Test-Targeting.cjs https://tower-duel-solo.172.233.67.181.sslip.io artifacts/tower-duel/targeting-fix/public-solo-ws
```

## 證據

`artifacts/tower-duel/targeting-fix/`：

- `server-tests.jsonl`、`focused-tests.jsonl`、`benchmark-before.txt`、`benchmark-after.txt`。
- `local-ws/results.json`：本機正常協定測試。
- `public-multiplayer-ws-final/results.json`、`public-solo-ws/results.json`：兩個公開站各五場的目標、首擊、距離與逐幀追蹤。
- `public-solo-browser/result.json` 與同目錄截圖：單人 WebGL 的滑鼠、觸控及恢復驗證。
- `remote-before.json`、`remote-after.json`、`deployment-checks.json`：部署前後實際檔案、執行中程序與健康檢查。
- `multiplayer-deploy.txt`、`solo-deploy.txt`：部署版本與安裝記錄。

原問題調查與修正前的重現資料保留在 [TARGETING_INVESTIGATION.md](TARGETING_INVESTIGATION.md)。
