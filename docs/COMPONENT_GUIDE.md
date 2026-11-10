# 把組件加入自己的遊戲

先完成 [Shared World](../examples/shared-world/README.md) 的兩視窗練習，能指出「連線、權威狀態、每幀呈現」各在哪裡，再從這份文件加入第一個組件。完整學習路線見 [examples](../examples/README.md)；本頁教接入步驟，API 契約與容量限制以 [MODULES](MODULES.md) 為準。

組件是程式庫，需要由應用呼叫。加入 UPM 套件或 `using` 只完成安裝；收包、更新、重設與呈現接好後，才會產生效果。

## 1. 先決定要改哪一端

| 想解決的問題 | 加入哪裡 | 動手前要有什麼 |
| --- | --- | --- |
| 其他玩家移動跳格 | Unity interpolation | 帶 tick 的快照、應用快照歷史與取樣；不需改 Go 協定 |
| 自己移動要等伺服器 | Unity prediction，視需要 correction | 與伺服器一致的移動模型、輸入序號及權威 ack 語意；協定不足時先改雙端 |
| 不知道伺服器目前時間 | Unity clock | server tick、同一單調時鐘、配對的 RTT；協定沒有 probe/pong 時須補雙端 |
| 新增／移除／重生物件難管理 | Unity replication | 完整清單、穩定 key 與 life；協定沒有生命識別時須補伺服器 |
| 技能先出效果，再等確認 | Unity actions＋伺服器動作處理 | action ID、去重、成功／拒絕回覆；權威規則由伺服器執行 |
| 想看數據或重現弱網 | Unity diagnostics／simulation，各自可用 | 收送邊界、同一時鐘、可丟棄與控制訊息分類 |
| 只傳附近的物件 | Go interest | 每個接收者的投影、完整範圍清單、客戶端移除與重新進入處理 |
| 降低快照流量 | Go codec＋Unity codec | 雙端 frame envelope、成功解碼的 frame ack、缺基準時回全量 |
| 用過去位置判斷命中 | Go history＋lagcomp | pose 深複製、可接受的回溯時間、生命識別與應用命中幾何 |
| 錄製與離線播放 | Go replay／Unity playback | 錄製格式、完整驗證、離線資料與即時狀態隔離；兩端能力可分別使用 |

表中的前置條件是應用接線需求。Unity 選用套件各自只依賴 core；例如 correction 套件本身不要求 prediction，AOI 也不要求先裝 codec 或 replay。自動重連的 `ReconnectPolicy` 已在 core，應用仍需決定重試、身份恢復及世界重建。

## 2. 每次加入都走相同流程

1. **定義資料與責任。** 先決定輸入、快照、時間、ack 與生命由誰供應。需要改訊息格式就先更新雙端協定。
2. **安裝與引用。** Unity 合併 `Packages/manifest.json`，再改實際使用型別的 `.asmdef`；Go 在應用 module 引用 SDK，再 import package。
3. **組裝。** 在自己的 Composition 建立實作；依 session／接收者分開持有有狀態物件。不要讓不同玩家共用 codec 基準或 AOI 遲滯狀態。
4. **接事件與更新。** 標出「收到資料」「實際送出」「每幀／每 tick」的呼叫位置。驗證訊息後才更新組件與應用狀態。
5. **接生命週期與畫面。** welcome、新生命、換世界、離線與重連要重設對應歷史；把組件結果交給自己的 DTO 或 GameObject。
6. **驗證與回退。** 比較加入前後的行為，測一次斷線／重連，再切回基本策略。要移除套件時，也移除具體型別與 assembly 引用。

下面完整做一次「Shared World 加入遠端插值」。其他組件的實際入口在第 4、5 節。

## 3. 第一個實作：替 Shared World 加入遠端插值

目標是讓**其他玩家**從每秒 15 份快照變成連續呈現。自己的方塊仍使用最新權威位置；這一步先學插值，自己的即時反應留給 prediction。伺服器保持 30 Hz 模擬與 15 Hz 快照。

### 3.1 準備練習專案與檔案

將 [shared-world/client](../examples/shared-world/client) 的 `Assets`、`Packages`、`ProjectSettings` 複製到自己的 Unity 專案，例如 `examples/my-game/client`，保留資產的 `.meta`。以下路徑都相對於這個**練習專案**；原本 Shared World 留作比較。`Library`、`Temp`、`Builds` 是生成物，無須複製。

這次會改六個既有檔案、新增一個檔案：

