# B3 Sprite 逐角色製作進度

使用者授權：每個角色完成 2D 呈現並通過實戰檢查後，同步替換卡面與包邊，再繼續下一個角色；若 Codex 使用額度不足則保存進度。

最新指示：使用者已試玩並接受目前成果，要求受擊改為紅色，完成後繼續下一個角色。紅色受擊與恢復已完成；六種角色的 2D、同造型卡面與包邊皆已完成，完整雙 Player 實戰與本地 Solo 建置通過。

最新修訂：使用者要求將陰影直接放進 Sprite Sheet，並再次強調人物主體也要有方向性明暗。六角色已完成固定右上亮／左側暗的離線烘焙，並把配對的腳邊左下投影放在同一 PNG 的下半部。新版保留較明亮、可讀的 B3 色彩，取代原先偏黑的即時圓弧受光；詳見 art/b3-production/BAKED_LIGHTING.md。

## 共用規格

- 已確認美術基準：B3 圓潤 Q 版動漫風。
- 朝向：S、SW、W、NW、N、NE、E、SE，保留劍盾左右手。
- 初版每角色：8 個待機方向姿勢、48 格行走、48 格攻擊，共 104 格；待機搭配小幅呼吸，死亡使用共用消散回饋。
- 分格預設 192×192，腳底錨點 (96, 176)；劍士小隊採 224×224、錨點 (112, 208)，突擊兵與攻城兵採 256×256、錨點 (128, 224)，增加武器留白並維持場內尺寸。每角色採固定分格及一致縮放。
- 動畫根據呈現時間軸與權威攻擊 tick 運作，傷害仍由原伺服器結算。
- 同步更新同造型卡面、藍金包邊、下一張預覽與拖曳卡面。
- 光照／投影：畫面右上方來光、往左下方投影。人物接收主光強度、光色、環境光與建築陰影。逐幀 alpha 輪廓沿光方向投到腳底平面，搭配柔邊接觸陰影；消散時同步淡出。兩席位維持相同畫面方向，人物不顯示陣營圓盤。

## 目前狀態

| 角色 | 素材 | 遊戲整合與驗收 |
| --- | --- | --- |
| 盾衛 | 104 格及卡面、共用包邊已完成分格與 Unity 匯入 | 完成；雙視角受光／貼地 GPU 檢查、客戶端回歸及 Windows 雙 Player 實戰通過 |
| 弓箭手 | 104 格、卡面與共用包邊完成 | 完成；雙視角原生渲染、紅色受擊、放箭與投射物同步、客戶端及雙 Player 實戰通過 |
| 劍士小隊 | 104 格、三人卡面與共用包邊完成 | 完成；原生受光／受擊、客戶端及雙 Player 實戰通過，保留三個獨立單位 |
| 炸彈兵 | 104 格、卡面與共用包邊完成 | 完成；紫色帽／制服／鏡片不受陣營換色影響，原生受光、客戶端及雙 Player 實戰通過 |
| 突擊兵 | 104 格、卡面與共用包邊完成 | 完成；雙視角原生受光／受擊、客戶端及雙 Player 實戰通過；保留雙手長劍、衝鋒速度與雙倍傷害規則 |
| 攻城兵 | 104 格、卡面與共用包邊完成 | 完成；修正左向行走／揮斧與低處斧刃干擾腳底錨點；原生受光／受擊、客戶端及雙 Player 實戰通過 |

盾衛來源、完整提示詞與原始檔副本：artifacts/tower-duel/sprites-b3/source/guard/。分格報告與 GIF 預覽：artifacts/tower-duel/sprites-b3/guard/。

Unity 6000.6.8f1 需在正常本機授權存取權限下啟動；本次測試 Editor 已以隱藏批次模式啟動，使用 Unity CLI 1.0.0 並固定目標至 tower-duel/client。

已通過：Test-Client.ps1 的 SDK／網路、特效及 B3 Sprite 檢查；B3 檢查涵蓋八方向、雙方鏡頭、腳底錨點、行走／時間軸停滯、命中幀、冰霜、部署、取消前搖、消散與卡面。結果：artifacts/tower-duel/sprites-b3/client-tests/result.json。

本機工具：.local/unity-cli-pacing/unity.exe（1.0.0）；Python 使用 Blender 5.2 內建 Python，PYTHONPATH 指向 .local/sprite-tools（Pillow 12.1.1）。

盾衛最後驗收：artifacts/tower-duel/sprites-b3/guard-grounded-player-test/result.json，雙方皆使用 2D 盾衛與 B3 卡面／包邊，陣營圓盤數 0，投影方向皆為 (-0.7071, -0.7071)，完整八牌對戰正常結束。P95 畫面時間約 16.9 ms。GPU 受光／光色／遮光／腳底相接檢查與雙視角比較圖：artifacts/tower-duel/sprites-b3/native/lighting-validation.json、lighting-comparison.png。此處比較圖是 Unity 原生渲染；隱藏 Windows Player 不提供可靠螢幕截圖。

Unity 素材與腳本 GUID 最終檢查：703 個檔案、0 錯誤、0 警告。

弓箭手驗收：artifacts/tower-duel/sprites-b3/archer-player-test/result.json，兩席位的 2D 人物、卡面、包邊及貼地投影遮罩均為 3（盾衛＋弓箭手），陣營圓盤為 0；完整八牌實戰正常結束。原生方向、受光、受擊及動作 GIF：artifacts/tower-duel/sprites-b3/native-archer/。來源與提示詞：artifacts/tower-duel/sprites-b3/source/archer/。角色資料：art/b3-production/archer.json。GUID 檢查更新為 601 檔案、0 錯誤、0 警告。本機 Builds/Solo 已重建。

