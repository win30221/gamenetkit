# C# 組件課程

八份課程 `.cs` 各有一個 Main 入口，另有共用 Check.cs，使用正式 SDK，沒有 UnityEngine、Rumblebound 或網路服務依賴。工具透過共用 Toolchain.ps1 解析釘選 Unity 隨附的 .NET 8 SDK；可用 `-DotNet` 指定其他安裝。從框架根目錄執行：

```powershell
./examples/modules/client/Run.ps1 -Lesson interpolation
```

`-Lesson all` 跑全部。工具直接用 SDK 編譯器與參考組件，不需要 NuGet 還原；輸出在 `.local/module-lessons/client`。每課只編譯 core 與自己需要的套件，`Run.ps1` 的 `$lessons` 對照表就是實際依賴。程式中的 `Check` 用來確認示範結果；接入自己的應用時換成適當錯誤處理。

## 接入自己的 Unity 專案

需要逐檔修改的第一個練習，先做 [Shared World 加入遠端插值](../../../docs/COMPONENT_GUIDE.md#3-第一個實作替-shared-world-加入遠端插值)。該指南也列出每個套件的 assembly、接入位置與完成檢查；以下各課保留最短 API 示範。

先讀 [Shared World 的接線](../../shared-world/README.md#接線順序)，理解連線、收快照與每幀更新各由誰負責。在自己的專案練習，保留基本教學原樣。以插值為例：

1. 在 `Packages/manifest.json` 合併 `com.realtime.interpolation`，保留既有依賴。以下路徑適用於 `examples/<專案>/client` 這個深度，搬移後需調整。
2. 在使用它的 asmdef `references` 加入 `Realtime.Interpolation`；原有 `Realtime.Core` 等引用保留。
3. 在自己的 Composition 建立課程使用的具體型別，使用端持有 core 介面。
4. 在 welcome／重連建立基準、收到快照時 Observe、每幀 Advance；應用自行保存 DTO 與更新 Transform。

```json
{"dependencies":{"com.realtime.interpolation":"file:../../../../packages/unity/interpolation"}}
```

其他套件遵循 `com.realtime.<名稱>`／`Realtime.<名稱首字大寫>`，例如 `com.realtime.prediction`／`Realtime.Prediction`。`prediction-correction` 與 `diagnostics-simulation` 各需兩個套件；複製課程程式不等於完成應用的狀態、協定與場景接線。各契約限制集中在 [MODULES](../../../docs/MODULES.md)。

## interpolation

先用 [插值操作教學](../../interpolation/README.md) 看關閉／固定／自適應的實際差異，再回來讀這份最短數值示範。

讀 [InterpolationLesson.cs](InterpolationLesson.cs)，執行 `-Lesson interpolation`。只需 core＋interpolation。

同一份 tick 4 快照，latest 直接顯示 x=4；固定一個快照間隔的時間軸在下一幀取樣 x=3。課程用兩筆應用資料做線性取樣，SDK 只決定 RenderTick。`TimelineCatalog` 展示如何註冊 fixed／adaptive；要比較 adaptive 的弱網行為，可再看 SDK 測試，這一課不聲稱已模擬完整弱網。

接入點是 `Reset → Observe → Advance → 應用取樣`；重連同時清掉應用快照。停用改成 `LatestSnapshotTimeline`，呈現直接採最新值，再移除具體型別註冊與套件引用。

## prediction-correction

先用 [預測與校正操作教學](../../prediction/README.md)，實際比較延遲下的輸入反應與伺服器推移，再讀下方最短數值示範。

讀 [PredictionCorrectionLesson.cs](PredictionCorrectionLesson.cs)，執行 `-Lesson prediction-correction`。需要 core＋prediction＋correction。

先看課程的 `Motion`：CreateState／Copy／Step 完全由應用提供，且只修改傳入 state。50 ms 沒回包時 authority 仍為 0，預測為 0.200；收到權威 0.100 與 ack 後，未確認的輸入重播到 0.167。顯示偏移另從 0.033 衰減到 0.012，不延遲權威狀態的採用。

實際送出時記錄 `SentInput`，每幀 `Advance`，收到快照時以協定語意換算 `PredictionAck` 再 Reconcile。新生命／重連用 ResetHistory 並清顯示偏移。停用預測改 `AuthorityPrediction`，停用平滑改 `SnapCorrection`；兩個開關的責任不同。

## clock

可先用 [即時連線與恢復](../../network-recovery/README.md) 切換最後觀察 tick／RTT 校準時鐘，觀察弱網下的數值。

讀 [ClockLesson.cs](ClockLesson.cs)，執行 `-Lesson clock`。需要 core＋clock。

收到 tick 30、RTT 100 ms 後，再過 100 ms，SnapshotClock 仍為 30，DisciplinedClock 估計為 34.5；長斷線限制在 45。接入 Observe 時所有本機時間必須使用同一個單調時鐘，RTT 使用秒。它不決定權威步進，也不能保證上下行延遲對稱。重連 Reset；停用回到 SnapshotClock。

## replication

讀 [ReplicationLesson.cs](ReplicationLesson.cs)，執行 `-Lesson replication`。需要 core＋replication。

應用提供 key、life 與深複製函式。初始 a／b，下一份清單只剩新生命的 a：得到 removed=a 舊生命＋b、added=a 新生命。輸入 DTO 之後被修改不會污染已套用狀態；舊 tick 不會清空清單。

先驗證應用 envelope，再 Apply 完整清單；只有成功後才消費 Added／Updated／Removed，並由呈現層建立或移除物件。離線 Clear。停用時仍須由應用自己處理完整清單與生命變更，不能只移除套件後保留空接線。

## actions

讀 [ActionsLesson.cs](ActionsLesson.cs)，執行 `-Lesson actions`。需要 core＋actions。

只有伺服器確認才顯示是基本路徑；加入後可在 Begin 成功時立即顯示暫時效果。同一 ID 不會播放兩次，課程分別產生一次確認、拒絕與逾時。

輸入時 Begin，伺服器回應時 Resolve，每幀 Advance；應用管理效果取消與資源，SDK 計數器不會自動銷毀場景物件。傷害與規則仍由伺服器決定。新 session Clear；停用時回到確認後呈現。

## codec

讀 [CodecLesson.cs](CodecLesson.cs)，執行 `-Lesson codec`。需要 core＋codec。

直接讀取共用 [Go 產生的 fixtures](../../../tests/fixtures/codec.json) 前兩筆，驗證全量 frame 1 與差分 frame 2 都還原正確資料。Reset 後再收差分會得到 MissingBaseline 與 ack=0，應用需要求完整基準。

接入順序為 envelope 驗證 → Decode → 解析還原 payload → 套用應用狀態；只能回報成功解碼的 Ack。課程使用 .NET 的 System.Text.Json 讀 fixture，Unity 接線可用應用自己的 JSON 適配器。停用時直接傳完整 JSON；產生端見 [Go codec](../server/README.md#codec)。

## diagnostics-simulation

可操作的雙向設定、量測、快照暫停與自動重連在 [即時連線與恢復](../../network-recovery/README.md)。

讀 [DiagnosticsSimulationLesson.cs](DiagnosticsSimulationLesson.cs)，執行 `-Lesson diagnostics-simulation`。需要 core＋diagnostics＋simulation。

關閉量測時沒有樣本；加入後 80／120 ms 的 RTT 得到平均 100 ms。弱網設定 100 ms 延遲與 100% 可丟棄訊息損失，snapshot 被丟棄，welcome 仍延後送達。

在收送邊界量測，在應用訊息進出適配器加入 Enqueue／TryTake；自行決定 discardable，不能把控制訊息全當快照丟棄。這是應用層訊息模擬，不是 TCP／QUIC 封包模擬。重連 ClearPending／Reset；停用量測用 NoClientDiagnostics，停用弱網則直接交付訊息。這兩項可各自使用。

## playback

先用 [錄製與播放操作教學](../../replay/README.md) 錄一段移動與物件變化，再實際暫停、拖曳進度及調整速度。

讀 [PlaybackLesson.cs](PlaybackLesson.cs)，執行 `-Lesson playback`。需要 core＋playback。

Load tick 清單後以獨立時間推進：播放到 15、暫停仍為 15、跳到 30，再以兩倍速走 250 ms 到 45。這個時鐘只選時間與 frame index，檔案資料、解碼與呈現由應用持有。

進入離線模式 Load，每幀以 unscaled delta Advance；Seek 時重建應用呈現基準。離開時 Clear，隔離即時 session，不能拿播放 tick 回寫 live world。停用就是不進入離線模式；錄製端見 [Go replay](../server/README.md#replay)。
