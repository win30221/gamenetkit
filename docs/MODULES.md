# Modules

本頁是 Unity/Go 套件、可替換契約及應用組裝的統一索引。責任與 reset 邊界見 [ARCHITECTURE.md](ARCHITECTURE.md)；實際操作選項見 [Rumblebound example](../examples/rumblebound/README.md)，錄製/播放見 [REPLAY.md](REPLAY.md)。下文會區分 **SDK 實作上限**、**可配置參數**與 **sample 組裝值**，不要把遊戲預設視為所有使用者的固定限制。

## Package Index

第一次加入組件，先讀 [組件接入指南](COMPONENT_GUIDE.md)：從 Shared World 加入插值，並依需求找到其他組件的安裝、呼叫與清理位置。想單獨練 API，從 [組件課程](../examples/modules/README.md) 選一項執行；本頁保留完整契約與限制。core／transport 的基本接線見 [Minimal](GETTING_STARTED.md)，多人生命週期見 [Shared World](../examples/shared-world/README.md)。

Unity 共 14 個套件：core、transport、十個同步選用套件，以及選用 protobuf adapter／官方 runtime。十個同步選用套件各自只依賴 core，彼此沒有套件相依；transport 的 package manifest 無 core 相依。protobuf 依賴 transport 與 protobuf-runtime，core／transport 不反向依賴 Protobuf。套件皆以 `packages/unity/<目錄>` 提供本機 UPM 來源。

| Unity 套件 | 公開入口 | 應用供應 / 停用或替代路徑 |
| --- | --- | --- |
| `com.realtime.core` | `IPresentationTimeline`、`TimelineCatalog`、`InputHistory<T>`、預測/修正及 advanced 契約、`SessionState`、`ReconnectPolicy` | 基本契約與策略；應用供應 DTO、規則、時間與身份 API。內建 latest、authority、snap、snapshot、無量測策略 |
| `com.realtime.transport` | `MessageTransport`、`Incoming` | URL、hello、平台回呼目標與 codec；替換時由應用保留自己的收送/生命週期適配 |
| `com.realtime.interpolation` | `FixedTimeline`、`SnapshotInterpolation` | tick/snapshot rate、單調時間；停用改 core `LatestSnapshotTimeline` |
| `com.realtime.prediction` | `ReplayPrediction<TState,TInput>` | `IPredictionModel`、確認語意、timeout；停用改 core `AuthorityPrediction` |
| `com.realtime.correction` | `ExponentialCorrection` | 誤差距離、forceSnap 與應用自己的顯示偏移；停用改 core `SnapCorrection` |
| `com.realtime.clock` | `DisciplinedClock` / `IServerClock` | server tick、RTT、單調時間；停用改 core `SnapshotClock` |
| `com.realtime.replication` | `ReplicaStore<T>` / `IReplicaStore<T>` | key、life、clone、完整清單；停用由應用直接套用清單 |
| `com.realtime.actions` | `ActionPresentation` / `IActionPresentation` | action ID、時間及確認/拒絕事件；停用只呈現權威確認 |
| `com.realtime.codec` | `FrameDecoder` / `IFrameDecoder` | frame envelope 驗證、ack 回傳；停用協商完整 JSON |
| `com.realtime.diagnostics` | `ClientDiagnostics` / `IClientDiagnostics` | 同一單調時鐘、應用量測值與流量；停用改 core `NoClientDiagnostics` |
| `com.realtime.simulation` | `MessageConditioner` / `IMessageConditioner` | 訊息分類、Pump、連線清理；停用走直接傳輸 |
| `com.realtime.playback` | `PlaybackClock` / `IPlaybackClock` | 有界 tick 清單與獨立播放控制；移除播放入口，不影響即時同步 |
| `com.realtime.protobuf` | `ProtobufSession<TSend,TReceive>`、`Realtime.Protobuf` assembly | 官方 message／parser 與 binary transport 的薄適配；遊戲供應 envelope 與驗證規則，owner 呼叫 Pump |
| `com.realtime.protobuf-runtime` | `Google.Protobuf.dll` | 官方 .NET Standard managed runtime；產生碼與 adapter 使用，沒有遊戲 schema；要求 Unity .NET Standard 2.1 profile |

Go module 為 `github.com/win30221/gamenetkit/server`，共有九個 library package：loop、transport、netcore 與六個選用 package。跨語言 codec 測試資料產生器位於 [tests/fixtures/generate-codec](../tests/fixtures/README.md)，由測試目錄維護。

