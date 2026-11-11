# 框架驗證

維護 Go SDK、Unity SDK 與共用傳輸時使用。本目錄的程式不參與正式遊戲啟動。所有命令從 gamenetkit 根目錄執行。

| 指令 | 用途 |
| --- | --- |
| [Test-GoAndBrowser.ps1](scripts/Test-GoAndBrowser.ps1) | 日常 Go／JS 回歸：15 個 Go 模組 test／vet、傳輸、共用網頁與 Shared World Node 測試；需 Go、Node。 |
| [Test-Protobuf.ps1](scripts/Test-Protobuf.ps1) | 官方 runtime 的 Go／C# 相容性、型別化 binary WS 與兩人共用世界；需 Go、.NET8，不代替 Unity Player 驗證。 |
| [Test-UnitySdk.ps1](scripts/Test-UnitySdk.ps1) | 真正 Editor 執行純 SDK 測試；需釘選 Editor 與有效授權。 |
| [Test-SdkNative.ps1](scripts/Test-SdkNative.ps1) | 在無 Editor 授權時執行相同的純 C# SDK 契約測試；需 .NET 8，不涵蓋 Player／WebGL。 |
| [Test-Transport.ps1](scripts/Test-Transport.ps1) | 真實 WS／HTTP3 及 browser bridge；需 Go、.NET 8 SDK、Node。 |
| [Test-GoModuleRemoval.ps1](scripts/Test-GoModuleRemoval.ps1) | 進階：暫移選用 Go 模組，測試／vet 後還原；需序列執行。 |
| [Check-Dependencies.py](scripts/Check-Dependencies.py) | Python：檢查依賴邊界。 |

```powershell
python ./tests/scripts/Check-Dependencies.py
python -B -m unittest discover -s ./tests/dependencies -v
./tests/scripts/Test-GoAndBrowser.ps1
```

[fixtures](fixtures/README.md) 保存 Go／C# 共用 codec 測試資料及產生器。`sdk-unity` 是 Editor 測試專案，`transport` 是實際傳輸測試主機；`dependencies` 測試 Python 守門規則。範例專用 C# 測試主機跟隨範例保存。範例專用驗證另見 [跨範例](../examples/tests/README.md)、[Minimal](../examples/minimal-unity/tests/README.md) 與 [Rumblebound](../examples/rumblebound/README.md)。
