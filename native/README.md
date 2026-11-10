# 原生傳輸支援

[webtransport](webtransport/README.md) 是 Unity 桌面端選用的 Go HTTP/3 helper。使用原生 WebSocket 或瀏覽器 WebTransport 時不需要這個子程序。

從框架根目錄執行 `./tools/Build-NativeTransport.ps1 -OS windows -Arch amd64` 建置；輸出在 `webtransport/Builds/windows-amd64/`。發佈時提供對應平台執行檔，再依 [接線說明](webtransport/README.md) 設定絕對路徑及憑證 pin。`testfixture` 與 `internal/fixture` 只供測試，不隨遊戲發佈。
