# 選用的桌面 WebTransport helper

Unity 的 .NET Standard 2.1 傳輸套件可透過獨立 Go 程式使用 HTTP/3 WebTransport。QUIC 採用與既有 sample 相同的 `quic-go`／`webtransport-go`；不把 Go 或 QUIC library 加進 Unity managed assembly。此路徑適用可啟動子程序的桌面環境，使用一條連線一個隱藏 helper，需隨 Player 提供對應平台執行檔。行動裝置與主機平台尚未支援。

從 `gamenetkit` 建置，例如：

```powershell
./tools/Build-NativeTransport.ps1 -OS windows -Arch amd64
# 也提供 linux/darwin 與 amd64/arm64 交叉編譯入口
```

Windows 預設產物為 `native/webtransport/Builds/windows-amd64/realtime-webtransport.exe`。應用自行將它放入發佈目錄，並於連線前設定絕對路徑：

```csharp
MessageTransport.NativeWebTransportExecutable = helperAbsolutePath;
if (MessageTransport.SupportsWebTransport) {
    int attempt = transport.BeginAttempt();
    transport.Open(attempt, "https://server.example/wt", helloJSON,
        sha256LeafCertificateHex, callbackTarget, true);
}
```

這個設定僅在原生／Editor 編譯分支存在；WebGL 繼續使用瀏覽器的 WebTransport。未設定 helper 時，原生 WS 正常可用，`SupportsWebTransport` 為 false，要求原生 WT 會明確拋出不支援。路徑存在代表已配置能力；實際程序啟動仍可能因平台／權限／檔案格式失敗。

TLS 必須提供 SHA-256 葉憑證指紋，helper 會以固定時間比較並檢查憑證有效期間；不接受空白 pin，也不從命令列或環境傳遞 URL／hello。應用需從可信配置取得 pin。身份、ticket、Origin 與應用版本驗證仍由伺服器負責。

hello 以可靠 stream 的一行送出，不能包含 CR／LF；伺服器可回傳多行可靠文字，也可在 welcome 後關閉該 stream 並繼續使用 datagram。`Send` 使用 datagram，實際最大大小受 QUIC 路徑 MTU 限制；32 KiB 是傳輸內容的總上限，不是 datagram 保證。沒有自動分片或可靠升級，過大的 datagram 會失敗。收送保持原始 UTF-8 文字，不解析遊戲 JSON。

本機 stdio 使用有界 Base64 行：啟動 `RFWT1 / URL / pin / hello` 四行，送出 `D <base64>`，收到 `M <base64>`；錯誤只回固定代碼，不印出 URL／憑證／遊戲內容。這是 helper 私有版本協定。Close／Dispose 只終止該實例的子程序，替換後的舊回覆仍受 MessageTransport epoch 隔離。

`./tests/scripts/Test-Transport.ps1` 建置並執行實際 helper 與本機 HTTP/3 測試服務，驗證 pinned TLS、可靠 32 KiB、datagram UTF-8、錯誤 pin、兩實例、關閉／替換／診斷中斷。`go test ./...` 另驗證 stdio EOF 與錯誤文字邊界。這些是 Windows .NET 8 的真實網路測試及 Unity 參考編譯，尚未代表 Unity Player／其他桌面 OS 的實際執行驗收。

`main.go` 是隨桌面 Player 發佈的 helper；`internal/fixture/server.go` 與 `testfixture/main.go` 是本機 HTTP/3 測試服務，由 Go 測試和 Test-Transport.ps1 使用，無需隨 Player 發佈。
