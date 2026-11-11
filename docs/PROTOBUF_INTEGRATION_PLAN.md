# Protobuf 與型別化收送接入計畫

狀態：第一版二進位 WebSocket 與型別化收送已接入。採用官方 Protobuf 產生器與 runtime，Go＋Unity 共用一份訊息定義；GameNetKit 負責有界傳輸、型別化編解碼與交付。本文保留設計與階段驗收標準；實際操作見 [範例](../examples/protobuf/README.md)，平台驗證與限制見 [驗證紀錄](PROTOBUF_VALIDATION.md)。

## 1. 最小方案

採用 proto3、官方 `protoc`、Go 的 `protoc-gen-go`、`google.golang.org/protobuf` 與 C# 的 `Google.Protobuf`。第一版不需要 gRPC、雲端 schema registry、Buf 遠端產生服務、自製 protoc plugin、反射掃描路由或動態程式產生。

官方工具已能產生 Go／C# 型別、parser、序列化與 oneof 分支。GameNetKit 提供一個生成入口，把工具版本和輸出路徑固定，使用者不必分別操作兩套產生流程。

```text
遊戲的 protocol/*.proto（唯一手寫訊息定義）
                  ↓ 官方產生器
       Go *.pb.go       C# *.cs
                  ↓
     GameNetKit Protobuf 收送 adapter
                  ↓
        GameNetKit 二進位傳輸
```

訊息定義一次，不代表遊戲邏輯只寫一次：伺服器驗證／套用規則、客戶端呈現、輸入與快照語意仍由各端遊戲程式負責。

## 2. 使用者的工作流程

1. 從範本建立 `protocol/game.proto`，設定 `go_package`、`csharp_namespace`。
2. 定義自己的訊息，以及每個方向各一個封包 envelope。
3. 執行一條 `Generate-Protocol.ps1 -Config <遊戲設定檔>`，同時產生 Go 和 C#。
4. 安裝選用 adapter，引用生成的型別，接上處理函式。
5. 改欄位只改 `.proto`，重新生成；不手改生成物，也不另維護訊息 ID 表。

設定檔只列 schema 根目錄／輸入檔、Go 輸出目錄與 C# 輸出目錄，不重複宣告欄位或訊息映射。完整範例提交生成原始碼，下載者可直接編譯；只有修改 schema 才需要產生工具。

初次建置要取得依賴；工具安裝後生成在本機執行，不上傳遊戲 schema。官方工具與 runtime 版本固定並記錄雜湊／依賴鎖定，禁止在 CI 使用 `latest`。精確版本在第 0 階段驗證後釘選，不假設 protoc、C#、Go 三者使用相同版本號。

## 3. 用標準 oneof 避免自製訊息系統

最小 schema 示意（節錄；實際範本另含 package 與語言選項）：

```proto
syntax = "proto3";

message MoveInput {
  uint64 sequence = 1;
  float x = 2;
  float z = 3;
}

message WorldSnapshot {
  uint64 tick = 1;
  float x = 2;
  float z = 3;
}

message ClientPacket {
  oneof body {
    MoveInput move = 10;
  }
}

message ServerPacket {
  oneof body {
    WorldSnapshot snapshot = 10;
  }
}
```

正式範本再加 `Hello`／`Welcome`／`ProtocolError` 分支，示範協定主版本檢查；身份憑證、session／match／generation、遊戲 input ACK 放在遊戲需要的位置，不由 SDK 強制所有遊戲採相同 envelope。

訊息種類由 oneof 的固定欄位編號表示，不另外配置 type 字串、Any type URL、型別名稱雜湊或 registry。新增一種訊息時，在同一份 schema 定義它並加入對應方向的 oneof，生成物會更新。

## 4. 型別化 API 的合理簡化

第一版選擇「型別化 envelope」，而非承諾任意 `Send<T>()`／`On<T>()` 自動尋找訊息種類。後者通常需要另一份註冊資料、執行期反射或自製產生器；目前沒有必要。

C# 使用方式（省略應用 handler）：

```csharp
// Parser 是 protoc 產生的靜態入口；binaryTransport 在外部建立。
var session = new ProtobufSession<ClientPacket, ServerPacket>(
    binaryTransport, ServerPacket.Parser);

session.Received += packet =>
{
    switch (packet.BodyCase)
    {
        case ServerPacket.BodyOneofCase.Snapshot:
            ApplySnapshot(packet.Snapshot);
            break;
    }
};

session.Open(binaryTransport.BeginAttempt(), url, helloPacket);
// 收到應用 Welcome 後送出輸入。
bool accepted = await session.TrySendAsync(new ClientPacket
{
    Move = new MoveInput { Sequence = sequence, X = x, Z = z }
});

// 在 Unity owner／Update 排空；網路背景執行緒不呼叫遊戲處理函式。
session.Pump();
```

