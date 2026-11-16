# B3 地圖與建築美術第一版

依使用者確認的泥濘舊路雛形製作分層素材：乾燥快攻路、右側連通城門內外的長段泥濘路、中立雙門高牆、炮台主堡與箭塔。此批是美術素材準備，不變更正式對戰地圖與遊戲規則。

關鍵朝向：我方建築顯示背面，炮口／弩砲箭頭朝畫面上方的敵營；對面建築顯示正面，武器朝畫面下方。藍、紅兩隊各有兩種朝向。兩席位使用獨立的地面與牆面視角，固定畫面右上來光、左下投影，不直接旋轉已烘焙的美術。

第一批已完成 **兩個地面視角、16 個靜態分格與各自投影**，並以 Unity 6000.6.8f1／URP 的 60° 正交相機實際渲染雙方視角。這批供檢視美術、大小、朝向與接地；尚未替換正式地圖，泥濘減速、城牆視線與索敵規則也尚未實裝。建築開火／倒塌動畫留待後續製作，不把靜態四視角當成完整動畫。

## 素材位置

| 素材 | PNG | 格式 |
| --- | --- | --- |
| 南方／藍方地面 | [ground-south.png](terrain/ground-south.png) | 1024×1536；右側連續泥路 |
| 北方／紅方地面 | [ground-north.png](terrain/ground-north.png) | 1024×1536；左側連續泥路，光向仍為畫面右上 |
| 炮台主堡 | [keep-static-baked.png](buildings/keep-static-baked.png) | 1024×2048，512×512 格 |
| 弩砲箭塔 | [tower-static-baked.png](buildings/tower-static-baked.png) | 1024×2048，512×512 格 |
| 高牆與兩個常開門洞 | [wall-gates-baked.png](wall/wall-gates-baked.png) | 2048×1024，2048×256 格 |
| 樹木／灌木／石頭／草花 | [border-props-baked.png](props/border-props-baked.png) | 768×2304，384×384 格 |
| 分格、錨點、尺度與來源 | [manifest.json](manifest.json) | 包含每個格的名稱與 row／column |

主堡與箭塔本體為 2×2 格：上排依序「藍方背面朝上、藍方正面朝下」，下排依序「紅方背面朝上、紅方正面朝下」。投影放在同一張 PNG 的下半部，順序與本體一一對應。牆面上排為南側、下排為北側，門洞是實際透明缺口，門內地面不另畫乾燥門檻。邊景為 2×3 格，順序見 manifest。

同資料夾未帶 `-baked` 的 PNG 是本體版，方便單獨檢視。**本體版已含美術自身的方向性明暗**；`-baked` 另包含地面投影。投影由底座／腳邊出發、向畫面左下，烘焙一次後直接播放；不在遊戲中另加整體變暗濾鏡。

已依檢視回饋修正牆、主堡、箭塔的陰影分離：底座下緣的高低包含透視深度，投影需涵蓋實際接地輪廓。烘焙時將底座接地區與體積投影先合併，再一次套用透明度，避免接縫與重疊變黑。檢查涵蓋每個底座輪廓欄位，要求至少 98% 接合；透明門洞及樹冠不列為底座。雙方視角已重新擷取，前後對照見 [接地修正](../../../../artifacts/tower-duel/environment-b3-v1/review/contact-fix-comparison.png)。

## 檢視與重建

- 雙方完整預覽及門洞近景：[review](../../../../artifacts/tower-duel/environment-b3-v1/review/)。
- 內建 imagegen 原圖、完整提示詞與生成紀錄：[來源](../../../../artifacts/tower-duel/environment-b3-v1/)。本批未使用 CLI/API 圖像生成 fallback。
- [Prepare-B3-Environment.py](../../scripts/Prepare-B3-Environment.py) 執行透明度清理、分格、基線、縮放及同圖配對投影，保留原畫 RGB，不用程式替代繪圖。需要 Pillow。
- Unity 選單 `Tower Duel/Capture B3 environment artwork` 只建立暫時場景，載入外部美術 PNG；擷取完還原原場景，不執行完整 Setup、不改 map.json 或正式場景。
- 16 格已通過透明邊界、半透明雜邊、完整底座輪廓接合、左下方向及無裁切檢查；Unity 編譯與 GUID 檢查通過。原始生成稿仍保留，最終美術尚待使用者檢視。

來源以已確認 B3 地圖、炮台主堡及弩砲塔為參考，由內建 imagegen 生成供此專案使用；不將這批圖片標示為 KayKit／CC0 素材。