| 檔案 | 要改什麼 |
| --- | --- |
| `Packages/manifest.json` | 加入 interpolation 本機套件 |
| `Assets/Scripts/Realtime.Examples.SharedWorld.asmdef` | 引用 `Realtime.Interpolation` |
| `Assets/Scripts/NetcodeComposition.cs` | 選擇 FixedTimeline |
| `Assets/Scripts/RemotePositions.cs`（新增） | 深複製快照、依 RenderTick 取樣位置 |
| `Assets/Scripts/ExampleSession.cs` | 成功收包後保存歷史、斷線時清除 |
| `Assets/Scripts/Unity/WorldPresentation.cs`、`ExampleClient.cs` | 將取樣結果用在遠端方塊 |

只換時間軸不會自動改變 Transform，下面的保存與取樣也要完成。

### 3.2 安裝套件並引用 assembly

在 `Packages/manifest.json` 的既有 `dependencies` 加一筆，保留 core、transport 及 Unity 模組：

```json
"com.realtime.interpolation": "file:../../../../packages/unity/interpolation"
```

JSON 欄位之間要有逗號。這個相對路徑以 `examples/my-game/client/Packages` 為起點；如果練習專案在別處，改成從它的 `Packages` 指到 `packages/unity/interpolation` 的路徑。

把 `Assets/Scripts/Realtime.Examples.SharedWorld.asmdef` 的 `references` 改為：

```json
"references": ["Realtime.Core", "Realtime.Transport", "Realtime.Interpolation"]
```

保留 asmdef 的其他欄位。Unity 呈現 assembly 沒有直接使用 interpolation 型別，這一步只改上述應用 assembly。等待 Unity 完成套件匯入；此時應無找不到 namespace／assembly 的錯誤。

### 3.3 在 Composition 選擇時間軸

`NetcodeComposition.cs` 加入 `using Realtime.Interpolation;`，並把 `Timeline` 初始化改成：

```csharp
public IPresentationTimeline Timeline { get; } =
    new FixedTimeline(tickRate: 30, snapshotRate: 15, intervals: 2);
```

兩個快照間隔約為 133 ms。這是呈現緩衝，權威模擬沒有變慢。此處的頻率來自 Shared World 協定；自己的遊戲應使用其實際頻率。

### 3.4 新增應用快照歷史與取樣

新增 `Assets/Scripts/RemotePositions.cs`，內容如下。`SnapshotBuffer` 只保存參考，所以 Capture 主動複製資料，避免之後的新快照修改舊樣本。

```csharp
using Realtime.Core;

namespace Realtime.Examples.SharedWorld
{
    public sealed class RemotePositions
    {
        private readonly IPresentationTimeline timeline;
        private readonly SnapshotBuffer<PlayerState[]> history =
            new SnapshotBuffer<PlayerState[]>(128);

        public RemotePositions(IPresentationTimeline timeline)
        {
            this.timeline = timeline;
        }

        // 只在 ReplicaState.Apply 成功後呼叫。
        public void Capture(ReplicaState state)
        {
            var copy = new PlayerState[state.Players.Count];
            int index = 0;
            foreach (var player in state.Players.Values)
                copy[index++] = new PlayerState {
                    id = player.id, x = player.x, z = player.z, ack = player.ack
                };
            history.Add(state.Tick, copy);
        }

        public void Clear() => history.Clear();

        public bool Sample(long id, out float x, out float z)
        {
            x = z = 0;
            // 已離開的玩家不能因歷史資料而重新出現。
            if (history.Count == 0 ||
                !Find(history[history.Count - 1].Value, id, out _)) return false;
            if (!history.TryBracket<long, PlayerState>(
                timeline.RenderTick, id, Find, out var pair)) return false;
            var a = pair.Before;
            var b = pair.HasAfter ? pair.After : a;
            x = (float)(a.x + (b.x - a.x) * pair.Fraction);
            z = (float)(a.z + (b.z - a.z) * pair.Fraction);
            return true;
        }

        private static bool Find(PlayerState[] players, long id, out PlayerState found)
        {
            foreach (var player in players)
                if (player.id == id) { found = player; return true; }
            found = null;
            return false;
        }
    }
}
```