Go 提供同樣薄的 typed peer：建立時傳入 inbound message factory，讀取回傳 `*pb.ClientPacket`、送出接受 `*pb.ServerPacket`；使用生成的 oneof type switch 分派。收到命令後仍由應用放進自己的有界 loop 命令佇列，讀取 goroutine 不直接改世界。

每個方向只需在建立 session 時指定一次型別／parser／factory，不需要逐訊息登錄 parser。型別錯誤優先在編譯時顯示。

`TrySendAsync` 的 true 只表示符合現有平台語意的本機提交／寫入成功，不代表伺服器收到或遊戲已執行；保留忙碌拒收、錯誤通知與應用 ACK。SDK 不自動補送攻擊，也不提供假的 exactly-once 保證。

## 5. 套件與依賴邊界

| 位置 | 職責 |
| --- | --- |
| `packages/unity/transport` | 加入 bytes 路徑，維持 epoch、佇列上限、背壓、抵達時間與清理契約 |
| `packages/unity/protobuf` | 選用 UPM adapter：生成訊息與 bytes 互轉、typed session／owner Pump |
| `packages/unity/protobuf-runtime` | 固定版本的官方 Google.Protobuf DLL 與授權 |
| `server/transport` | 增加二進位 WS adapter，保留既有文字 Wire 用法 |
| `server/protobuf` | 選用 Go package：官方 proto 編解碼、typed peer；不提供遊戲協定 |
| `tools/Generate-Protocol.ps1` 與內部工具解析 | 封裝官方產生器，不實作 schema parser |
| `examples/protobuf` | 獨立的小型共用世界範例，包含一份 schema、生成物與雙端接線 |

Unity protobuf adapter 可以依賴 transport 與 Google.Protobuf；這是新的傳輸 adapter 邊界，不要求現有十個選用同步套件改相依，也不讓 core／transport 反向依賴 Protobuf。Go 的 protobuf package 使用官方 runtime，不讓 loop、history 或遊戲無關契約認識生成型別。

Unity 執行期使用固定版本的 Google.Protobuf managed runtime 與必要依賴，包成可本機安裝的 UPM 成品，保留授權資訊；不要求每位遊戲作者在 Editor 安裝 NuGet 管理外掛。SDK 維護用建置腳本從鎖定的官方 NuGet 套件還原並封裝，確認沒有重複 DLL。實際 DLL／依賴清單由相容性驗證決定。

## 6. 傳輸接入順序

第一個可交付版本支援原生 WebSocket 與 Unity WebGL WebSocket；WebTransport 作後續明確里程碑，不能宣稱第一版已支援全部 transport。

### WebSocket

- 一個 WS binary message 對應一個 Protobuf envelope，利用既有 WS 訊息邊界，不再加自製 length header。
- 訊息被 WS 切成 fragments 時有界重組；解析前先檢查大小，初期沿用 32 KiB 訊息上限與 64 筆接收佇列上限。
- 同一連線嘗試固定文字或二進位模式；既有 `Open/Send/TryRead` 文字入口保留，新增明確 bytes 入口。範例用獨立 `/ws-protobuf` 路由，避免內容猜測／自動切換。
- binary hello 也是該遊戲 schema 的 ClientPacket，沒有先送私有 JSON 再偷偷切換格式的要求。
- 內部接收事件區分資料、關閉與傳輸錯誤；目前注入 JSON error 的方式只留在舊文字 adapter，不能讓 Protobuf parser 讀到錯誤 JSON。
- 一條連線只有一份有界事件佇列與一個消費 owner；舊文字與新 bytes API 不競爭讀取，不另外複製一套 socket 生命週期。
- WebGL JS 使用 `ArrayBuffer`／`Uint8Array`。C#→JS 用 pointer＋length，JS 在非同步送出前取得自有副本；JS→C# 使用有界 JS inbox，由 C# Pump 複製資料，避免用只能帶文字的 SendMessage 傳二進位。錯誤、抵達時間和 epoch 一起保存。
- 應用傳入的可變 bytes 在非同步期間不得被修改；第一版採明確複製／獨立所有權，不先引入池化、零拷貝或借用期限複雜度。

### WebTransport（後續）

- datagram 保留一包一 envelope；實際大小依底層路徑限制，不將 32 KiB 當成 MTU。
- 可靠 stream 使用 Protobuf 常用的 varint length-delimited framing，明確限制長度，再解析；不沿用目前文字換行切包。
- 原生 helper、JS 與 Go 接納 adapter 一起升級；helper 若用 Base64 作本機 stdio framing，它只是程序間傳遞，不代表線上封包使用 Base64。
- 可靠／不可靠選擇與能力回報需在此階段一併定義，不因用了 Protobuf 就改變送達保證。第一版對未實作的 binary WT 明確回報不支援。

