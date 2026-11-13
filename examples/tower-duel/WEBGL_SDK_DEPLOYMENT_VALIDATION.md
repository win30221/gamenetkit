# WebGL SDK 改動與部署驗證 · 2026-11-13

公開版本：`20261113T053841Z-778150ef`。
[開啟雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)。

本輪將 [SDK 共用化改動](SDK_REUSE_VALIDATION.md) 重建為 WebGL release，
完成本機瀏覽器測試後，以 client-only 模式更新既有站台，再完成公開站瀏覽器驗證。
遊戲服務維持 `active`，部署前後 MainPID 均為 `3110574`，未重新啟動。

## 建置與部署

- Unity 6000.6.8f1，使用既有遊戲場景、IL2CPP、Gzip、Medium stripping 與正式 WebGL 設定。
- 建置結果 `Succeeded`，0 errors、0 warnings，耗時 202.14 秒，產物共 18,348,458 bytes。
- API、地圖、規則、代理設定及 SDK `transport.js` 與前一公開版逐檔相同。
  本次更新四個 WebGL build 檔案，部署腳本確認後原子切換 release。
- 遠端 release 的 12 個檔案與 manifest SHA-256 一致；公開下載的 7 個檔案也與本次建置一致。
- HTTPS 健康檢查、協定 3、八張卡及地圖／規則雜湊均通過檢查。
- 前一版 `20261113T024139Z-99d819bc` 保留於 releases 目錄。

## 實際瀏覽器驗證

使用 Chrome 157.0.8103.78，載入實際 release WebGL 檔案，沒有修改 production runtime 或加入測試專用介面。

| 驗證 | 結果 |
| --- | --- |
| 本機受控快照／音效／重連測試 | 11 項通過，無 runtime error |
| 公開站相同受控測試 | 11 項通過，無 runtime error |
| 本機弱網真實雙人對局 | 完整對局、頁面重整、頁內斷線恢復及同房重賽通過 |
| 公開 HTTPS／WSS 真實雙人對局 | 完整對局、兩種恢復及同房重賽通過，兩端無 browser page error |
| 滑鼠、觸控與尺寸 | 720×1000 滑鼠、390×844 觸控及七種視窗尺寸通過既有操作／構圖檢查 |

### 重連與法術時間軸

`Test-Network-Web.cjs` 在瀏覽器攔截測試用 WebSocket，提供受控快照及兩次刻意失敗的恢復嘗試，
但實際執行正式 WebGL 客戶端的 SDK、快照取樣、特效與音效程式。

- 最新權威 tick 停在命中之前時，火球及冰霜雖可見，均不會提前播放命中音效。
- 後續快照允許呈現 tick 到達命中時才播放音效；重複快照不重播同一命中。
- 火球及冰霜到期後移除；火球到期後，特效量測區域相對空場基準的差異為 0 pixels。
- 跨重賽重用 effect ID 仍會建立新的命中生命週期。
- 本機 SDK 退避間隔為 555／1024／2034 ms；公開 bundle 的受控測試為 529／1048／2036 ms。
- SDK 重試及頁面重整均保留原房間／憑證。音效計數排除 Unity 恢復既有 Web Audio 音源的內部重啟。

受控測試不代替真實伺服器對局。另行執行的本機弱網對局使用單向 120 ± 80 ms 訊息層延遲，
實際丟棄 16 則、重複 6 則訊息；雙方出牌 13／8 次並完成對局及重賽。
公開 WSS 對局雙方出牌 10／8 次，均操作八張卡與兩種法術，勝負及六座塔最終血量一致，
之後成功進入同房下一局並再次部署。

觸控使用瀏覽器模擬；Android／iOS 實機效能與操作手感仍需實機驗收。

## 重跑與產物

```powershell
./examples/tower-duel/scripts/Build.ps1 -Target WebGL
node examples/tower-duel/scripts/Test-Network-Web.cjs http://127.0.0.1:18110 artifacts/tower-duel/network-web
node examples/tower-duel/scripts/Test-Web.cjs http://127.0.0.1:18110 artifacts/tower-duel/web-test
```

本機需先用 `Run.ps1 -Web` 啟動服務；弱網測試另經 `server/cmd/weaknetproxy`。
`Test-Web.cjs` 現在同時要求頁面重整恢復與不重整頁面的 WebSocket 恢復成功。

本次紀錄位於 `artifacts/tower-duel/webgl-sdk`：

- `build-result.json`、`editor.log`：正式 WebGL 建置。
- `timeline-local/result.json`、`timeline-public/result.json`：11 項 SDK／特效檢查及截圖。
- `weak-web/result.json`、`public-web/result.json`：真實雙人對局、兩種恢復、重賽、尺寸及截圖。
- `public-hashes.json`、`remote-hashes.json`、`service-after.txt`：公開檔案、遠端 release 與服務連續性。

本次部署 manifest 位於 `.local/deploy/tower-duel/20261113T053841Z-778150ef/deployed.json`。
