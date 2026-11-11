# Protobuf：一份定義，Go＋Unity 型別化收送

這個獨立範例使用官方 Protobuf 工具與 runtime，沒有自製 schema parser、訊息 ID 表或 RPC 產生器。先讀 [協定](protocol/game.proto)，再對照 [Go 伺服器](server/main.go) 與 [Unity 客戶端](client/Assets/ProtobufDemo.cs)。

## 直接執行

沿用儲存庫釘選的 Go、Unity 與 PowerShell7。官方 Google.Protobuf DLL、授權及生成的 Go／C# 原始碼已包含在來源中，執行範例不需安裝 protoc 或 Unity NuGet 外掛。

從儲存庫根目錄執行：

```powershell
./examples/Build-Unity.ps1 -Project Protobuf -Target Web
./examples/protobuf/Run.ps1
```

建置需 Unity 授權及 WebGL module。保持服務開啟，開兩個 `http://127.0.0.1:18101/unity/` 視窗，各按 Connect；WASD／方向鍵移動，兩邊應看到同一份玩家清單與位置。離開／Disconnect 後對方清單移除。停止服務按 Ctrl+C。

Windows 版本用 `-Target Windows` 建置，再啟動 `client/Builds/Windows/ProtobufExample.exe`；預設連到相同的18101服務。平台後端及已驗證範圍見 [驗證紀錄](../../docs/PROTOBUF_VALIDATION.md)。

Unity 主動呼叫 session.Pump 才交付事件，網路背景執行緒不改場景。範例的移動是每筆已接受命令產生有界位移，20Hz 廣播；它聚焦型別化收送，沒有預測、插值、輸入頻率反作弊或正式房間／身份系統，不應直接當成公開對戰服務。

## 修改訊息只改一份

```powershell
./tools/Generate-Protocol.ps1 -Config examples/protobuf/protocol.json
# CI／檢查生成物是否與 schema 同步：
./tools/Generate-Protocol.ps1 -Config examples/protobuf/protocol.json -Check
```

首次生成會取得鎖定的官方工具；後續可重用快取。工具入口目前支援 Windows x64／PowerShell7。細節見 [工具說明](../../tools/PROTOBUF.md)。

`protocol.json` 只指定 schema 根目錄、輸入檔與兩端的獨立生成目錄。生成來源分別是 `server/gen`、`unity/Runtime/Generated`，不要手改。刪除／改名 schema 導致舊生成物遺留時，工具明確報錯，不擅自刪除使用者程式或 Unity metadata。

每個方向用標準 oneof envelope：ClientPacket 包 Hello／Move，ServerPacket 包 Welcome／Snapshot／Error。新增訊息只在 `.proto` 定義欄位與 oneof 分支，再生成；兩端仍各自接自己的處理函式。

## 接進自己的遊戲

1. 複製 schema/config 範本，調整 `go_package`、`csharp_namespace` 和輸出路徑。
2. Go 引用 `server/transport` 的 BinaryWebSocket 與 `server/protobuf` 的 typed Peer；只在接受連線後包裝，不把 HTTP／身份放進 SDK。
3. Unity 參照本範例 `client/Packages/manifest.json` 安裝 transport、protobuf-runtime、protobuf 與自己的生成套件。`file:` 路徑以 Packages 目錄為基準。
4. 建立 `ProtobufSession<ClientPacket,ServerPacket>`，傳入生成的 ServerPacket.Parser；呼叫 Open／TrySendAsync／Pump。
5. 應用檢查 protocol version、已知 oneof body、數值、seq 與身份；資料轉進自己的模擬與呈現層。

SDK 不以訊息類名推導 wire ID，也不要求逐類型註冊 parser。oneof 的欄位編號就是標準 Protobuf 編碼的一部分。刪除欄位需 reserved，不能重用編號；需要區分缺值／零值就使用 optional。

第一版二進位 WebSocket 一個 message 一個 envelope，32KiB上限。`/ws-protobuf` 專用路由從 hello 起就是二進位，不混 JSON；WebGL 直接收送 bytes，沒有 Base64 外層。原有 JSON 範例與 WebTransport 路徑保持可用，但 binary WT、string conditioner／delta 的二進位 adapter 尚未提供。

## 驗證

```powershell
./tests/scripts/Test-Protobuf.ps1
```

它驗證實際 Go↔C# 二進位 WS、兩人共享世界、ACK、世代替換、已知／未知 body、失敗事件，以及 optional 零值、oneof、repeated、超過2^53的64-bit整數跨語言往返。另有 Go SDK／範例測試、JS bridge 測試和 Unity Player smoke。

WebGL URL 加 `?smoke=1`，或 Windows Player 加 `-protobufSmoke`，會自動建立兩個連線並驗證跨連線位置更新。成功輸出 `PROTOBUF_PLAYER_SMOKE PASS`；WebGL 同時把結果放在頁面 `data-protobuf-result` 屬性，方便實際瀏覽器測試。這個 smoke 不是效能基準。