| Go package | 入口 / SDK 內相依 | 應用供應 / 停用或替代路徑 |
| --- | --- | --- |
| `server/loop` | `Run[T]`、`Options[T]`；無其他 SDK 相依 | 有界命令 channel、間隔/預算、Apply/Step、停止與量測 callback；可替換成應用自己的單 owner 迴圈 |
| `server/transport` | `Wire`、`WebSocket`、`Limits`、`WriteLine`、`BoundedDatagram`；無其他 SDK 相依 | hello/input 驗證、HTTP/QUIC 接納、身份與 Origin 政策；其他傳輸實作 Wire |
| `server/netcore` | `History[T]`、`RewindPolicy`、`Interest`、`FrameEncoder`、`ReplaySink` 等契約 | 應用 DTO 與組裝；契約層不引用遊戲或具體選用實作 |
| `server/history` | `Buffer[T]`、`New` → netcore | clone、容量及保存內容；停用不記歷史 |
| `server/lagcomp` | `Bounded.Target`；無其他 SDK 相依，以 netcore 契約接入 | MaxTicks、命中幾何與合格歷史檢查；停用使用當下判定 |
| `server/interest` | `Radius`、`New` → netcore | 半徑、遲滯、容量及 Always 分類；停用投影全部物件 |
| `server/codec` | `Encoder`、`Decode`、量化 helper → netcore | payload、已確認基準、envelope 與量化投影；停用完整未量化 JSON |
| `server/replay` | 有界 journal writer/reader → netcore | 初始定義、事件、state hash 與重播規則；停用不錄製 |
| `server/protobuf` | `NewPeer[In,Out]`、`Peer.Read/Write` → transport、官方 proto runtime | inbound factory、生成 envelope、應用 oneof／語意驗證；選用，不讓 loop／netcore 依賴遊戲 schema |

## Composition

Protobuf 接入見 [獨立範例](../examples/protobuf/README.md)與[生成工具](../tools/PROTOBUF.md)：一份 `.proto` 產生兩端型別，生成物與 runtime 已提交；只有修改 schema 才需工具鏈。第一版目標是原生／WebGL WebSocket binary，binary WebTransport 尚未實作；Unity IL2CPP／WebGL 的實際驗收狀態以範例報告為準。既有 FrameDecoder／MessageConditioner 是文字介面，不能直接用於這條 binary 路徑。

Unity 用專案 `Packages/manifest.json` 加入 `file:` 套件；路徑相對於 `Packages` 目錄。先安裝 core/transport，依需求加入選用套件，在應用組裝入口註冊具體型別；使用端持有 core 介面。Go 應用以 `require github.com/win30221/gamenetkit/server` 與本機 `replace` 引用 SDK，然後在自己的組裝程式注入 netcore 實作。

Rumblebound 的 `BattleNetcodeModules.cs` 集中選用 Unity 具體型別；`BattleSettings` 保存選項。同責任用單選，其他能力獨立開關。sample 的跨組限制是本機平滑需要預測、近戰補償需要歷史。SDK 的 correction 套件本身不依賴 prediction，lagcomp 套件也不負責保存歷史。

sample 透過 hello 協商 history/lag/codec/interest/quantize，`/api/config.netFeatures` 公告伺服器能力，未支援的明確要求會被拒絕；輸入不能中途換選項。舊客戶端未傳 net 選項時，伺服器保留完整 JSON/全部物件。sample 目前預設 disciplined clock、lifecycle replica，攻擊仍 confirmed、JSON、all、未量化，歷史與補償關閉。這些不是 SDK 強制組合。

設定在**新連線**生效，自動重連沿用原組合。加入或替換模組時，同時處理工廠註冊、能力協商、預設/fallback 與 reset；移除時先退回替代策略，再移除註冊與套件。傷害、碰撞、tickRate、快照頻率不因 sample 模組選項改變；範圍/編碼改的是傳輸投影。

## Core

`TimelineCatalog` 預裝 `latest`，拒絕重複 ID，Create 時檢查實例的 `Mode` 與註冊 ID 一致。應用可以註冊自己的 `IPresentationTimeline`，不需改遊戲模擬；sample 展示選單可列出註冊 ID，顯示名稱由應用翻譯。

`InputHistory<TInput>` 保存輸入區間與 sequence→送出時間，沒有遊戲座標或規則。`SessionState` 只保存 credential/userID/revision，不驗證身份；成功 Accept 與每次 Clear 都增加 revision，應用用它隔離過期 HTTP 完成。`ReconnectPolicy` 只處理時間窗口與退避：預設 30 秒窗口、0.5 秒初始、3 秒最大延遲，可由建構參數修改。是否可重試、重新取得 ticket、重建連線由應用決定。

## Transport