遠程釋放姿勢已使用經權威命中確認的投射物起飛時間；回歸檢查涵蓋拉弓等待呈現 tick、冰霜、轉移目標後仍使用原命中目標、釋放姿勢與箭矢可見同步、重複快照不重放箭矢及前搖死亡取消。

最終六角色驗收（2026-11-16）：artifacts/tower-duel/sprites-b3/siege-client-tests/result.json 與 siege-player-test/result.json 均通過。雙方 B3 人物、卡面、包邊及投影遮罩均為 63（六種全數），陣營圓盤為 0，畫面投影皆約 (-0.7071, -0.7071)。八張牌、六種單位的行走／攻擊與兩種法術皆實際使用，對戰以主堡摧毀正常結束；P95 約 16.67 ms。炸彈兵的紫帽／鏡片保護、突擊兵與攻城兵的新素材均包含於此最後建置。

各角色原生受光、光色、建築遮光、腳底投影、接觸陰影、紅色受擊／恢復及陣營色檢查皆通過，證據見 native/ 與 native-{archer,swords,bomber,charger,siege}/lighting-validation.json。突擊兵單獨實戰見 charger-player-test/result.json（遮罩 31）；劍士與炸彈兵分別見 swords-player-test/、bomber-player-test/。每角色原始素材與完整提示詞保留於 source/{kind}/；量化分格規格保留於 art/b3-production/{kind}.json。

固定時間軸測試已改以 SDK 實際收包時間推進到出手點，避免 Editor JSON 序列化耗時造成偶發的「仍在出手前」誤判；並保留出手幀與可見投射物同步的原本斷言。未變更伺服器傷害、數值或出兵規則。

本地試玩包 examples/tower-duel/Builds/Solo 已以全部六角色重建（Succeeded、0 errors），入口 Start-Sandbox.cmd（自由測試）與 Start-Solo.cmd（對戰電腦）。地圖、主堡、防禦塔與法術目前仍使用現有正式素材，B3 概念稿留待後續逐項整合。

## 2026-11-16 人物比例與自身明暗修訂

使用者試玩後要求人物整體放大，並依兵種形象調整大小；攻城兵須明顯較大。已依既有畫布與腳底點放大呈現，卡面維持原畫。WorldHeight：盾衛 1.50、弓箭手 1.36、劍士 1.28、炸彈兵 1.46（包含大帽）、突擊兵 1.58、攻城兵 2.25。攻城兵相對盾衛為 1.5 倍；UnitHeight 同步更新至 WorldHeight 的兩倍，供血條與投射物位置使用。接觸陰影也隨角色比例放大，伺服器碰撞與數值維持原規則。

原先平面頂點的法線變化過小，加上最低直接光過高，導致人物背光側仍然偏亮。現在改為依實際人物高度評估逐像素圓弧體積法線，降低背光側直接光與環境光，保留 B3 色階、紅色受擊及建築遮光。此為 2D sprite 的程序化體積受光。六種人物皆以相同角色、相同像素區域，左右鏡像主光驗證亮暗側交換；雙席位結果一致。驗證遮罩以正式 atlas alpha 取得，排除近景中藍色河水被誤判為陣營衣服，以及固定腳邊探針落在保留原色的黑色描邊。

實際 Unity 同畫面比例圖：artifacts/tower-duel/sprites-b3/scale-lighting-review/six-character-lineup.png，順序盾衛、弓箭手、劍士、炸彈兵、突擊兵、攻城兵。六種 native*/lighting-validation.json 均通過含「left/right surface lighting」的新檢查；client-tests 與 player-test 位於 scale-lighting-review/。完整八牌實戰通過，雙方 2D 人物／卡面／投影遮罩為 63，無陣營圓盤，投影仍往左下；P95 約 16.9 ms。GUID 檢查 661 檔案，0 錯誤、0 警告。

## 固定光向烘焙最終驗收

六角色正式 `*-baked.png` 已輸出，624 格人物與 624 格同張配對陰影。`baked-shadow-review/assets-validation.json` 通過原始 alpha 一致、方向性明暗、可讀暗部、投影透明度、無裁切、腳底相接及左下方向檢查。`native*/lighting-validation.json` 皆通過固定烘焙色彩、無重複接觸陰影、建築遮光、受擊恢復與雙視角一致。

`baked-shadow-review/client-tests/result.json` 與 `player-test/result.json` 均通過；兩席位人物、卡面與投影遮罩 63、陣營圓盤 0、畫面投影方向一致，完整八牌對戰以主堡摧毀正常結束；P95 約 16.88–16.89 ms。GUID 703 檔案，0 錯誤、0 警告。本機 Solo 已重建，Succeeded／0 errors。`before-lineup.png` 與 `after-lineup.png` 保留此次修改前後的實際 Unity 畫面。

後續試玩修正炸彈兵往前行走的矩形邊框：清除 walk-b 來源的半透明底色，重製行走與對應烘焙影格。輪廓外半透明殘留由 107,396 降至 0。新增此類殘留的影格回歸檢查；624 格資料、炸彈兵雙視角受光／受擊／貼地檢查與 703 檔 GUID 檢查通過。PNG 正規化處理後保留原畫及行走資料；詳見 BAKED_LIGHTING.md。
