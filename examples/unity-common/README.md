# 教學範例的 Unity 呈現

第 3～9 課各有可加入 Unity Hub 的 `client` 專案。瀏覽器操作頁仍可直接使用；兩種呈現引用同一份 `client/Assets/Scripts` 應用程式，以及同一組 SDK。

| 選課名稱 | Unity 主要操作 |
| --- | --- |
| Prediction | 自己的預測開關、直接／平滑校正、權威推移 |
| NetworkRecovery | 自動重連、模擬中斷、重試倒數與事件；進階快照暫停／時鐘 |
| ObjectsActions | 物件建立／移除／重生、即時預覽、確認／拒絕／逾時、同 ID 重送 |
| Replay | 開始／結束錄製、載入、播放／暫停、跳轉、倍速、回到即時 |
| Interest | 全部／附近、半徑圈、可見與全部數量、自己的必要物件 |
| Codec | JSON／差分、座標精度、實際訊息 bytes、遺失基準恢復 |
| History | 查詢歷史位置、目前／回溯射擊、血量與命中；進階過期射擊／目標重設 |

以下從 `gamenetkit` 目錄執行，以 Codec 為例：

```powershell
# 建置 Windows Player；將 Codec 換成上表的選課名稱
./examples/Build-Unity.ps1 -Project Codec -Target Windows
# 啟動該課服務與已建置的 Player
./examples/Run.ps1 -Example Codec -Unity
```

建置需要可用的 Unity Editor 授權。尚未建置時，`-Unity` 會指出建置指令；不會改用另一課。編輯器中也能開啟該課的 `Assets/Scenes/Lesson.unity`，先啟動一般教學服務，再進 Play mode；預設直接連到該課私有 Go 服務。

WebGL 使用同一份專案：

```powershell
./examples/Build-Unity.ps1 -Project Codec -Target Web
./examples/Run.ps1 -Example Codec
```

開啟該課一般網址後加上 `/unity/`，例如 Codec 是 `http://127.0.0.1:18104/unity/`。教學主機提供已建置檔、SDK `/transport.js` 與同來源 `/ws` 代理；不必讓瀏覽器跨埠直接連私有服務。WebGL 建置關閉壓縮，服務以正確的 JavaScript／Wasm MIME type 回傳。

主機會替 Unity 預設 HTML 加上共用的自適應容器，窄視窗也能顯示左側操作面板。方塊建立時保留具體 MeshRenderer／MeshCollider／BoxCollider 參考，避免 IL2CPP 裁除 `CreatePrimitive` 所需元件；參見 [Unity 的 CreatePrimitive 說明](https://docs.unity3d.com/ja/6000.0/ScriptReference/GameObject.CreatePrimitive.html)。

左側保留當課操作，進階設定預設收合；右側藍色是自己，橘色是其他玩家。自己仍使用預測與校正，其他玩家沿用自適應插值；進階選項可比較不同呈現。圈線代表已交付的權威位置。關閉自動移動後用 WASD／方向鍵，失焦與暫停會送零輸入。Replay 播放有獨立 clock／replica，播放或載入時不接受即時移動。

程式入口：

- 各課 `Assets/Scripts/ExampleSession.cs`：純 C# 送收、驗證與組件接線；不依賴 UnityEngine。
- 各課 `Assets/Scripts/Unity/ExampleClient.cs`：將該 session 的結果映射到場景及操作按鈕。
- [LessonBehaviour.cs](Runtime/LessonBehaviour.cs)、[SceneView.cs](Runtime/SceneView.cs)：共用的輸入生命週期、場景物件與連線面板，沒有同步演算法。
- [UnityRoutes.cs](../browser-common/UnityRoutes.cs)：原始 WS 代理與靜態檔案；既有 `/lesson` 教學通道不變。
- [LessonBuild.cs](Editor/LessonBuild.cs)：固定場景、材質 shader 保留、BuildReport 結果檢查。

`./examples/tests/Test-UnityCompilation.ps1` 可在不啟動 Editor 的情況下，使用已安裝的 Unity／.NET Standard 參考組件編譯七課與 WebGL 傳輸分支。這個檢查、資產 GUID 檢查與真實 WS 測試，均不能代替 Editor import、Player 執行或瀏覽器畫面驗收；目前平台限制見 [VALIDATION](../../docs/VALIDATION.md)。

## 實際 Player 驗證

`examples/tests/Test-UnityPlayers.ps1` 使用每課兩個真正 Windows Player 與獨立測試埠，檢查 Unity JSON／生命週期、SDK socket、雙人 roster、權威輸入 ACK 與移動。開發用 `-rf-lesson-smoke` 才會自動操作並結束；一般啟動不啟用探針。第 3～9 課雙平台建置、14 個 Windows Player 與實際 WebGL 核心操作均有獨立證據；詳見 [最新驗證](../../docs/VALIDATION.md)。