`MessageTransport` 擁有 socket、取消、epoch、有界接收 framing、送出序列化與抵達時間，不擁有場景/UI。`BeginAttempt()` 會結束舊生命週期；Open/Receive 傳入該 attempt，`TryRead` 只交付目前 epoch 的內容；結束時 Close/Dispose。原生端提供文字 WebSocket；可設定 `NativeWebTransportExecutable` 啟用選用的桌面 WT helper，未設定時 WT 明確拋出不支援。配置與平台限制見 [原生 WT](../native/webtransport/README.md)。`TrySend(json)` 回傳 `Task<bool>`：忙碌、未開啟、已關閉或實際寫入失敗為 false；不建立等待佇列，應用須保留需重試的動作及合併過期的移動輸入。相容入口 `Send(json)` 對 C# 已知的忙碌／未連線同步拋出例外，避免 fire-and-forget 呼叫留下未觀察的 faulted Task；接受送出後的 I/O 錯誤仍由 `TryRead` 交付 `send_failed`。兩個入口對非法／超限文字都會拋出例外。原生端的 true 只表示目前連線的本機寫入已完成；WebGL 的 true 只表示 bridge 已接收提交，後續 JS 回傳 false 或寫入失敗會交付一次 `send_failed` 並結束該次連線，載入中的 bridge 無連線則立即拒收但保留連線嘗試，ready 後可重試；`Send` 對這項同步拒收亦會拋出例外。方法完成均不保證對端收到或已套用；提交、輸入 ack 與動作確認必須分開處理。

傳輸文字的上限統一為 UTF-8 32 KiB（`MessageTransport.MaxMessageBytes`／JS `maxMessageBytes`），涵蓋原生 WS 重組接收、公開 Receive、Send／hello，以及 JS 的 WS、WebTransport datagram 與可靠行／未完成片段。超限送出明確失敗，非法接收結束連線；原生及 WT 拒絕無效 UTF-8，WS 拒絕非文字內容。Codec 的原始 payload 仍限 16 KiB，外層 Base64／envelope 可使用較大的 wire budget。共用接收佇列在已有 64 份時拒絕再收，原生連線期限為 8 秒。應用仍另設訊息格式、整體逾時與更嚴格的伺服器 read limit，例如教學服務的 1 KiB 輸入限制。

`Incoming.ReceivedMS` 使用 `MessageTransport.ClockMS` 的 Stopwatch 毫秒原點。原生抵達時間不必等待下一個 Unity 畫面幀；WebGL 回呼的時間仍受瀏覽器/Unity bridge 排程影響。不可把這個絕對時間直接與 Unity realtime 絕對值相減；同一量測必須用同一時鐘。

WebGL 需要 `.jslib` 及 `packages/unity/transport/Browser/transport.js`，`BrowserRuntimeURL` 預設動態載入 `/transport.js`，可在 Open 前設定相對於頁面 base URI 的子路徑或完整模組 URL；Minimal、Shared World、sample 與重製遊戲的伺服器都提供此路徑。回呼方法必須是 `OnTransportMessage(string)`，只交給 `MessageTransport.Receive`，再由 owner `TryRead` 消費；sample 保留 `[Preserve]` 避免 IL2CPP 移除方法。直接呼叫遊戲 `OnWire` 會繞過量測/弱網路徑。

JS `createTransport({kind,url,hello,hash,receive,closed,clock,sent})` 收送原始文字，不解析、過濾或改寫 JSON。`clock` 預設為 `performance.now()`；`sent(data, atMS)` 在呼叫平台寫入前通知提交時間（不含內部 hello，不保證寫入成功或對端收取），`receive(data, atMS)` 提供抵達時間。單參數 receive callback 仍相容。

`send(data)` 在 WS 未開啟或 bufferedAmount ≥ 4096、WT 忙碌時回傳 false。WT 可用 `send(data, {waitForSlot:true})` 等候一個位置，超出時仍回傳 false；close 立即釋放正在寫入及等候的呼叫，即使平台寫入未完成。WebGL bridge 對所有內容啟用這個有界等待位置，沒有 probe 特例；C# `Send` 完成仍不代表 JS 實際送達，也沒有新增跨 bridge 的送出確認。

Rumblebound 的應用層 `ClientTransport` 負責 probe 身份、世代與回音量測；SDK 傳輸只處理訊息收送。RTT 使用實際送出及接收時間，WebGL 仍包含 bridge 排程時間，不能視為伺服器模擬延遲。