歷史有界，取樣夾在可用資料內；只有一份快照時就顯示那份位置。這份練習依賴 Shared World 每次新連線取得新 ID。自己的遊戲若復用 ID、支援重生、瞬移或 AOI 重入，還需要按生命／連續性截斷取樣歷史，見 [物件課程](../examples/modules/client/README.md#replication) 與 [Interpolation 契約](MODULES.md#interpolation)。

### 3.5 接到 session 的收包與清理

在 `ExampleSession.cs` 的 `State` 屬性旁新增：

```csharp
public RemotePositions Positions { get; }
```

在既有建構函式內，完成原本欄位指派後新增：

```csharp
Positions = new RemotePositions(netcode.Timeline);
```

把 `Pump` 中既有 `if (State.Apply(message))` 的內容改為下列內容，外面的訊息解析、錯誤處理與送輸入程式保留：

```csharp
if (State.Apply(message))
{
    if (message.type == "welcome")
    {
        Positions.Clear();
        netcode.Timeline.Reset(message.tick, now);
    }
    else netcode.Timeline.Observe(message.tick, now);
    Positions.Capture(State);
    Connecting = false;
    Status = "Connected";
}
```

`State.Apply` 會拒絕舊 tick 並驗證完整清單；驗證成功後才保存歷史。既有 `Pump` 的 `netcode.Timeline.Advance(now)` 保留，每幀推進呈現時間。

在 `Disconnect` 中的 `State.Clear()` 後新增 `Positions.Clear()`。這樣主動離線、連線錯誤、重連開始與 Dispose 都會清掉舊歷史。

這份練習沿用 Shared World 的 `Time.realtimeSinceStartupAsDouble` 秒，Reset／Observe／Advance 使用同一時間域。若改用 `Incoming.ReceivedMS`，所有相應呼叫都要一起改用 `MessageTransport.ClockMS / 1000.0`，不能把兩個時鐘的絕對值混用。

### 3.6 把取樣結果套到遠端方塊

將 `WorldPresentation.cs` 的方法簽名改為：

```csharp
public void Render(ReplicaState state, RemotePositions positions)
```

保留依最新清單建立／移除物件的程式；將原本設定 `cube.transform.localPosition` 的那一行換成：

```csharp
float x = item.Value.x, z = item.Value.z;
if (item.Key != state.LocalPlayerID &&
    positions.Sample(item.Key, out var sampleX, out var sampleZ))
{
    x = sampleX;
    z = sampleZ;
}
cube.transform.localPosition = new Vector3(x, 0.5f, z);
```

在 `ExampleClient.cs` 搜尋所有 `presentation.Render(session.State)`（Update、Reconnect、Disconnect 按鈕），全部改為：

```csharp
presentation.Render(session.State, session.Positions);
```

資料流現在是：驗證快照 → 更新最新 roster → 複製歷史 → 推進 RenderTick → 取樣遠端位置 → 更新 Transform。最新 roster 仍決定物件是否存在，插值只決定它畫在哪裡。

### 3.7 執行與完成條件

在框架根目錄啟動既有服務：

```powershell
./examples/Run.ps1 -Example SharedWorld
```

用 Unity 開啟**練習專案**的 `Assets/Scenes/SharedWorld.unity`，按 Play；另開一個 [原版瀏覽器視窗](http://127.0.0.1:18092/)，按連線、移動。瀏覽器提供第二名玩家；本次修改的效果顯示在練習專案的 Unity 畫面。根目錄 Build-Unity 腳本只認得內建專案名稱，自訂專案請用 Unity 的 Build Profiles 建置。

完成時應能確認：

- Unity 裡橘色的遠端方塊連續移動，位置稍晚於最新快照；自己的藍色方塊仍等權威回包。
- 對方離線後方塊立即移除，重新連線不會從上一名玩家的位置滑過來。
- 將 Composition 換回 `new LatestSnapshotTimeline()` 後，遠端回到最新快照的階梯移動；其他程式仍可編譯。

要比較 fixed／adaptive 與抖動，接著操作 [Interpolation 完整範例](../examples/interpolation/README.md)。其中 [InterpolatedWorld.cs](../examples/interpolation/client/Assets/Scripts/InterpolatedWorld.cs) 展示模式註冊與切換；simulation 是額外的弱網教具，基本插值不需要它。

| 現象 | 最常漏掉的接線 |
| --- | --- |
| 找不到 `Realtime.Interpolation` | 套件路徑相對於 Packages；應用 asmdef 也要引用 assembly |
| 換 FixedTimeline 後仍跳格 | 呈現是否真的用 RenderTick 取樣，而非仍直接畫最新 DTO |
| 新快照使舊位置跟著變 | 保存的是可變 DTO 參考；Capture 必須複製會變的資料 |
| 重連後從舊位置滑動 | Disconnect／welcome 是否同時清歷史與 Reset 時間軸 |
| 位置延後約 133 ms | 兩個快照間隔的預期緩衝；插值用呈現延後換取平順 |

移除 interpolation 時先換回 LatestSnapshotTimeline，刪掉 Composition 的 `using Realtime.Interpolation`，再移除 asmdef／manifest 的 interpolation 項目。這份 `RemotePositions` 只用 core，也可保留；要回到完整基本版則一併還原 session 與呈現的修改。

## 4. 其他 Unity 組件：安裝後接在哪裡

每列套件都要合併進 manifest，例如 `com.realtime.prediction` 對應 `file:../../../../packages/unity/prediction`；此路徑仍假設專案位於 `examples/<名稱>/client`。asmdef 引用使用下面的大小寫名稱。core、transport 與既有 Unity 模組保留。

| 套件／asmdef | 應用需要接上的呼叫與資料 | 可直接對照的接線 |
| --- | --- | --- |
| `com.realtime.prediction`／`Realtime.Prediction` | 提供 `IPredictionModel` 的 CreateState／Copy／Step；送出時 SentInput，每幀 Advance，權威快照 Reconcile。將協定 ack 映射為 Sequence／ServerTick／AppliedInputTicks | [PredictionCorrectionLesson](../examples/modules/client/PredictionCorrectionLesson.cs)、[預測範例](../examples/prediction/README.md) |
| `com.realtime.correction`／`Realtime.Correction` | 應用先採權威修正，再計算顯示偏移；用策略決定保留／衰減。每幀由應用更新偏移與 Transform | [PredictionCorrectionLesson](../examples/modules/client/PredictionCorrectionLesson.cs) |
| `com.realtime.clock`／`Realtime.Clock` | 用配對 RTT、server tick 與單調時間 Observe；需要估計時間時 Tick。RTT 單位為秒 | [ClockLesson](../examples/modules/client/ClockLesson.cs)、[連線恢復](../examples/network-recovery/README.md) |
| `com.realtime.replication`／`Realtime.Replication` | 提供 key／life／clone；驗證 envelope 後 Apply 完整清單，成功後消費 Added／Updated／Removed 建立或銷毀 GameObject | [ReplicationLesson](../examples/modules/client/ReplicationLesson.cs)、[物件範例 ReplicaState](../examples/objects-actions/client/Assets/Scripts/ReplicaState.cs) |
| `com.realtime.actions`／`Realtime.Actions` | 輸入時 Begin，回覆時 Resolve，每幀 Advance；應用依結果呈現／取消暫時效果。權威物件仍由快照建立 | [ActionsLesson](../examples/modules/client/ActionsLesson.cs)、[ActionFeedback](../examples/objects-actions/client/Assets/Scripts/ActionFeedback.cs) |
| `com.realtime.codec`／`Realtime.Codec` | envelope 驗證 → Decode → 解析還原 payload → 套用狀態；只回報成功解碼的 frame ack，缺基準要求全量 | [CodecLesson](../examples/modules/client/CodecLesson.cs)、[雙端 Codec 範例](../examples/codec/README.md) |
| `com.realtime.diagnostics`／`Realtime.Diagnostics` | 在實際收送邊界記錄 RTT／bytes 與應用量測；UI 讀取量測結果 | [DiagnosticsSimulationLesson](../examples/modules/client/DiagnosticsSimulationLesson.cs) |
| `com.realtime.simulation`／`Realtime.Simulation` | 將應用訊息交給 Enqueue／TryTake，每幀交付到期訊息；由應用區分可丟快照與只延遲的控制訊息 | [DiagnosticsSimulationLesson](../examples/modules/client/DiagnosticsSimulationLesson.cs)、[弱網範例](../examples/network-recovery/README.md) |
| `com.realtime.playback`／`Realtime.Playback` | 先驗證並讀入離線資料，Load tick 清單，每幀用 unscaled delta Advance，控制 Pause／Seek／倍速；應用自行取樣、呈現與重建 seek 基準 | [PlaybackLesson](../examples/modules/client/PlaybackLesson.cs)、[Replay 範例](../examples/replay/README.md) |

各課完整的最短呼叫與參數都在上表連結的 `.cs`。例如只練 prediction＋correction：

```powershell
./examples/modules/client/Run.ps1 -Lesson prediction-correction
```

| 能力 | 重設／離開時 | 停用時 | 接入完成的檢查 |
| --- | --- | --- | --- |
| prediction／correction | 新世界、新生命或重連以 ResetHistory 建基準，清顯示偏移 | AuthorityPrediction／SnapCorrection | 延遲下自己能立即移動，伺服器推移後仍能對齊；失焦會停止 |
| clock | 新連線 Reset，所有觀察使用同一時鐘 | SnapshotClock | 長時間沒快照時外推有界 |
| replication | 離線／換世界 Clear，呈現層也銷毀物件 | 應用自行套用完整清單 | 同 ID 換 life 會先移除舊生命再建立新生命 |
| actions | 新 session Clear，取消尚存的暫時效果 | 只在伺服器確認後呈現 | 成功／拒絕／逾時各一次；重複確認不重播 |
| codec | 新連線 Reset，丟棄舊 frame 基準與 ack | 雙端協商完整 JSON | 故意丟基準後能要求全量並恢復；不混用輸入 ack |
| diagnostics／simulation | Reset 樣本／ClearPending 佇列，處理既有暫停政策 | NoClientDiagnostics／直接交付 | 開關只影響量測或交付；welcome 等控制訊息不被當快照丟棄 |
| playback | 離開 Clear，Seek 重建離線呈現 | 不進入離線模式 | 暫停、跳轉、倍速不修改 live world |

重連完整流程另見 [Network Recovery](../examples/network-recovery/README.md)：core 的策略只安排重試，應用必須關閉舊傳輸、清除上述歷史、取得新的連線基準。手動取消後也要停止重試。

## 5. Go 組件：放進模擬與輸出流程

SDK 只有一個 Go module。以下適用於 `examples/my-game/server/go.mod`，路徑以 go.mod 所在目錄為起點：

```go
require github.com/win30221/gamenetkit/server v0.0.0

replace github.com/win30221/gamenetkit/server => ../../../server
```

在自己的入口／組裝程式 import 所需 package，例如 `github.com/win30221/gamenetkit/server/interest`。不用為每個組件建立 module，也不用把 SDK 的範例 DTO 當成自己遊戲的協定。

| import 的 package | 放在什麼位置 | 應用責任、清理與回退 | 最短程式／操作驗證 |
| --- | --- | --- | --- |
| `server/interest` | 權威步進後，為每個接收者各自 Select，再建立該接收者的完整投影 | 每個接收者一個 selector；離線釋放，重連重建。停用投影全部物件。客戶端收到範圍清單後移除離開者，重入重建取樣基準 | [interest.go](../examples/modules/server/interest.go)、[AOI 操作](../examples/interest/README.md)：兩人收到不同清單，權威世界數量不減少 |
| `server/codec` | 接收者投影 → 可選量化 → 序列化 → Encode(payload, decodedAck) → 傳輸 | 每個接收者一個 encoder；新連線 Reset。量化只改輸出副本，ack 必須來自對端成功解碼。停用時同步改雙端完整 JSON | [codec.go](../examples/modules/server/codec.go)、[Codec 操作](../examples/codec/README.md)：缺基準回全量，解碼結果一致 |
| `server/history` | 模擬 owner 完成 tick 後 Add pose 副本，需要查詢時 At | 提供深複製與有界容量；新比賽 Clear。停用不保存；歷史查詢不修改當下 world | [history.go](../examples/modules/server/history.go)、[歷史操作](../examples/history-lagcomp/README.md)：查過去位置而 live 位置不變 |
| `server/lagcomp` | 驗證身份／動作與時間 → Target 授權時間窗 → history.At → 應用命中幾何 | 不自行保存歷史或計算傷害；生命不符、過舊時間不能當合法命中。停用使用當下判定 | [history.go](../examples/modules/server/history.go)、[回溯操作](../examples/history-lagcomp/README.md)：超出時間窗被拒絕 |
| `server/replay` | 模擬 owner 套用事件後 Append，結束 Close；離線 VerifyFile 成功後再重播 | 應用定義事件／規則／hash，處理寫入失敗與不完整錄製；新錄製建立新 journal。停用不建 journal，隔離 live world | [replay.go](../examples/modules/server/replay.go)、[Replay 操作](../examples/replay/README.md)：完整驗證後才能播放，seek 不動即時世界 |

例如只學 AOI：

```powershell
./examples/modules/server/Run.ps1 -Lesson interest
```

同時加入 AOI 與 codec 時，先為每個接收者篩選，再編碼各自的投影；不能把另一人的 encoder 基準拿來使用。需要錄製完整世界時，從權威資料錄製，避免誤錄成某位玩家的 AOI 投影。

完成一項能力後，再去 [Rumblebound](../examples/rumblebound/README.md) 看多組件如何組裝。全部課程的回歸可用 `./examples/modules/Test.ps1`；自己的 Unity 專案仍要另外編譯並操作，終端課程通過只證明純資料示範。
