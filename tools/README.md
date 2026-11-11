# 維護與共用支援

啟動範例請看 [examples](../examples/README.md)，框架驗證請看 [tests](../tests/README.md)，所有入口見 [指令導覽](../docs/COMMANDS.md)。

| 指令 | 用途 |
| --- | --- |
| [Build-NativeTransport.ps1](Build-NativeTransport.ps1) | 編譯 Go 原生 WebTransport helper，輸出至 native/webtransport/Builds。 |
| [Clean-Workspace.ps1](Clean-Workspace.ps1) | 依白名單清理生成物；先以 -WhatIf 檢視。 |
| [Generate-Protocol.ps1](Generate-Protocol.ps1) | 依遊戲 JSON 設定，以釘選官方 protoc／protoc-gen-go 同時產生 Go 與 C#；`-Check` 比較而不改生成物或 Unity `.meta`。 |
| [Restore-Protobuf.ps1](Restore-Protobuf.ps1) | 從雜湊鎖定官方 NuGet 還原 Google.Protobuf 本機 UPM runtime；`-Check` 驗證已提交 DLL／授權。 |

`internal` 是腳本共用實作，不是日常操作入口：`Toolchain.ps1` 讀取版本及解析工具位置，`Go-Env.ps1` 設定目前程序的 Go 環境。

Protobuf 設定、首次下載與快取、版本鎖定及平台前提見 [PROTOBUF.md](PROTOBUF.md)。範例入口：`pwsh ./tools/Generate-Protocol.ps1 -Config ./examples/protobuf/protocol.json`；使用已提交生成碼與 runtime 的消費端不需安裝產生工具。
