# 等待房間退出驗證 · 2026-11-13

已於 2026-11-13 15:46（台灣時間）部署至[雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)，版本 `20261113T074624Z-7cae3c74`。

- 等待面板在「準備對戰」旁顯示「退出房間」，避免原本的右上角「離開」被等待遮罩擋住。
- 建立者及加入者都可退出，已準備與倒數時仍可操作。沿用明確的離房訊息並清除本機重連資料，返回大廳後可重新建立或加入。
- Unity 6000.6.8f1 WebGL release 建置成功、零錯誤；306 個 Unity 資產的 GUID／腳本引用檢查零錯誤、零警告。
- 本機真實 HTTP／WebSocket 房間測試通過七項情境：建立者退出、退出後重整、重新開房、已準備的加入者退出、重新加入、倒數退出，以及結算後回大廳再開房。使用桌面滑鼠與手機觸控，兩端無 browser page error。
- 採 client-only 部署，12 個檔案均與 SHA-256 清單一致。HTTPS 健康檢查、協定與地圖／規則雜湊通過，權威服務 PID `3110574` 維持不變。
- 公開網址的真實 HTTPS／WSS 房間重跑相同七項情境全數通過，桌面與手機兩端無 browser page error；公開報告及截圖位於 `public-web/rooms/`。

測試腳本為 `scripts/Test-Room-Exit.cjs`。本機報告與截圖位於 `artifacts/tower-duel/room-exit/local-verified/`，部署紀錄位於 `artifacts/tower-duel/room-exit/public-web/`。