## 7. 與現有模組的相容範圍

Protobuf 是序列化方式，不是新的預測、同步或壓縮演算法。收到生成型別後，應用仍接既有插值、預測、history、replication 等資料契約；生成訊息通常可變，歷史保存時仍須遵守 clone／所有權規則。

目前 C# `FrameDecoder` 輸出 UTF-8 string，`MessageConditioner` 的 payload 也是 string。第一版 Protobuf 範例不宣稱已可直接搭配這兩條文字路徑。後續需要時再補 bytes decoder／conditioner adapter，共用既有機制與容量限制，避免複製完整演算法。沒有用 Base64 包回 JSON 來假裝完成二進位整合。

Rumblebound 的 `Protocol.cs` 同時含 HTTP/config 與即時訊息，不能整份機械替換。先保留現有 JSON 範例，獨立驗證 Protobuf 範例；之後如要遷移，只逐步替換即時協定，不連帶改身份 API、遊戲規則、Replay 格式或同步預算。

## 8. 版本與錯誤規則

- 已使用的 field number 不重用；刪除欄位保留 `reserved` 編號／名稱。
- 需要分辨「未提供」與預設值的欄位使用 `optional` 或 message presence，不把零值當成缺欄位。
- Protobuf 的可解析不等於相同遊戲語意；不相容變更仍升協定主版本。第一版範例要求相同主版本。
- parser 接受新增未知欄位；未知 oneof 分支會形成無已知 body。Unity adapter 可傳入 accepts predicate，範例用它拒絕無已知 body 並回報 unsupported_message；Go 範例在應用 handler 檢查。是否忽略未知訊息由應用明訂。
- 解析失敗、超限與模式不符回報明確協定錯誤；成功解析後仍需驗證數值範圍、finite 浮點數、身份、權限、seq／generation。
- 不把 protobuf serialized bytes 當成跨語言 canonical hash，也不直接取代現有 replay state hash。

## 9. 實作階段與完成標準

| 階段 | 交付 | 完成條件 |
| --- | --- | --- |
| 0：相容性小驗證 | 同一份 proto 生成 Go／C#，固定官方工具與 runtime 版本 | 雙向編解碼；Unity IL2CPP 與 WebGL 實際建置／執行確認，含 oneof、repeated、64-bit 數字、optional；完成 runtime 封裝 |
| 1：二進位 WS | 原生、WebGL、Go bytes 路徑 | 真實 Go↔C#／WebGL 收送；分片、上限、錯誤事件、背壓、斷線、epoch、抵達時間；既有文字回歸通過 |
| 2：typed adapter＋範例 | 官方生成入口、選用套件、最小共用世界、接入文件 | 使用者只改 proto 即可生成兩端；型別化 send／receive；owner 交付；schema 演進與未知 body 測例通過 |
| 3：按需求擴充 | binary WT、bytes conditioning／delta | 各路徑實際驗收後才宣告支援；不阻擋前面獨立 WS 版本交付 |

第 0 階段是技術驗證，不代表已承諾 Google.Protobuf 在目前所有 Unity target 都可直接使用。若工具鏈／裁剪問題出現，先處理明確依賴與 AOT 引用，不能用反射自動註冊掩蓋問題。

生成腳本提供 `-Check`：在暫存目錄重生並比較生成物，CI 阻止 schema／Go／C# 不同步。只比較受控生成檔，穩定保留 Unity `.meta`／GUID，不刪除整個使用者輸出目錄。跨語言 fixture 比較解碼後語意，避免誤要求 protobuf 位元組在所有情況完全一致。

第一版提供獨立範例與選用套件，既有遊戲協定不需遷移。binary WebTransport、二進位 conditioner／delta 與同步預算不在這次範圍。階段 0／1 的所有平台執行驗收仍以驗證紀錄為準，不以建置成功代替 Player 實際執行。

## 參考依據

- [官方 proto3 指南](https://protobuf.dev/programming-guides/proto3/)：欄位編號、oneof、optional 與 schema 演進。
- [C# 官方生成程式說明](https://protobuf.dev/reference/csharp/csharp-generated/)：生成 message、Parser 與 oneof case。
- [Go 官方入門](https://protobuf.dev/getting-started/gotutorial/)：protoc-gen-go 與官方 runtime。
- [官方 runtime 相容保證](https://protobuf.dev/support/cross-version-runtime-guarantee/)：生成器／runtime 版本配對，不任意混用。
- [Buf 產生程式說明](https://buf.build/docs/generate/overview/)：可作後續 lint／breaking check／生成管理工具；目前單一 Go＋C# 流程先用官方產生器，避免新增必要服務。
