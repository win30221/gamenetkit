# 八張牌美術與來源

更新：2026-11-12。以現有 KayKit 盾衛、弓箭手、劍士小隊為視覺基準，沿用低面數、圓潤角色、色盤材質與紅藍陣營標記。

## 採用素材

所有角色、武器與骨架動畫來自已下載的 KayKit Adventurers Free 2.0／Character Animations Free 1.1；原始封存檔與選用檔案的 SHA-256、來源及 CC0 授權記錄見 [character-manifest.json](character-manifest.json)。新增檔案由 [Import-Characters.ps1](../scripts/Import-Characters.ps1) 選取原始來源，不修改來源模型或權重。

| 卡牌 | 採用的角色／裝備 | 攻擊片段 | 身高 | 含裝備三角面 |
| --- | --- | --- | --- | --- |
| 盾衛 | Knight／單手劍與方盾 | Melee_Block_Attack | 1.80 m | 6,378 |
| 弓箭手 | Ranger／弓 | Ranged_Bow_Release | 1.70 m | 9,600 |
| 劍士小隊 | Rogue／單手劍，三人編隊 | Melee_1H_Attack_Slice_Horizontal | 每人 1.55 m | 每人 7,878 |
| 炸彈兵 | Mage／smokebomb 道具 | Ranged_Magic_Shoot | 1.65 m | 6,994 |
| 突擊兵 | Rogue_Hooded／雙手劍 | Melee_2H_Attack_Stab | 1.75 m | 7,613 |
| 攻城兵 | Barbarian／雙手斧 | Melee_2H_Attack_Chop | 2.00 m | 7,647 |

待機、步行／奔跑、攻擊與死亡共使用十一個片段。保持 +Z 朝前、腳底為原點、關閉 root motion，模型不提供權威碰撞或傷害。主體縮放、武器掛點與控制器由 [TowerDuelCharacters.cs](../client/Assets/Editor/TowerDuelCharacters.cs) 重建。

## 法術、投射物與卡面

- 火球：2026-11-15 改用 Rumblebound 既有 Hun0FX 火焰與 Cartoon FX 爆炸，補上漸細拖尾與範圍圈。飛行／命中／到期對應伺服器的 start／impact／end tick。
- 冰霜：改用既有 Cartoon FX 法陣並轉為冰藍色，保留真實範圍圈；單位血條附近顯示「減速」，效果結束依權威狀態移除。
- 炸彈：沿用同系列 smokebomb 道具作飛行投射物，補上拋物線、拖尾與重用的粒子爆炸。傷害仍由伺服器結算，投射物與命中特效跟隨呈現時間軸。
- 卡面：依使用者追加要求，已以精緻立體卡通插畫替換早期模型截圖。八張原圖為 1254×1254，正式匯入上限 512；提示詞、來源與檔案見 [CARD_ART.md](CARD_ART.md)。[TowerDuelContent.cs](../client/Assets/Editor/TowerDuelContent.cs) 僅匯入既有插畫，不會在建置時覆寫。
- 特效由 [DuelEffects.cs](../client/Assets/Scripts/DuelEffects.cs) 與 [DuelParticleEffect.cs](../client/Assets/Scripts/DuelParticleEffect.cs) 驅動；原低面數符號保留作法術部署預覽。第三方來源、原授權與免下載重建流程見 [戰鬥特效](COMBAT_FX.md)。

素材製作方案已涵蓋原定五張新卡；採用同一組可重建的角色、道具與原創特效。六種兵與兩種法術已進入雙 Windows Player 對局；後續生成的插畫卡面另行記錄，場內模型維持現況。

## 音效與驗證

滿版外場沿用 KayKit 樹木與岩石，由 [TowerDuelSurroundings.cs](../client/Assets/Editor/TowerDuelSurroundings.cs) 固定種子組裝。2026-11-13 改為連續草地網格、原創程序色調貼圖、微幅起伏、延伸曲線河流與疏密樹叢，隱藏原本矩形展示底座；149 棵樹、74 個岩石，新增碰撞體為 0，不改動 20×30 m 的規則地圖。數量與碰撞體檢查產生於 `client/Builds/surroundings-audit.json`，視窗與鏡頭驗收見 [RESPONSIVE_VIEW_VALIDATION.md](../RESPONSIVE_VIEW_VALIDATION.md)。

選牌、部署、拒絕、受擊、死亡、火球、冰霜、倒數與勝負音效為 [DuelAudio.cs](../client/Assets/Scripts/DuelAudio.cs) 的原創合成音；透過 UI／SFX 混音群組播放，提供靜音切換，限制同時音軌數並保留輸出餘量。

最新角色面數記錄產生於 `client/Builds/character-audit.json`。雙 Player 三局驗收已觀察六種角色的腿／手臂骨骼變動、死亡動畫、投射物與兩種法術；詳見 [P0 驗收紀錄](../P0_VALIDATION.md)。手機實機與真人可讀性／聽感仍需獨立驗收。
