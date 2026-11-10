# Rumblebound 遊戲層

此目錄保存 Rumblebound 專用的規則、協定、模擬與呈現資源；通用即時連線 SDK 仍位於倉庫的 `server` 與 `packages/unity`。

- `shared`：規則、地圖、驗證、幾何與碰撞。
- `protocol`：遊戲專用訊息格式與驗證。
- `identity`、`lobby`、`service`：身份、房間與應用服務。
- `sim`：權威戰鬥、AI、據點與伺服器重播。
- `unity`：純 C# 遊戲資料與移動邏輯。
- `unity-art`：角色、動畫、場景、特效、音效與呈現器，保留素材 GUID 和授權。
- `devcert`、`web`：開發憑證與登入視圖。
- `cmd/replay`：驗證、重建及匯出已完成的伺服器 journal。
- `cmd/replaydemo`：產生可供 Test-Replay.ps1 驗證的示範 journal，不參與正式對戰啟動。

原始來源及 hash 保存在 `MIGRATION.json`。在此目錄可執行 `go test ./...` 和 `go vet ./...`。完整遊戲的啟動與建置方式見[上層 README](../README.md)。搬出此倉庫時，仍需處理上層 README 所列的框架依賴。
