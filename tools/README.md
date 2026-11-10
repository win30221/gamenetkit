# 維護與共用支援

啟動範例請看 [examples](../examples/README.md)，框架驗證請看 [tests](../tests/README.md)，所有入口見 [指令導覽](../docs/COMMANDS.md)。

| 指令 | 用途 |
| --- | --- |
| [Build-NativeTransport.ps1](Build-NativeTransport.ps1) | 編譯 Go 原生 WebTransport helper，輸出至 native/webtransport/Builds。 |
| [Clean-Workspace.ps1](Clean-Workspace.ps1) | 依白名單清理生成物；先以 -WhatIf 檢視。 |

`internal` 是腳本共用實作，不是日常操作入口：`Toolchain.ps1` 讀取版本及解析工具位置，`Go-Env.ps1` 設定目前程序的 Go 環境。
