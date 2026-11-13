# 雙橋推塔場景原型

免費 KayKit 素材組裝的獨立美術原型。原始 FBX 與貼圖位於 `source`，授權為 CC0，來源與下載檔案 hash 見 `source/provenance.json`，原授權見 `source/License.txt`。只使用免費包內容。

## 打開目前成果

從倉庫根目錄查看 `artifacts/kaykit-arena`：

- `UnityPreview`：以 Unity 6000.6.8f1 開啟，場景為 `Assets/TwinbridgeArena/TwinbridgeArena.unity`。
- `TwinbridgeArena.unitypackage`：在 URP 專案使用 Assets → Import Package → Custom Package，匯入後開啟上述場景或放入 `TwinbridgeArena.prefab`。場景附帶 `Rendering/TwinbridgeURP.asset` 與 Universal Renderer；匯入其他 URP 專案時可以沿用該專案的管線。
- `TwinbridgeArena.blend`：Blender 編輯來源，重複植被共用 mesh，貼圖已打包。
- `TwinbridgeArena.fbx`、`hexagons_medieval.png`：一般模型／貼圖交換檔。
- `unity-preview.png`、`unity-urp-preview.png`：Unity URP 實際渲染；`arena-preview.png` 為 Blender 展示視角。
- `audit.json`、`unity-validation.txt`：幾何統計與真正 Unity 匯入驗證。

生成物位於 Git 忽略的 `artifacts`，可以使用以下命令重建；`source` 保留重建所需的原始素材。

## 重建

需要 Blender 5.2 與倉庫釘選的 Unity Editor（包括本機授權）。首次重建由 Package Manager 解析 URP 17.6.0；需要可連線至 Unity 套件來源。由倉庫根目錄執行：

```powershell
./examples/tower-duel/art/Build-Arena.ps1
```

Blender 不在預設位置時傳 `-Blender 'C:/實際位置/blender.exe'`。Unity 沿用 `tools/internal/Toolchain.ps1`，也可傳 `-UnityEditor` 或設定 `GAMENETKIT_UNITY_EDITOR`。只重新匯入現有 FBX 用 `-SkipBlender`；省略兩張 Blender 渲染用 `-SkipRender`，Unity 渲染仍會產生。日誌在 `.local/kaykit-arena`。

## 布局與修改入口

Unity 地面為 XZ，1 單位為 1 公尺。場地寬 20、長 30；藍方近端主堡 `(0, -11)`，紅方遠端主堡 `(0, 11)`；前塔在 `x = ±5, z = ±7.7`。中央河道寬 3，橋心在 `x = ±5`，橋面寬 2.5。

`build_arena.py` 組裝建築、地形與展示鏡頭；`BuildArenaPreview.cs` 建立 URP asset／renderer、替所有 Quality 層級指定管線，設定材質、鏡頭、簡單地面 BoxCollider，並驗證城堡／前塔的實際世界座標。FBX 的座標轉換由 `Art` 節點的 180° 旋轉校正。Unity 中可移動獨立物件試布局；再次重建會以腳本布局覆蓋生成場景，需保留的改動應回寫腳本。

## 驗證範圍

目前 25,138 個可見三角面、103 個 MeshRenderer、9 個共用材質、1 張 1024×1024 atlas。Unity 與 Blender 的三角面數一致；這是原型預算檢查，材質數不等於 draw call 數。模型已在 Unity 成功匯入並渲染，沒有為了降低數字破壞原有低面數造型。

材質使用 URP `Lit`，FBX 的來源色彩與 atlas 分別映射到 `_BaseColor`、`_BaseMap`。照明採即時方向光，來源場景沒有 PPv2、lightmap 或 reflection probe 需要轉換。此成果是可編輯場景，不包含召兵、戰鬥、尋路、連線或裝置效能驗收。附帶碰撞僅作地面／橋面代理，正式遊戲的行走區域、建築阻擋與 SDK 規則仍需接入。原素材刻意以重疊 UV 取樣色盤，不能把它當作 lightmap UV。