sample 的 Node 入口只 re-export SDK；HTTP handler 從框架來源樹載入 SDK 原檔，部署需保留該檔案。`.jslib` 為每個 C# MessageTransport 配置獨立 handle／epoch／socket，上限 64 個保留 handle；Close 只關閉自己，Dispose 釋放 handle。每個實例需不同的 Unity callback target，避免應用自行把兩條訊息送到同一接收器。舊版 RB_Connect／RB_Send／RB_Close ABI 保留在獨立 handle 0。它仍是 Unity WebGL 支援檔，未作為一般 JavaScript 遊戲 SDK 發佈。

Go `Wire` 負責 ReadHello/ReadInput/Write/Reject/Close；WebSocket 只接受文字，忽略 Write 的可靠旗標（WS 本來可靠）。`Limits.Reliable` 依應用傳入的 datagram/snapshot budget 決定升級可靠路徑或拒絕超大內容。`WriteLine` 為 stream 設 deadline、附換行並檢查短寫；`BoundedDatagram` 將 context 取消連到 abort。sample 的 WebTransport/QUIC 接納仍在應用層。身份、Origin allowlist、ticket、match/generation、loaded barrier 與命中驗證也由應用管理。

## Interpolation

時間軸只提供 `RenderTick` 與 buffer/arrival/jitter/underrun/resync 指標；應用必須保存自己的快照、取樣 DTO、處理生命切換與場景物件。`FixedTimeline` 用固定緩衝，`SnapshotInterpolation` 根據到達觀察調整；即時呈現不超過最新收到的快照。`latest` 直接使用最新 tick，不建立插值套件。

core 的 `SnapshotBuffer<T>` 提供有界環形快照容器與可篩選的前後幀查詢，重複／倒退 tick 不加入；`Clear` 重設時間域。`TryBracket` 由 selector 選取實體，`first` 可排除舊生命樣本，`Fraction` 限在 0..1，最後一幀之後不外推。它保存參考，不自動複製或修改 DTO；生命、碰撞、瞬移與投射物權威終點仍由應用處理。Rumblebound 的角色與投射物共用這個查詢，移除 interpolation 套件後仍能使用 core 容器。

```csharp
var catalog = new Realtime.Core.TimelineCatalog();
catalog.Register("adaptive", (rate, snapshots) =>
    new Realtime.Interpolation.SnapshotInterpolation(rate, snapshots));
var timeline = catalog.Create("adaptive", tickRate: 30, snapshotRate: 15);
timeline.Reset(firstServerTick, now);
// 收到新快照：timeline.Observe(tick, now)，另存自己的快照。
// 每幀：timeline.Advance(now)，以 RenderTick 取樣自己的資料。
```

上述頻率只是例子。新 baseline 時 Reset，正常快照 Observe，每幀 Advance；需要共同時間時透過 `IClockAwareTimeline.SourceClock` 注入，不要求其他時間軸一定實作此擴充介面。

## Prediction

`IPredictionModel<TState,TInput>` 由應用提供。`TState` 是可變 class：`CreateState()` 每次建立獨立 buffer；`Copy(source,destination)` 深複製所有影響模擬的可變資料，包括陣列元素；`Step(state,input,seconds,serverTick)` 只更動傳入狀態，不回寫權威 DTO、其他預測器或外部 world。`TInput` 在歷史中直接保存，沒有 clone callback，因此應用應使用值型別/不可變輸入，或在記錄前自行複製。

```csharp
var predictor = new Realtime.Prediction.ReplayPrediction<MyState, MyInput>(
    model, tickRate: 30, timeoutSeconds: 0.2, fixedStep: true);
predictor.Reconcile(authority, new Realtime.Core.PredictionAck {
    ResetHistory = true, ServerTick = serverTick
}, now);
predictor.SentInput(sequence, now); // 實際送出時記錄
predictor.Advance(now, heldInput);  // 每畫面幀
// State 是只供讀取的畫面預覽；外部不要修改內容。
```

後續 Reconcile 傳入 `PredictionAck.Sequence`、`ServerTick`、`AppliedInputTicks`。重播錨點為該序號的送出時間加 `max(0, AppliedInputTicks)/tickRate`；找不到已確認送出記錄時直接採權威基準，不憑空重播。`AppliedInputTicks` 的意思由遊戲協定映射；Rumblebound 以 snapshot.tick/inputTick 與 input timeout 限制換算，不可直接假設另一款遊戲相同。加入、新生命或世代基準切換時由應用設定 `ResetHistory=true`。

`fixedStep=false` 使用幀時間步進；`true` 以 `1/tickRate` 提交模擬，另複製成獨立 buffer 計算不足一 tick 的畫面預覽，預覽不寫回提交狀態。固定模式依 tick 起點所在輸入區間取值，幀內轉向/放開可能延至下一 tick。

