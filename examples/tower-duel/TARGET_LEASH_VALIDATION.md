# 敵軍出界後解除追擊鎖定

2026-11-15（台灣時間）。依使用者要求，已鎖定敵軍離開偵測範圍後，士兵會放棄追擊、重新索敵；沒有範圍內敵軍時恢復推塔。

## 現行規則

- `server/targeting.go` 在保留士兵對敵軍的鎖定前，重新檢查雙方中心距離是否小於或等於該兵種的 `aggro`。
- 敵軍死亡或超出半徑時，同一個模擬 tick 清除鎖定，沿用既有的塔與範圍內敵軍碰撞邊緣距離比較，選擇最近目標。沒有合格敵軍則繼續前往建築目標。
- `simulation.go` 既有的換目標處理會取消未完成的前搖、歸零前搖進度；攻擊冷卻持續計時，新目標必須重新開始自己的前搖。已完成的傷害與命中事件保留。
- 敵軍仍在偵測範圍內時保持鎖定，即使有更近的敵軍到場。建築鎖定仍維持至建築被摧毀；攻城兵維持只攻建築。

共用戰鬥程式只有 `targeting.go` 的鎖定資格變動；前一版塔／敵軍距離排序、碰撞、尋路、卡牌數值與訊息協定沿用。單人外掛繼續使用自己的服務、網站與控制 API。

## 驗證

- 修正前的五種可攻擊士兵都會在敵軍剛超出半徑時繼續鎖定；新增邊界測試可重現，見 `expected-failure-before.txt`。
- `TestSoldierDropsEscapedTarget`：五種士兵 × 兩席 × 三個階段（即將命中的前搖、命中之後、追擊途中）× 兩個後續情境（附近有敵軍／只有範圍外敵軍），共 60 案例通過。
- 驗證改選範圍內最近敵軍、沒有合格敵軍時變更路徑目標並朝塔移動、前搖不轉移、不打中已出界敵軍、攻擊冷卻保留，以及快照同步清除舊鎖定。
- `TestLockedEnemyAggroBoundary`：五種士兵 × 內側、邊界、外側，共 15 案例通過；邊界內外以相鄰浮點數測試，邊界本身仍保持鎖定。
- 完整後端 54 個頂層測試組與 `go vet ./...` 通過，包含前一版的 120 個塔前／塔後索敵案例、原過橋路線、建築鎖定、攻擊前搖與真實 WebSocket 連線測試。
- 單人 companion 九個測試組與 `go vet` 通過，包含真實遊戲後端的出牌、電腦玩家、恢復、重賽、代理與工作階段隔離。

證據位於 `artifacts/tower-duel/target-leash/`：`targeting-before.go.txt`、`expected-failure-before.txt`、`server-tests.jsonl`、`solo-tests.jsonl`。新增回歸測試在 `server/target_leash_test.go`，原鎖定測試已更新為保留範圍內存活目標。

## 線上部署

| 站點 | 版本 | 遊戲服務 |
| --- | --- | --- |
| [多人](https://tower-duel.172.233.67.181.sslip.io/) | `20261115T084251Z-a85990c3` | `tower-duel.service`，18110 |
| [單人](https://tower-duel-solo.172.233.67.181.sslip.io/) | `20261115T084327Z-fb6f87f0` | `tower-duel-solo-api.service`，18111 |

已比對實際運行程序的 `/proc/<pid>/exe` 與發布檔案，兩個遊戲執行檔 SHA-256 相同：

```text
70ffab1a97eb9163eab3884f9e187c156b376bf931bc2175b8c8814c08c17d8f
```

兩個遊戲後端與單人網站健康檢查通過，遊戲程序各自獨立。兩站各九個原有 WebGL／transport／地圖／規則檔案的雜湊與部署前一致，未重建 Unity。單人安裝期間保留新多人 PID `3161127`；共享 Caddy 容器沒有重啟，單人外掛的獨立移除方式沿用。

部署記錄為 `multiplayer-deploy.txt`、`solo-deploy.txt`；執行中程序、原有資產與隔離檢查為 `remote-before.json`、`remote-after.json`、`deployment-checks.json`。

兩站部署全部完成後，各以真實公開 HTTPS/WSS 執行五場正常雙席對戰，兩席左右路的塔後守兵與塔前守兵回歸檢查全部通過。塔後案例仍首擊塔（380 → 348 HP），塔前案例首擊較近守兵（塔維持 380 HP），測試後正常離開房間。記錄為 `public-multiplayer-ws/results.json`、`public-solo-ws/results.json`。此公開檢查覆蓋部署與原索敵流程；出界解除鎖定、換目標與取消前搖由上述 75 個共用後端測試案例驗證。
