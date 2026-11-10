# Minimal Go 服務

這是使用 Go SDK 的最短應用：每個 WebSocket 連線擁有獨立方塊世界，並非多人共用世界。

從 **gamenetkit 根目錄** 執行：

```powershell
./examples/Run.ps1 -Example Minimal
```

服務位於 `127.0.0.1:18091`；`/healthz` 回傳 `ok`，`/ws` 接受輸入，`/unity/` 提供另行建置的 Unity WebGL。服務本身不產生畫面，也不自動建置客戶端。保持終端執行，按 Ctrl+C 停止。

客戶端與建置步驟見 [minimal-unity](../minimal-unity/README.md)；逐步解說 main.go、握手、30 Hz 模擬與 15 Hz 快照見 [入門指南](../../docs/GETTING_STARTED.md)。需要兩人互見請改用 [Shared World](../shared-world/README.md)。
