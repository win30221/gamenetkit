# 八張插畫卡面

2026-11-12 依使用者要求，以精緻立體卡通插畫替換模型縮圖。先製作盾衛作視覺基準，其餘七張沿用該圖的光線、材質與比例，分別生成。

使用內建 **image_gen**，未使用 CLI。原始 PNG 原樣複製到正式專案，保留內嵌來源資訊；完整提示詞、參考關係、SHA-256 及儲存路徑見 [card-art-manifest.json](card-art-manifest.json)。此批為本專案生成的原創角色／技能圖，不是從參考遊戲擷取的圖像。

| 卡牌 | 插畫主題 | 正式資產 |
| --- | --- | --- |
| 盾衛 | 藍羽銀盔、盾牌、堅定表情 | [guard.png](../client/Assets/Resources/TowerDuel/Cards/guard.png) |
| 弓箭手 | 銅紅髮、綠色兜帽、拉弓姿勢 | [archer.png](../client/Assets/Resources/TowerDuel/Cards/archer.png) |
| 劍士小隊 | 三位綠衣劍士、前後層次 | [swords.png](../client/Assets/Resources/TowerDuel/Cards/swords.png) |
| 炸彈兵 | 紫色法帽、護目鏡、燃燒引線炸彈 | [bomber.png](../client/Assets/Resources/TowerDuel/Cards/bomber.png) |
| 突擊兵 | 綠色兜帽、面罩、雙手長劍 | [charger.png](../client/Assets/Resources/TowerDuel/Cards/charger.png) |
| 攻城兵 | 橘紅鬍鬚、厚重皮甲、巨斧 | [siege.png](../client/Assets/Resources/TowerDuel/Cards/siege.png) |
| 火球 | 熔金核心、橙紅火焰與彗尾 | [fireball.png](../client/Assets/Resources/TowerDuel/Cards/fireball.png) |
| 冰霜 | 六角冰晶、藍色漩渦與冰霧 | [frost.png](../client/Assets/Resources/TowerDuel/Cards/frost.png) |

共同提示方向為「premium polished 3D cartoon、chunky sculpted forms、warm golden key / cyan rim light、square close-up、readable at mobile card size、no text / UI / border」。完整逐張提示詞保留在清單。

八張原圖皆為 1254×1254；Unity 匯入上限 512，使用 sRGB、雙線性過濾、Clamp、無 mipmap 與高品質壓縮。名稱與紫色費用標章由 UI 繪製，插畫鋪滿牌面；費用不足降低插畫亮度。建置只匯入既有圖片，不重新生成或覆寫插畫。

依使用者指定，目前戰場仍沿用 KayKit 模型，插畫作為卡面及後續模型升級的方向。兩者以兵種裝備、色彩與職能對應，不聲稱模型已升級成插畫品質。

部署預覽使用實際兵種 Prefab 的半透明副本，不是卡面貼圖：正常部隊一個模型、劍士小隊三個編隊模型；不投影陰影、不產生碰撞或傷害。法術只顯示規則資料中的作用面積與外圈：火球半徑 2.2 m、冰霜 2.8 m。非法位置改為紅色，拖回牌區、離開視窗、取消或提交後清除預覽。
