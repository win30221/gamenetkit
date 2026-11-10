# 跨範例驗證

用於維護多個範例；單一範例的行為測試放在該範例自己的目錄。所有命令從 gamenetkit 根目錄執行。

| 指令 | 用途 |
| --- | --- |
| [Test-UnityCompilation.ps1](Test-UnityCompilation.ps1) | 編譯 SDK、七個教學與完整遊戲的 application／Unity／Editor 及 WebGL 分支。先匯入 Rumblebound 還原套件；不代表 Player 執行驗收。 |
| [Test-UnityPlayers.ps1](Test-UnityPlayers.ps1) | 啟動七課的真實雙 Windows Player；先建置所選範例，可用 -Projects 指定。 |

```powershell
./examples/tests/Test-UnityCompilation.ps1
./examples/Build-Unity.ps1 -Project Prediction -Target Windows
./examples/tests/Test-UnityPlayers.ps1 -Projects Prediction
```