固定步重播還要求 ack 錨點至目前時間有連續保留的輸入歷史。長幀裁切、歷史過期、內部缺口或缺少尾段時，採用當次權威基準並從目前時間繼續預測，不用最後輸入補算未知時間。這不改變 held-state 的取樣契約；伺服器合併同 tick 多筆輸入與畫面逐幀取樣仍可能不同，不能以鎖住發包頻率或更換預測模式保證完全一致。

目前實作每幀最多模擬 50 ms，輸入歷史最多一秒/240 幀、120 份送出記錄，固定步重播預算為 `ceil(tickRate)+2`；超過可配置的快照 timeout 時停止預測。`PredictionMetrics` 分開記錄 committed/replayed/preview steps、committed tick 與 dropped seconds。這些邊界避免無限制追趕，並不保證任意長斷線仍可重播。

這不是整個多人世界的鎖步或 rollback。遠端未收到輸入、伺服器收包時間、傷害與戰鬥仍可造成差異，需要權威確認與修正。sample 的 MotionModel/地圖/推擠/衝刺與停步由遊戲供應。純數值與陣列狀態測試不引用 Rumblebound。

## Correction

`ICorrectionPolicy` 只回覆是否保留顯示偏移與衰減係數；權威狀態立即採用，座標/Transform 仍由應用持有。`SnapCorrection` 直接校正，`ExponentialCorrection` 對偏移做指數衰減，強制重設或大誤差直接校正。sample 在復活、攻擊停步等情況 forceSnap，並自行維護遠端接觸安全處理。關掉平滑不等於移除所有呈現/碰撞安全處理。

`GameMovement` 的遠端接觸偏移使用獨立 `remoteContactSmoothingSeconds` 建構參數（預設常數 0.08 秒，有限值 0..1，0 為立即收斂），不跟隨「自己的移動：校正」選項。換 entity／life 時重設遠端接觸狀態；即時收斂仍須通過場地與人物不重疊檢查。

## Clock

`IServerClock.Observe(serverTick, now, roundTripSeconds)` 使用同一單調秒數。`SnapshotClock` 保存最新已觀察 tick；`DisciplinedClock` 用 RTT 中點估計、有限幅度修正、讀值不倒退，最多外推至最新 server tick 後半秒；無效值、倒退 tick 或超出 0..5 秒的 RTT 不採計。雙向延遲不對稱會產生誤差，不能當作精確單向延遲或反作弊證明。

core 的 `ReplyTracker` 負責有界 ID／送出時間配對、一次性回覆、過期清除及 cumulative ack 之前的淘汰；只對明確配對的 ack 產生耗時，不替被跳過的輸入推估延遲。`ProbeScheduler` 在這個配對器上加入固定間隔與遞增 ID。兩者使用同一單調秒數，重連時 Clear；不定義 ping JSON、身份驗證或傳輸。Rumblebound 用它們追蹤 probe 與輸入回覆。

sample 在 welcome/start Reset；pong 的 `clockValid` 區分有效 tick 0 與沒有時鐘資料。伺服器 echo tick 由 atomic 讀取，不經模擬命令佇列。插值可透過 `IClockAwareTimeline` 使用同一估計，但自身仍量測到達與緩衝，呈現不超過收到的最新快照。

sample 的攻擊新鮮度時間被限制在最新收到 tick 到其後兩 tick，避免伺服器暫停時估計值變成不合法未來；目標回溯使用 render tick，另受伺服器時間窗限制。本機預測則用自己的單調時間歷史，不改成 server clock 驅動。

## Replication

可操作的新增／移除／同 ID 重生，以及下一節的動作確認，見 [物件同步與動作確認教學](../examples/objects-actions/README.md)（啟動選單 5）。

`ReplicaStore<T>` 套用**完整物件清單**，不是欄位 patch。應用供應 key、life 與深複製 callback；預設容量 1024，可由建構參數改。先驗證整份清單的重複/空 ID、容量、generation/tick，再一次替換狀態；clone 必須建立不同實例並深複製可變內容。

同世代只接受嚴格前進 tick；較舊世代拒絕，新世代重設清單基準。life 改變產生 removed+added，相同 life 產生 updated。成功 Apply 才重設變更清單；無效 Apply 回傳 false 並保留原狀態及上一批變更清單，呼叫者不可在失敗後重複消費。`Clear` 清內容、變更與世代/tick 基準。

`Values` 是唯讀字典介面，但元素仍是可變物件，讀取者不得修改 store 擁有的內容。sample 用 playerID 作 key、entityID+life 作生命標記；added/removed 統計包含 welcome/start 重設，不能當作實際 Instantiate/Destroy 次數。建立、停用、回收與重設場景物件仍是應用責任。

