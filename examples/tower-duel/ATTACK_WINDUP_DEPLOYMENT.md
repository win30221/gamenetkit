# 攻擊前搖部署驗證 · 2026-11-15

公開版本：`20261115T024452Z-bf8955f5`。
[開啟雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)。

六種士兵在前搖結束後才造成傷害；前搖中死亡會取消該次攻擊，弓箭與炸彈也不會射出。
攻擊間隔維持原值，冰霜會同時減慢前搖與冷卻。完整行為見 [協定規則](protocol/README.md)。

## 建置與部署

- Unity `6000.6.8f1` 正式 WebGL、IL2CPP、Gzip、Medium stripping；建置 `Succeeded`，0 errors，耗時 178.18 秒，產物 18,353,087 bytes。
- Go `1.27.3` 重新編譯 Linux amd64 API，透過既有部署腳本一併更新客戶端、伺服器及規則。
- `tower-duel.service` 已重啟並維持 `active/running`，驗證時 MainPID 為 `3155730`。
- 協定維持 3、規則版 `eight-cards-1`；新規則 SHA-256 為 `6cf70db689f479a819abd327d76158b77b18cda4cf4cc1a4e94bf18d1e09da5d`。
- 地圖 SHA-256 維持 `65fdb92c36797fffc8b21a7794154fa3a07427cc5ca4321f3c0ca179544dd8c9`。
- 主機上 12 個部署檔案與 manifest SHA-256 全數一致；7 個公開檔案回應與新 bundle 一致，gzip 回應解壓後比對。
- 公開 HTTPS 健康檢查、八張卡、協定與地圖／規則雜湊、WebGL 版本標記皆通過部署腳本檢查。

## 已完成的驗證

| 驗證 | 結果 |
| --- | --- |
| Go 伺服器測試與 `go vet` | 通過，涵蓋六種兵種前搖、死亡取消、衝鋒／範圍傷害、冰霜與同時命中 |
| Unity 客戶端回歸測試 | 通過，涵蓋前搖動畫、確認後生成投射物、死亡取消、重複快照與重連不重播舊投射物 |
| 本機正式 WebGL 房間操作 | 建立、加入、退出、清除恢復憑證與重新加入共 7 項通過；兩端無 runtime error |
| 本機實際對局 | 弓箭手於 tick 63 開始前搖，tick 74 目標仍有 220 HP，tick 75 命中後為 196 HP |
| 公開 WSS 實際對局 | 弓箭手於 tick 78 開始前搖，tick 88 目標仍有 220 HP，tick 90 命中後為 196 HP |
| 公開正式 WebGL 雙人對局 | 完整對局、八張卡、六種部隊、兩種法術、頁面重整／頁內斷線恢復及同房重賽通過；雙方出牌 13／8 次，勝負與六座塔血量一致，兩端無 runtime error |
| 公開滑鼠／觸控與視窗尺寸 | 拖牌取消、合法格吸附、能量不足、七種視窗尺寸及固定高度改變寬度的構圖檢查通過 |

## 驗證產物

- `artifacts/tower-duel/windup-deploy/web-build-result.json`：正式建置結果。
- `artifacts/tower-duel/client-tests/result.json`：Unity 回歸測試。
- `artifacts/tower-duel/windup-deploy/room-local/result.json`：正式 WebGL 房間操作測試。
- `artifacts/tower-duel/windup-deploy/live-local.json`、`live-public.json`：本機／公開站權威快照與前搖時序。
- `artifacts/tower-duel/windup-deploy/public-hashes.json`、`remote-hashes.txt`：公開回應與主機檔案驗證。
- `artifacts/tower-duel/windup-deploy/public-web/result.json`：公開雙人對局、恢復、重賽與畫面截圖。
- `artifacts/tower-duel/windup-deploy/health.json`：公開站最終健康檢查。
- `.local/deploy/tower-duel/20261115T024452Z-bf8955f5/deployed.json`：本次部署 manifest。
