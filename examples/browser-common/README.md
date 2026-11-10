# 共用瀏覽器支援

本目錄是各教學主機共用的來源檔，不是可獨立啟動的網站，也沒有 npm 安裝步驟。要操作畫面，從框架根目錄執行 `./examples/Run.ps1` 選擇範例。

| 檔案 | 用途 |
| --- | --- |
| [UnityRoutes.cs](UnityRoutes.cs) | 主機提供 Unity WebGL 資源與 WebSocket 代理 |
| [unity-view.css](unity-view.css) | Unity 網頁容器與版面 |
| [mobile-view.js](mobile-view.js) | 橫向提示與全螢幕控制 |
| [mobile-view.test.mjs](mobile-view.test.mjs) | 共用手機網頁行為的 Node 測試 |
| [Test-UnityRoutes.mjs](Test-UnityRoutes.mjs) | Unity 資源／路由的測試工具 |

Unity 建置與網址見 [Unity 教學說明](../unity-common/README.md)。共用手機網頁測試已納入 `./tests/scripts/Test-GoAndBrowser.ps1`；這些測試不代替真實瀏覽器或觸控裝置驗收。