完整 example 在每次成功 Apply 時同步發出 `ReplicasApplied`，`ActorRoster` 消費三種變更並保留同生命的 view；不要把變更清單留到下一個畫面幀才讀，因為一個 Pump 可能套用多份快照。同 tick start/result、初次綁定、回放與移除 replication 套件時使用快照清單備援；Unity 只在 roster 改變時協調場景物件。

## Actions

`ActionPresentation` 是起手呈現 ledger：Begin 以 ID 預約，Resolve 確認/拒絕，Advance 處理等待與去重期限，Clear 重設。預設容量 128、等待一秒、保留四秒；應用仍需決定如何播放/停止表現。

sample 把 action ID 帶入玩家最近攻擊、近戰、箭矢及範圍技能。speculative 只立即播放動畫/音效/起手特效，不改 HP、冷卻、位置、命中或勝負。保留窗口內同 ID 不重播，確認/拒絕可以重複到達；拒絕停止尚未完成的預測表現，已確認效果不受影響，已播出的聲音無法倒轉。特效池與八聲道是 sample 的呈現配置。

輸入重送期限由應用另管；本機停止重送不等於伺服器拒絕，晚到確認仍可處理。confirmed 替代路徑只在權威確認時播放，不需 actions 套件。

完整遊戲仍逐筆等待動作確認；同一 tick 的 held movement 也仍採最後收到的方向。動作結算後，伺服器會在該次 Step 結束加發擁有者的正常 snapshot，避免再等定期廣播；正常廣播 tick 不重複傳送，未讀 start 優先保留，掉包仍由後續快照帶回持續的 ActionAck。客戶端收到確認後，下一筆已排隊動作立即具備送出資格，但仍遵守 60Hz 上限。30Hz／15Hz 時這可省 0～33.3ms 的伺服器排程等待，並未消除 RTT 或改成多筆動作同時在途。

完整 example 以同一 ledger 查詢是否已預先呈現，不再另存 Unity preview ID 集合。尚待確認的命令在 ledger 保留窗口之外仍保持去重；第一次晚到 ack 會重新登錄已被淘汰的條目再 Resolve，保留確認統計。視覺 cue 的短生命期仍由呈現層決定，與命令重送／權威拒絕分開。

## History and Lag Compensation

操作教學見 [歷史位置與延遲補償](../examples/history-lagcomp/README.md)（選單 9）。`Buffer.Bounds()` 回報實際保留的 first／last tick 與是否有資料，避免應用自行猜測間隔或重建另一份索引。

Go `history.New[T](capacity, clone)` 的容量可設 2..4096；寫入與查詢都 clone。Add 拒絕舊 tick、替換相同 tick，滿時移除最舊。At 只在現有範圍內回傳 before/after bracket，不外推，也不自動內插。保存內容、clone 完整性與內插規則都由應用負責。

`lagcomp.Bounded{MaxTicks: ...}.Target(now,requested,observed)` 只驗證時間請求：不得要求 observed 之後、observed 不得在 now 之後、不得超出 MaxTicks；失敗回傳 now/false。它不保存歷史、不選命中幾何、不信任 RTT，也不修改 world、傷害或免疫。

Rumblebound 組裝值為 32 份碰撞 pose，近戰回溯時間上限是 `maxMeleeRewind = 200ms`，依實際 TickRate 向下取完整 tick（30Hz 為六 tick，60Hz 為十二 tick），不超出時間預算。近戰保留目前攻擊者位置與靜態地形，只查目標歷史位置；資料缺失或不合格改用當下判定。歷史目標必須存活，且 entity/life/connection generation 與當下相符；當下 HP、隊伍、保護與冷卻仍照常檢查。歷史只保存碰撞 pose，沒有完整世界 rollback。

箭矢收到操作後才生成，飛行/碰撞用當下狀態；範圍技能的起手/落下/命中也不回溯。`tech.rewindTick`/`rewindUsed` 保留最近一次已接受近戰的查詢與合格歷史結果，冷卻拒絕不覆寫。整合測試指定時間窗內的歷史 tick 以確保路徑執行；一般遊玩以 render tick 請求，緩衝加傳輸超過窗口時使用當下判定。窗口是有界查詢政策，不是完整反作弊時間證明。

## Codec Wire

操作教學見 [Codec 與座標量化](../examples/codec/README.md)（選單 8），包含實際 envelope 大小與缺基準恢復。

codec 處理 opaque bytes；應用 envelope 與 DTO 身份驗證仍在應用。Rumblebound wire v1 的 frame envelope 包含 type、protocol/content/matchID/generation/tick、id/base/data；data 是 Base64 二進位 payload：

