# Protobuf 第一版驗證紀錄

日期：2026-11-11。第一版提供 Go／Unity 型別化二進位 WebSocket；使用方式見 [範例](../examples/protobuf/README.md)，生成工具見 [工具說明](../tools/PROTOBUF.md)。

## 版本與平台

釘選 protoc 36.2、Google.Protobuf 3.36.2（官方 netstandard2.0 DLL）、Go protobuf v1.36.11；下載來源與 SHA256 見根目錄 `protobuf-toolchain.json`。Unity 6000.6.8f1 使用 .NET Standard API 相容層，提供 System.Memory／Unsafe，不重複封裝系統組件。

| 驗證 | 結果 |
| --- | --- |
| Go ↔ C# 官方生成物互通 | 通過：optional 零值 presence、repeated uint64 > 2^53、oneof uint64 最大值、未知欄位保留 |
| Unity Editor | 相容性檢查與 C# 編譯通過 |
| Windows Mono Player | 建置及實際執行通過；兩個連線共享世界、位置更新與 ACK，輸出 `PROTOBUF_PLAYER_SMOKE PASS` |
| WebGL IL2CPP | 實際建置通過，包含最後的時鐘修正；瀏覽器執行尚未驗證 |
| Windows IL2CPP | 未完成：本機缺少 Visual Studio C++／Windows SDK，建置工具鏈檢查失敗 |

瀏覽器驗證透過 Codex 本機瀏覽器工具嘗試；使用者回覆已開啟 Local browser use 後，重試與重設工具仍回報停用。這是目前的執行驗證限制，不能把 WebGL 建置成功視為瀏覽器已通過。Windows 範例預設使用 Mono；`ProtobufBuild.WindowsIl2Cpp` 保留給已安裝 C++ 工具鏈的環境。

Windows smoke 以 batchmode／nographics 執行，驗證實際 Player runtime 與網路交付，不涵蓋畫面品質、手動輸入或效能。Player log 有 Unity temporary allocation 壽命警告；沒有 Protobuf 例外，smoke 通過，不宣稱完成長時間效能驗證。

## 回歸與邊界

- `Test-Protobuf.ps1`：真實 Go WebSocket 與 C# typed session，兩人狀態、ACK、過期序號、版本拒絕、連線替換、Pump owner 交付、畸形 protobuf、未知 body predicate、斷線、空參數／超限傳送皆通過。
- `Test-Transport.ps1`：既有文字 WS、原生 HTTP/3 WebTransport 與新 binary WS 通過；涵蓋分片、32KiB 精確邊界與超限、空訊息、背壓、資料複製、錯誤事件與 epoch 隔離。
- 瀏覽器 JS／jslib 的 25 個測試通過；這些為 Node 模擬 bridge 測試，不取代瀏覽器 Player 執行。WebGL C# 分支也獨立編譯通過。
- `Test-SdkNative.ps1`：既有 SDK 模組合約通過。
- `Test-GoAndBrowser.ps1`：全部 15 個 Go 模組 test／vet、25 個傳輸 JS 測試、4 個共用網頁與 4 個 Shared World 測試通過。完整回歸發現舊範例間接 protobuf 版本仍鎖在 1.36.10；已同步九個範例至 1.36.11 並更新 checksum，重跑通過。
- 依賴邊界檢查與 11 個 guard 測例通過；core／transport 不引入 Google.Protobuf。
- `Generate-Protocol.ps1 -Config examples/protobuf/protocol.json -Check` 通過；生成工具另檢查路徑重疊、手寫檔案覆寫與 Unity metadata 保留。

檢查修正了 WebGL 時鐘域：`ClockMS` 與收件時間都使用 `performance.now()`，原生維持 Stopwatch。確定性 bridge 測試確認延後輪詢不改寫抵達時間。

本次本機詳細輸出位於忽略版控的 `artifacts/protobuf-*.log`。`Test-Protobuf.ps1` 的 .NET 8 測試編譯會出現 System.Memory 組件版本對應警告；實際 .NET 8 與 Unity Mono 執行均已通過。

## 重現

```powershell
./tools/Generate-Protocol.ps1 -Config examples/protobuf/protocol.json -Check
./tests/scripts/Test-Protobuf.ps1
./tests/scripts/Test-Transport.ps1
./tests/scripts/Test-SdkNative.ps1
./tests/scripts/Test-GoAndBrowser.ps1
./examples/Build-Unity.ps1 -Project Protobuf -Target Windows
./examples/Build-Unity.ps1 -Project Protobuf -Target Web
./examples/protobuf/Run.ps1
```

保持服務執行，在另一個終端啟動 Windows Player，帶 `-protobufSmoke -logFile <絕對路徑>`，檢查 `PROTOBUF_COMPATIBILITY PASS` 與 `PROTOBUF_PLAYER_SMOKE PASS`。WebGL 開啟 `http://127.0.0.1:18101/unity/?smoke=1`，同樣要求成功標記；頁面也會設定 `data-protobuf-result`，目前這一步仍待實際瀏覽器驗證。

本版本不提供 binary WebTransport、binary conditioner／delta、同步預算或 RPC 系統。範例是本機型別化傳輸示範，遊戲規則、身份與輸入頻率限制仍由應用處理。
