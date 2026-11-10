# Minimal Unity 客戶端

此 Unity 專案只引用 core 與 transport，展示輸入與權威快照。用 Unity Hub 加入本目錄；版本依 `ProjectSettings/ProjectVersion.txt`。完整前提與程式解說見 [入門指南](../../docs/GETTING_STARTED.md)。

先在 **gamenetkit 根目錄** 的第一個終端執行：

```powershell
./examples/Run.ps1 -Example Minimal
```

第二個終端也從根目錄執行：

```powershell
./examples/Build-Unity.ps1 -Project Minimal -Target Windows
& ./examples/minimal-unity/Builds/Windows/Minimal.exe
```

左右鍵移動方塊；兩個客戶端各有獨立世界，不會互見。Web 版改用 `-Target Web` 建置，手動開啟 [Minimal 網頁](http://127.0.0.1:18091/unity/)。不能直接開啟輸出的 index.html。

入口是 [MinimalClient.cs](Assets/MinimalClient.cs)，建置入口是 [MinimalBuild.cs](Assets/Editor/MinimalBuild.cs)。收到第一份有效快照後才開始送輸入；傳輸 error 會關線，目前沒有自動重連。測試範圍見 [tests](tests/README.md)。