| base / opcode | 內容 |
| --- | --- |
| `base = 0` | 完整 UTF-8 JSON bytes |
| `base > 0`，opcode `0` | `uint16 offset` + `uint16 length`，複製已確認基準片段 |
| `base > 0`，opcode `1` | `uint16 length` + literal bytes |

所有 uint16 採 little-endian。編碼器每個八 byte 鍵最多索引八個候選片段；差分需比完整內容節省額外預算才採用，否則 keyframe。兩端各保留最多 64 份、每份最多 16 KiB 的內容，這是目前 codec 實作上限。

客戶端只 ack 成功解碼的 frame，伺服器只引用已確認且仍有 cache 的基準；sample 輸入 seq 未前進時不能回退伺服器 frame ack。遺失中間快照不必破壞下一份解碼。缺基準回傳 `MissingBaseline` 並以 ack 0 要求完整內容，decoder 高水位仍阻止舊 frame 重播；新連線 Reset 兩端 cache。

解碼檢查 ID/基準/容量/指令/UTF-8；sample 另檢查內外 protocol/content/match/generation/tick 一致。完整 envelope 因 Base64 超過 16 KiB 時，sample 改送原始完整快照；未實際送出的 frame ID 不會被客戶端確認。轉場/結果維持可靠控制。欄位排列、變化幅度及 keyframe 開銷影響壓縮率，不保證每包比 JSON 小。

量化是 Go codec 的獨立 helper，不要求 delta。sample 只量化投影副本中的玩家/飛行彈體 X/Z 至 0.001 單位，單座標誤差至多約 0.0005；若玩家量化後不符合地圖淨空則保留原值。終點、命中、技能與 domain state 保留原精度。量化間距/範圍屬應用策略。

## Interest

實際操作見 [AOI 興趣範圍同步教學](../examples/interest/README.md)（選單 7）：比較全部／範圍清單、每位玩家的進出邊界，以及接收物件與權威物件的數量。

`Radius` 每接收者保存自己的 visible 集合；New 接受正半徑、非負 Margin 與物件 Limit。新物件進 Enter 半徑納入，已納入物件用 Enter+Margin 判斷退出，Always 直接保留。它只回傳 ID 集合，投影建立、事件規則及場景回收由應用負責。

候選數超過 Limit 時，先保留全部 Always，再從符合進出半徑的其餘物件依距離由近至遠補到 Limit；距離相同以 ID 字典序決定，不受輸入排列影響。Always 本身超過 Limit 時仍全部保留，因此 Limit 是一般情況的成員預算，並非 Always 的硬上限。容量淘汰後重新進入須再符合 Enter 半徑；候選數未超量時保留原本半徑與遲滯行為。

sample 的半徑 UI 限 4..40 單位，退出 Margin 為兩單位；自己、自己擁有的箭矢/技能，以及與自己相關的終點/近戰事件保留。全域比分/勝負不過濾，隊伍顏色由完整隊伍比分維持。離開範圍移除、重新進入重建；只過濾 per-recipient 投影，不改 world 或其他連線。

這是距離與頻寬策略，不提供遮蔽物/戰爭迷霧。可實作 `netcore.Interest` 替換成其他幾何/隊伍規則；評估半徑時需考慮遊戲視野與物件速度。

## Diagnostics

`IClientDiagnostics` 位於 core；具體套件只依 core，不要求 transport/預測/插值。由單一 owner 以同一單調時鐘提交已確認輸入、回音、位置誤差等值。每類最多 512 個/十秒樣本，負值與 NaN/Infinity 不採計；p95 用 nearest-rank，偶數中位數取中間兩值平均。低樣本 p95 只是當次排序結果。

完整 Rumblebound example 已串接六種指標：`FrameMS` 為 Pump 間隔；`ConnectionRttMS`、`InputReplyMS` 從提交至應用調理鏈路到驗證後交付，包含模擬延遲與主迴圈排程；`CorrectionDistance` 在接受權威狀態後採樣；`SnapshotIntervalMS` 為同步層收到有效狀態的交付間隔；`ClientStepMS` 合計同一 Pump 內兩次 `GameMovement.Advance` 的 Stopwatch 耗時，不包含等待、完整渲染或 GPU。重複 ack 不增加輸入回覆樣本。

畫面幀耗時是實際幀間隔，含排程/等待，不是 GPU 時間。sample 的本機同步耗時包住 `GameMovement.Advance`，包含預測與呈現狀態更新，不是 server tick 或完整渲染。位置單位 u 是遊戲世界單位。

