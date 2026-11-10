# Shared World 原生 C# 測試客戶端

Program.cs 使用正式 core／transport 連到真實 Go WebSocket，驗證多人世界的加入、移動與離開。它是測試主機，沒有 Unity 畫面。

一般驗證從 gamenetkit 根目錄執行：

```powershell
./examples/shared-world/scripts/Test-SharedWorld.ps1 -NativeClient
```

腳本直接編譯原始碼，不需要 NuGet 還原。NativeClient.csproj 另外提供標準 .NET 8／IDE 專案入口；手動執行時需先啟動 Go 服務，並把服務網址作為程式第一個參數。
