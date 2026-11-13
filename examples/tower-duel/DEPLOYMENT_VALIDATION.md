# 部署、血量與能量驗收 · 2026-11-13

後續血條與拖牌修正已以版本 `20261113T024139Z-99d819bc` 更新公開站，最新驗收見 [拖牌修正驗收](DRAG_REFINEMENT_VALIDATION.md)。下方保留前一版的完整規則驗證紀錄。

本輪功能與地圖構想見 [部署與地圖設計](DEPLOYMENT_AND_MAP_DESIGN.md)。本輪變更已於 2026-11-13 10:12（台灣時間）部署至[公開站](https://tower-duel.172.233.67.181.sslip.io/)。

公開版本為 `20261113T021129Z-ccbdaa3e`，地圖雜湊 `65fdb92c36797fffc8b21a7794154fa3a07427cc5ca4321f3c0ca179544dd8c9`。
HTTPS 健康檢查、客戶端版本標記、API 地圖／規則雜湊與 12 個部署檔案的 SHA-256 清單均已驗證一致。
公開站亦通過真實 HTTPS／WSS 雙人對局、390×844 觸控與 720×1000 滑鼠操作、七種尺寸、重整恢復及同房重賽；兩端無 browser page error。
完整結果與截圖位於 `artifacts/tower-duel/deployment/public-web/`。

| 驗證 | 結果與範圍 |
|---|---|
| Go 測試與 vet | 通過。涵蓋兩席與左右路擴區、失去己方塔不擴區、另一側／河道／後場仍封鎖、小隊邊界整組拒絕、重複 action 與新局重置。 |
| Unity 編譯與建置 | Unity 6000.6.8f1；Windows development 與 WebGL release 建置成功。 |
| Unity 資產檢查 | `check_unity_project.py` 檢查 304 個檔案，0 errors、0 warnings。 |
| 雙 Windows Player 實際對局 | 通過；兩席出牌 13／8 次，勝負與六座塔血量一致，八張牌、六種角色動畫與法術完整呈現。兩端都有血量數字、部分能量填充；防守端實際出現滿能量晃動。 |
| 真實瀏覽器滑鼠與觸控 | Chrome 157；720×1000 滑鼠與 390×844 觸控通過短拖取消、非法取消、能量不足、格心部署、手指偏移、標頭空隙落點、重整恢復與同房重賽。兩端無 browser page error。 |
| 螢幕尺寸 | 七種尺寸通過畫布與卡面比例檢查，另檢查 390／768／912／1366 同高視窗的垂直構圖一致。 |
| 最終 UI 固定快照 | 兩席各通過八項放置檢查，包括單側 pocket、兩側 pocket、小隊前緣與重賽。另擷取放大血量、部分能量與滿能量提示。這項使用合成快照測客戶端，不替代 Go 的權威規則測試。 |

雙 Player 原先的 `wholeFieldVisible` 仍如實記錄為 false：最外圍後方草地可在滿版構圖之外。通過條件改用 `tacticalContentVisible`，逐一檢查兩邊主堡、前塔、血條高度、主堡後緣與橋頭，符合既有 [滿版鏡頭設計](RESPONSIVE_VIEW_VALIDATION.md)，未改動鏡頭來配合測試。

產物位於 `artifacts/tower-duel/deployment/`：

- `player-verified/result.json`：雙 Player 對局與血量／能量證據。
- `web/result.json`：真實網路對局、滑鼠／觸控與七種尺寸。
- `web/card-follow-before-board.png`：拖出一小段時的虛化卡面。
- `feedback/result.json`：兩席各八項放置決策。
- `feedback/enemy-pocket-open-seat1.png`：敵方前塔倒下後該側格線開放。
- `feedback/numeric-health-fractional-energy-seat1.png`、`feedback/full-energy-reminder-seat1.png`：最終手機血量與能量介面。

Android／iOS 的實機手指遮擋、操作手感與長時間效能仍待實機試玩；「潮汐遺跡」長短路目前為設計稿，尚未改動場景地形或導航。