流量以傳輸送出內容與讀到的接收內容計 UTF-8 bytes；不是實際線路 bytes 或對端確認送達量，上行寫入失敗仍可能計入提交量。最近一秒速率最多保存 2048 訊息，總量另存計數；不含內部 hello、HTTP、TLS/QUIC/WS header。sample 關閉顯示時不採樣/不發 probe，重連或重新啟用時清樣本；原生 pong RTT 在共用接收抵達時計算，弱網路徑則包含訊息交付延遲。

## Message Simulation

`MessageConditioner` 只依 core，以應用訊息為單位模擬雙向延遲/抖動/選擇性遺失/暫停。delay/jitter 各限 0..1 秒、loss 0..1；每方向容量預設 128、可設 1..1024 訊息；每訊息上限固定 32 KiB（`MessageConditioner.MaxMessageBytes`，容納傳輸 envelope），超限明確回傳拒絕，sample 轉成 network_error。延遲為 `max(0, delay + uniform(-jitter,+jitter))`，每方向維持 FIFO 到期時間，不因 jitter 重排可靠訊息。

兩方向使用獨立 PRNG；相同種子、訊息分類及觀察時間序列才會重現相同擾動。`Pause(now,seconds)` 最長十秒，暫停雙向交付；期間可丟訊息直接遺失，控制訊息有界等待。`ClearPending` 清舊連線佇列但保留尚未到期的 Pause，避免重連繞過斷流；手動新組合建立新實例。

完整 example 各建立一個上行／下行 conditioner，獨立設定 delay、jitter、loss；舊 `BattleSettings.DelaySeconds` 仍只代表收件延遲。輸入可丟棄（離散動作會沿用 ID 重送），loaded/ping 等控制訊息只延遲；下行 snapshot 可丟棄，welcome 直接建立基準，start/result/pong 只延遲。送出只在 transport 空閒時從有界佇列取出，重連／離線／結算會清空。驗證到可靠結算後，即使它仍在下行延遲中，也會停止上行交付，避免向已關閉的連線繼續送資料。

sample 把 input/snapshot/frame 標為可遺失，welcome/start/pong/error 等只延遲。應用必須定期 Pump/TryTake，實際交付包含主迴圈粒度。它不操作網卡或 TCP/QUIC 重傳、不模擬 HTTP/握手或封包重排；UI 顯示的是每方向的應用訊息層擾動。

## Replay and Playback

入門操作見 [錄製與播放 Replay 教學](../examples/replay/README.md)（選單 6）：錄製完整權威快照，完成檔案驗證後用獨立播放時鐘呈現。

Go `netcore.ReplaySink` 要求 Append 複製資料且不可因磁碟 I/O 阻塞模擬；失敗使整份錄製不完整，不能把成功前綴當完整檔。遊戲供應初始定義、事件套用與完整狀態 hash，SDK 只做有界 journal、完成與完整性檢查。Unity `IPlaybackClock`/`PlaybackClock` 保存有界 tick 清單、倍速/暫停/跳轉與獨立時間；遊戲負責檔案格式、呈現、seek 重建與即時狀態隔離。具體格式、指令、移除方式及限制統一見 [REPLAY.md](REPLAY.md)。

Journal 在 Append 時序列化以取得獨立 payload，佇列同時保存已編碼 bytes、Tick 與 Kind；背景寫入直接使用這些中繼資料更新摘要，不再反序列化自己剛編碼的事件。

## Removal and Validation

Unity 移除選用套件時，同時移除應用註冊、`Packages/manifest.json` 相依與應用 `.asmdef` 中的組件參考，再改選本頁表格的 fallback。完整 example 的註冊在 `BattleNetcodeModules.cs`，`BEGIN OPTIONAL <名稱>` 區塊是該套件的具體型別接線。替換實作時保留介面／模式 ID 與 reset 邊界。core／transport 是目前最小範例基礎。

共用遊戲 `examples/rumblebound/game/sim/net_module_{history,lagcomp,interest,codec,replay}.go` 受對應 `!rf_no_*` build tag 控制；移走目錄時同步使用排除 tag，netcore 契約不需改。`Test-ServerRemoval.ps1` 暫移目錄後測試／vet 並還原；Unity 的 `Test-OptionalRemoval.ps1 -Module all` 驗證新 example 的實際建置及 DLL，預設驗證目前的 RumbleboundExample。工具暫時修改共用來源，須序列執行，結束後自動還原；日常使用不需執行。

可執行命令、前提及歷史驗證/本輪驗證的區分見 [VALIDATION.md](VALIDATION.md)；目前整合驗證入口為 `examples/rumblebound/scripts/Test-Battle.ps1` 與 `examples/rumblebound/scripts/Test-BattlePlayer.ps1`。本頁不以既有日誌宣稱本輪 Unity 建置已通過。
