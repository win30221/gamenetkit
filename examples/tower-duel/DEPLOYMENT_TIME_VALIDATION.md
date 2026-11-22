# 兵種部署時間

2026-11-15（台灣時間），規則版 `eight-cards-3`、協定 3。已隨 [合併站](solo/MERGED_DEPLOYMENT.md) 部署到主網址。

## 行為

- 六種部隊各自使用 `client/Assets/Resources/TowerDuel/rules.json` 的 `deployTicks`，目前皆為 30 tick（1000 ms），日後可各別調整；支援 0～300 tick。
- 出牌成功立即扣費、換牌並生成部隊。劍士小隊的三名成員使用同一個完成 tick。
- 部署期間維持待機，不能索敵、主動移動、累積衝鋒或開始攻擊前搖；時間到才正常行動，首次傷害還需等待原本的攻擊前搖。
- 部署中的士兵仍可被敵兵、塔及法術攻擊，也保留圓形碰撞與被推擠的行為；在部署期間死亡就直接移除。
- 部署時間依伺服器 tick 計算，冰霜不延長倒數。重連與重複出牌保留原本的 `readyTick`，不重設 1000 ms。
- 客戶端以共用渲染時間軸顯示「部署 1000ms」倒數，完成後恢復原本的減速／衝鋒提示。被推擠時也維持待機動畫；快照停頓不會靠本機時間提早完成。
- 法術沿用各自的施放延遲。

## 已完成驗證

- Go 全部伺服器測試與 `go vet` 通過。
- 六種兵種、雙席、全小隊：前 29 tick 無移動／索敵／攻擊，第 30 tick 開始移動。
- 分別配置 0、15、30、75 tick，確認部署完成時才開始前搖，首次命中為部署完成加上原本的前搖。
- 部署中可受敵兵傷害及被已排程火球擊殺；冰霜降低攻速但不延長部署完成時間。
- 重複指令、扣費／換牌與重連快照保留完成時間；不合法的時間設定被拒絕，法術施放不受影響。
- 單人外掛與真實遊戲後端測試、`go vet` 通過。
- Unity 編譯及客戶端回歸測試通過，包含部署中待機、推擠不播放走路、斷線恢復、快照停頓與完成後開始攻擊。

## WebGL 與編輯器驗證

- 雙人 WebGL 正式建置成功，0 errors；完整雙人對局、重連、重整、重賽、滑鼠／觸控及七種尺寸檢查通過，雙端無 runtime error。
- 單人 WebGL 正式建置成功，0 errors；桌面與手機的真實後端測試確認快照中的部隊在 `readyTick` 前沒有移動、索敵或攻擊，完成後開始移動。
- 已檢視桌面與手機的毫秒倒數畫面，部署結束後倒數消失。
- Unity 數值表控制項、毫秒轉 tick、驗證訊息、DPS 更新、雙檔儲存與外部修改保護通過。
- 真實 Go 後端已成功載入由 Unity 工具輸出的獨立測試規則（部署 1500 ms / 45 tick），原本共用規則仍維持各兵種 1000 ms。
- 編輯器的一鍵建置／啟動、Unity 與 Go 雜湊一致、重啟及停止測試通過。
- Unity 資產／GUID 檢查 322 個檔案，0 errors、0 warnings。

測試產物：

- `artifacts/tower-duel/deploy-time/gameplay-tests.txt`
- `artifacts/tower-duel/deploy-time/client-tests/result.json`
- `artifacts/tower-duel/deploy-time/hud-web/result.json`
- `artifacts/tower-duel/deploy-time/multiplayer-web/result.json`
- `artifacts/tower-duel/deploy-time/solo-web/result.json`
- `artifacts/tower-duel/balance/editor-result.json`
- `artifacts/tower-duel/balance/backend-result.json`
- `artifacts/tower-duel/balance/roundtrip-go-result.json`
