# B3 固定光源 Sprite Sheet

2026-11-16：依使用者要求，把人物本體的方向性明暗及自身地面投影烘焙進圖片。光線固定從畫面右上來，人物左側保留較冷、可讀的暗色，投影從腳邊往左下；雙方鏡頭一致。保留前次確認的兵種大小。

## 正式檔案

`client/Assets/Resources/TowerDuel/B3/Units/{kind}/`：

- 原始 `idle.png`、`walk.png`、`attack.png` 保留。
- 正式使用 `idle-baked.png`、`walk-baked.png`、`attack-baked.png`。
- 每張圖上 8 列是人物，下 8 列是相同朝向／相同動作幀的地面陰影。待機 1 欄，行走與攻擊 6 欄。
- 共 6 種角色，624 格人物、624 格配對影子；身體 alpha 與原始 atlas 完全相同，腳底點、裝備左右手與兵種大小不變。
- 人物框使用原本腳底 pivot；地面影子使用 (0.75, 0.75) pivot 與固定 2 倍地面畫布。兩者從同一張 PNG 取樣，投影層貼地播放，遊戲不再從人物輪廓即時投射陰影。
- PNG 上限 4096、RGBA、不壓縮、不生成 mipmap、Bilinear、Clamp。原 atlas 維持原設定。

## 製作與來源

角色原畫來自本專案先前的內建 **imagegen** 製作；完整原始圖片與各角色提示詞保留於 `artifacts/tower-duel/sprites-b3/source/{kind}/`。

此次先以內建 **imagegen** 編輯炸彈兵小樣，鎖定明亮的 B3 色彩、右側高光、左側彩色暗部，以及腳邊左下投影；未使用 CLI/API fallback。小樣與提示詞位於 `artifacts/tower-duel/sprites-b3/baked-shadow-review/`：

- `bomber-baked-reference-v2.png`：方向性明暗示意。
- `bomber-reference-prompt.txt`：第一版提示詞。
- `bomber-lighting-correction-prompt.txt`：修正主體光向與陰影的提示詞。

正式 624 影格由 Unity Editor 的 `TowerDuelSpriteBaker` 離線輸出，沿用每一格原畫與 alpha。人物增加固定、較柔和的右亮左暗色階，避免原先即時材質把暗側壓黑；地面投影依每一格 alpha 與固定光向輸出低不透明度影格，邊緣在離線階段柔化。小樣用於美術方向，正式影格以引擎烘焙保持動畫身份及腳底位置一致。

## 重建

如有修改原始角色圖，先完成 `Prepare-B3-Sprites.py {kind}`，再於乾淨、未播放的 Unity Editor 執行：

```csharp
TowerDuel.Editor.TowerDuelSpriteBaker.BakeAll();
TowerDuel.Editor.TowerDuelSpriteContent.Prepare();
```

也可使用選單 `Tower Duel / Bake fixed-light sprite sheets`，接著 `Prepare B3 sprite characters`。變更光向或烘焙參數後需重新輸出圖片。

人物自身明暗保持固定，不再隨場景主光強度／光色重算。建築遮擋造成的環境陰影仍由引擎接收，紅色受擊、陣營換色、部署預覽及死亡透明度也仍由遊戲控制。額外接觸陰影停用，避免重疊加黑。

## 驗證與預覽

`Validate-B3-Baked.py` 檢查 18 張正式 PNG：原始 alpha、方向性明暗、暗部色彩、人物／影子配對、投影透明度、邊界、腳底相接與左下方向。偏身且單側採樣區沒有可見表面的兩個動作格仍接受完整 alpha 與影子檢查，其餘可見表面採樣檢查寫入報告。

結果與 Unity 真實渲染：

- `baked-shadow-review/assets-validation.json`：624 人物與 624 配對陰影通過。
- `baked-shadow-review/before-lineup.png`、`after-lineup.png`：舊即時材質與新版烘焙對比。
- `native/` 與 `native-{kind}/lighting-validation.json`：雙方視角、固定烘焙色彩、不重複壓暗、建築遮光、貼地投影、受擊恢復、陣營色。
- `baked-shadow-review/client-tests/result.json`：客戶端、動作、影格配對、地面姿態、受擊／消散及卡面。
- `baked-shadow-review/player-test/result.json`：完整八牌雙 Player 實戰。

上述路徑皆相對 `artifacts/tower-duel/sprites-b3/`。本機 Solo 與 Windows 檢查版均使用正式烘焙圖；尚未部署網頁版。

## 炸彈兵行走透明底修正

2026-11-16 試玩發現往前行走時出現矩形邊框。來源為 `source/bomber/walk-b.png` 的半透明底色，裁切後仍留在人物包圍盒內；原本低 alpha 門檻使它與人物一起顯示，也進入地面影子。

`bomber.json` 新增 `sourceAlphaCutoff.walk-b = 128`，在縮放前清理該來源的底色，再由縮放產生人物輪廓的正常反鋸齒。原始生成檔保留，其他來源與卡面維持原流程。正式炸彈兵行走 atlas 的遠離實體輪廓半透明殘留由 107,396 像素降為 0；重新輸出對應的烘焙人物與投影影格。

`Validate-B3-Baked.py` 新增人物輪廓外大面積半透明底色檢查，避免 alpha 一致但連原始錯誤一起保留的情況。624 格資料檢查與炸彈兵雙視角原生渲染均通過。修正前實際畫面：`baked-shadow-review/bomber-walk-before-fringe-fix.png`；修正後行走預覽：`native-bomber/walk-native.gif`。
