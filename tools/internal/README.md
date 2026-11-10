# 內部腳本支援

這兩份檔案由其他腳本以 dot-source 載入。一般操作範例與執行測試時，不必另外手動執行。

| 指令 | 用途 |
| --- | --- |
| Toolchain.ps1 | 從根目錄 toolchain.json 解析 Go／Unity／.NET；支援 GAMENETKIT_* 覆寫。 |
| Go-Env.ps1 | 選用 Go 並設定目前 PowerShell 的快取環境；不修改系統環境。 |

需要在自己的終端使用同一份 Go 工具鏈時：
```powershell
. ./tools/internal/Go-Env.ps1
& $Go version
```
