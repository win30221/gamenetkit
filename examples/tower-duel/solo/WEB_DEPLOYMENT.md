# 獨立單人網頁測試站

2026-11-15（台灣時間）已部署：[單人測試場](https://tower-duel-solo.172.233.67.181.sslip.io/)。目前版本 `20261115T084327Z-fb6f87f0`，協定 3、八張卡、規則 `eight-cards-1`。本次更新共用後端，已鎖定敵軍離開偵測範圍就放棄追擊並重新索敵；WebGL 與外掛介面沿用原產物，驗證見 [TARGET_LEASH_VALIDATION.md](../TARGET_LEASH_VALIDATION.md)。前一版塔／敵軍最近距離排序記錄見 [TARGETING_FIX_VALIDATION.md](../TARGETING_FIX_VALIDATION.md)。

提供「單人練習」和「自由測試」。WebGL 使用原場景與呈現，新增的控制介面和 JavaScript bridge 都在可移除外掛資料夾內。電腦以正常玩家身份建房、準備與出牌；真人視窗正常加入第二席，沒有前端戰鬥模擬或測試用傷害捷徑。

## 獨立部署

- 原版 Go 遊戲程式另啟為 `tower-duel-solo-api.service`，loopback 18111。
- 外掛網站、控制 API、WebSocket 代理及電腦玩家為 `tower-duel-solo-web.service`，loopback 18112。
- 版本放在 `/opt/rumblebound/tower-duel-solo/releases`，`current` 指向本次版本。
- 唯一新增代理設定為 `/opt/rumblebound/tower-duel/tower-duel-solo.caddy`，透過既有 import 載入。
- 網頁產物為 `Builds/SoloWeb/WebGL`，沒有覆蓋原多人 WebGL。
- 工作階段 cookie 為 HttpOnly／Secure／SameSite Strict；異來源不能建立或刪除房間，其他瀏覽器不能控制別人的房間。後端已清除的房間可重複清理。

首次單人站部署時，原多人站為 `20261115T024452Z-bf8955f5`，當時原後端 PID `3155730` 維持不變。該次部署前後比對以下 SHA-256 均一致：共享 Caddy 主設定、原多人站代理檔、原部署 manifest、原後端執行檔、原 WebGL Wasm。之後依使用者要求，兩站已更新共用戰鬥索敵與敵軍出界解鎖；多人目前為 `20261115T084251Z-a85990c3`，各自的遊戲服務仍分離。

證據：`artifacts/tower-duel/solo-web/production-before.json`、`production-after.json`、`production-isolation.json`。

## 已執行驗證

1. Unity 6000.6.8f1 WebGL release／IL2CPP 建置成功，零建置錯誤；沿用原 Web 設定，不重建場景。
2. Go 1.27.3 companion 的九個測試組與八張卡 × 兩席的決策子案例通過，`go vet` 通過。包含原後端完整協定、重賽、恢復、清理、同來源、cookie、跨使用者隔離、WebSocket 代理與 gzip／Wasm 標頭。
3. 本機 WebGL 與公開 HTTPS/WSS 站都執行真正 Chrome 瀏覽器測試；輸入使用滑鼠及手機觸控事件，沒有合成快照或替換伺服器回應。
4. `.meta`／GUID 檢查 314 個檔案，零錯誤、零警告；PowerShell 和部署 shell 語法檢查通過。

公開站驗收使用 Chrome 157.0.8103.78：

| 項目 | 結果 |
| --- | --- |
| 單視窗電腦對戰、正常第二席加入 | 通過 |
| 真實拖牌、後端接受／扣能量／手牌更新 | 通過 |
| 觀察電腦的後端部署 | 通過 |
| 關閉遊戲 WebSocket 後恢復同一局與手牌 | 通過 |
| 重新整理後恢復同一局與手牌 | 通過 |
| 其他瀏覽器不能刪除測試房間 | 通過 |
| 離開後回單人選單、後端釋放房間 | 通過 |
| 390×844 手機觸控拖牌與自由測試 | 通過 |
| 自由測試沒有電腦部隊 | 通過 |
| 瀏覽器／Unity 執行期錯誤 | 兩個情境均為零 |

公開測試房間為 `X6RBYL`（電腦）與 `RJ7V3T`（自由測試），測試後已離開並清理。證據與截圖在 `artifacts/tower-duel/solo-web/public-final/`，主要報告為 `result.json`。手機驗證是 Chrome 觸控模擬，沒有宣稱完成 iOS／Android 實機驗收；公開站未另跑整場破堡重賽，重賽另由真實後端 fixture 測試覆蓋。

## 移除驗證與操作

移除腳本已在無網路、沒有正式站掛載的暫存容器中測試：普通移除、`--purge` 清除獨立版本、代理設定驗證失敗時還原。每一步都確認原遊戲 fixture 檔案雜湊維持不變，且禁止對原多人服務執行變更。證據：`artifacts/tower-duel/solo-web/uninstall-validation.txt`。

沒有在正式站實際執行移除，目前單人測試站保持上線。日後可從儲存庫根目錄執行：

```powershell
./examples/tower-duel/solo/Remove-Deployment.ps1 -VmHost 172.233.67.181 -IdentityFile C:/Users/H/.ssh/id_rsa -Purge
```

再刪除 `solo/` 和 `client/Assets/TowerDuelSolo/`（含同層 `.meta`）即可移除來源，原遊戲程式與協定不需修改。完整建置與使用步驟見 [README.md](README.md)。
