# Minimal 範例驗證

所有命令從 gamenetkit 根目錄執行。

| 指令 | 用途 |
| --- | --- |
| [Test-Player.ps1](Test-Player.ps1) | 真實 Go 服務與 Windows Player；需先建置 Minimal Windows。 |
| [Test-Startup.ps1](Test-Startup.ps1) | 編譯正式 MinimalClient 並以真實 WS 驗證啟動／終止生命週期；Unity 畫面 API 使用替身。 |

```powershell
./examples/Build-Unity.ps1 -Project Minimal -Target Windows
./examples/minimal-unity/tests/Test-Player.ps1
./examples/minimal-unity/tests/Test-Startup.ps1
```

`startup/Program.cs` 是啟動生命週期測試主機，包含 Unity API 替身，由 Test-Startup.ps1 編譯執行。
