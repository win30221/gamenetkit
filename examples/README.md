# 範例學習路線：先看資料流，再按需求加入組件

啟動使用 [Run.ps1](Run.ps1)，Unity 建置使用 [Build-Unity.ps1](Build-Unity.ps1)；多範例驗證見 [tests](tests/README.md)，完整用途與前提見 [指令導覽](../docs/COMMANDS.md)。

第一次先操作 Shared World，接著讀 Minimal 的最短程式接線，再回 Shared World 理解多人世界。之後學核心呈現與生命週期，最後按需求挑選其他能力。完整遊戲放在最後看整合；所有範例引用同一份 SDK，沒有另外實作一套插值或傳輸。

**選單數字是啟動編號，不是必須從 1 修到 9 的課號。** 目錄也未依學習順序命名。下方路線說明先備知識與完成條件；已經有連線與快照的專案，可直接從 [組件加入指南](../docs/COMPONENT_GUIDE.md) 選需要的能力，照安裝、接線、重設與驗證步驟加入。

## 第一次照這條路走

| 階段 | 閱讀／操作順序 | 完成後應能做到 |
| --- | --- | --- |
| 1. 先看見結果 | 啟動 [Shared World](shared-world/README.md)，開兩個視窗 | 兩人互見；只能控制自己；離開後清單移除，重連取得新 ID |
| 2. 看懂最短資料流 | 讀 [Minimal 接線教學](../docs/GETTING_STARTED.md#4-看懂-go-與-unity-如何接線)，對照 [Go 入口](minimal-server/main.go) 與 [Unity 入口](minimal-unity/Assets/MinimalClient.cs) | 能指出輸入、Go 固定步進、快照、Unity 每幀呈現各在哪裡；可以先只讀原始碼，不需先建置 Unity |
| 3. 從單人連線到多人世界 | 回到 [Shared World 接線順序](shared-world/README.md#接線順序)，讀 session 與 world | 知道 Minimal 每條連線各有世界，Shared World 則由單一模擬 owner 擁有所有玩家；一人離線不會停止世界 |
| 4. 核心組件 | [插值](interpolation/README.md) → [預測／校正](prediction/README.md) → [連線恢復](network-recovery/README.md) → [物件／動作](objects-actions/README.md)；每課再對照 [加入指南](../docs/COMPONENT_GUIDE.md) | 遠端平滑、自己立即回應、權威校正；手動停止不重試；新生命重建物件、動作確認不重複播放 |
| 5. 需求選修 | 按下表挑 Replay、AOI、Codec 或歷史／延遲補償 | 能獨立接入需要的能力，知道哪些要改雙端協定；選修之間不必依啟動編號全部完成 |
| 6. 完整整合 | [Rumblebound](rumblebound/README.md) | 能從已學的資料流找到組裝、協商與 reset 邊界，再看房間、戰鬥與其他應用規則 |

Minimal 是**程式接線基礎參考**；因執行畫面需要 Unity 建置，選單才把它放在接線參考區。C#／Go 終端課程也可在學到某組件時穿插閱讀，不必等到最後。

## 讀程式前先認識這些詞

| 詞彙 | 在這些範例中的意思 |
| --- | --- |
| input／seq | 客戶端提交操作；seq 區分本次連線的輸入序號，不是讓客戶端直接指定權威位置 |
| tick | 伺服器固定步進的序號；不是毫秒，也不是 Unity 畫面幀數 |
| snapshot | 伺服器產生的狀態副本；基本範例直接呈現，插值會另存歷史再取樣 |
| authority／owner | authority 是伺服器決定的狀態；模擬 owner 是唯一修改該世界的執行流程 |
| ack | 對端已處理的確認；輸入 ack、編碼 frame ack、動作確認各有自己的協定語意，不能混用 |
| epoch／life | epoch 隔離舊連線回呼；life 區分同 ID 的新生命。重連或重生都要清理對應歷史與呈現基準 |

若還不能從程式指出這些資料在哪裡產生與消費，先完成上面第 2、3 階段，再加入預測或更換協定。

## 不記參數，直接選範例

在檔案總管雙擊框架根目錄的 [Start.cmd](../Start.cmd)，輸入編號即可。或從 `gamenetkit` 的父目錄執行：

```powershell
./gamenetkit/examples/Run.ps1
```

已經在 `gamenetkit` 內就用 `./examples/Run.ps1`。選單按基礎、核心組件、需求選修、整合與接線參考分組，印出文件路徑及網址。選 `0` 離開。停止服務後可再執行選單，換另一個範例。

只想列出全部範例與參數名稱、不啟動服務：

```powershell
./examples/Run.ps1 -List
```

指定範例可用 `./examples/Run.ps1 -Example NetworkRecovery`；不帶參數會顯示選單。

## 啟動索引與各課完成條件

| 啟動編號／入口 | 先理解什麼 | 操作後確認什麼 |
| --- | --- | --- |
| 1：[Shared World](shared-world/README.md) | 可先直接操作；之後對照 input、tick、完整 snapshot 與單一 owner | 兩視窗互見、輸入歸屬、加入／離開與重連；預設 18092 |
| 2：[插值操作教學](interpolation/README.md) | 快照頻率與畫面幀不同；DTO 與呈現分開 | 關閉／固定／自適應的差別；能說明平順與緩衝延後的取捨；預設 18094 |
| 3：[預測與校正](prediction/README.md) | 前一課的遠端呈現；輸入 seq、權威 ack 與應用移動規則 | 延遲下自己立即移動；伺服器推移後仍會對齊，放開／失焦會停止；預設 18095 |
| 4：[即時連線與恢復](network-recovery/README.md) | 連線 attempt、epoch、前兩課的歷史與呈現基準 | 意外斷線會重試；手動停止不重試；新連線不殘留舊狀態；預設 18096 |
| 5：[物件同步與動作確認](objects-actions/README.md) | 完整清單、ID／life、伺服器決定動作結果 | 同 ID 新生命會重建；確認／拒絕／逾時各一次，權威物件由快照建立；預設 18097 |
| 選修 6：[Replay](replay/README.md) | tick、完整快照、物件生命；需要錄製或離線播放時選 | 完成錄製後才播放，暫停／跳轉／倍速不改即時世界；預設 18100 |
| 選修 7：[AOI](interest/README.md) | 每接收者的完整清單、物件進出與呈現基準；需要減少同步對象時選 | 兩人接收清單可不同；走遠物件消失，但權威世界數量不變；預設 18101 |
| 選修 8：[Codec](codec/README.md) | 雙端訊息封裝、獨立 frame ack；需要調整流量或精度時選 | 基準遺失後恢復全量；量化只改輸出副本；比較實際 bytes；預設 18104 |
| 選修 9：[歷史／延遲補償](history-lagcomp/README.md) | 權威 tick、life、呈現時間與命中規則；需要回溯判定時選 | 超過時間窗被拒絕；歷史查詢不改當前位置或血量；預設 18105 |

AOI、Codec 與歷史補償都不要求先加入 Replay。操作範例保留一些已學的呈現功能，方便比較；這不表示自己的專案必須安裝範例中的全部套件。SDK 依賴與應用所需的雙端協定，見 [組件接入地圖](../docs/COMPONENT_GUIDE.md)。完整遊戲與程式參考的既有啟動編號如下：

| 選單／入口 | 用途 |
| --- | --- |
| 10 整合：[對戰重製版](rumblebound/README.md) | 房間／選角／載入、三角色戰鬥、據點／練習 AI、重連與錄製播放，預設 18093；Windows／WebGL 與實際操作範圍見 [VALIDATION](../docs/VALIDATION.md) |
| 11 接線參考：[Minimal](../docs/GETTING_STARTED.md) | 最短 Go＋Unity 接線；一個連線一個世界。先讀原始碼即可；執行畫面需 Unity Player 或 WebGL 建置 |
| 12 API 參考：[C# 組件課程](modules/client/README.md) | 8 個純資料終端主題，學到對應能力時穿插執行，逐項理解 SDK 呼叫 |
| 13 API 參考：[Go 組件課程](modules/server/README.md) | 4 個純資料終端主題，學到對應能力時穿插執行，逐項理解伺服器組件 |

組件課程不需要跑完前面每一課；先知道 input、snapshot、tick、ack 與連線生命週期，再選需要的主題。[組件加入指南](../docs/COMPONENT_GUIDE.md) 從 Shared World 實作插值，再列各組件的安裝、呼叫與重設入口；各課有最短 API 示範。完整 API 與限制集中在 [MODULES](../docs/MODULES.md)。

## 先跑起來

以下從 `gamenetkit` 目錄執行，工具會自行載入 Go 並定位來源；不需要先設定 `$Go`：

```powershell
./examples/Run.ps1 -Example SharedWorld
```

開啟兩個 [http://127.0.0.1:18092/](http://127.0.0.1:18092/) 視窗，各按「連線」。這個服務與原本 18093 的重製遊戲可以同時啟動。按 `Ctrl+C` 停止；改完 Go 程式需重新啟動。

看插值的實際畫面，啟動後開啟 [http://127.0.0.1:18094/](http://127.0.0.1:18094/)：

```powershell
./examples/Run.ps1 -Example Interpolation
```

想單讀最短 API 呼叫時，保留原本的終端課程：

```powershell
./examples/modules/client/Run.ps1 -Lesson interpolation
```

預測與校正的操作教學使用獨立的 [http://127.0.0.1:18095/](http://127.0.0.1:18095/)，可與插值教學同時啟動：

```powershell
./examples/Run.ps1 -Example Prediction
```

一次比較弱網、診斷、時鐘及自動重連，啟動 [http://127.0.0.1:18096/](http://127.0.0.1:18096/)：

```powershell
./examples/Run.ps1 -Example NetworkRecovery
```

學伺服器 AOI：

```powershell
./examples/modules/server/Run.ps1 -Lesson interest
```

C# 課程使用 .NET 8 SDK，預設採用本機 Unity 安裝附帶版本，可用 `-DotNet` 指定其他 SDK；不需 Unity 授權、場景或 NuGet 還原。這些課程驗證 SDK 資料行為，不代表 Unity Player 或瀏覽器畫面通過。

## 維護邊界

- Minimal 與 Shared World 只安裝 core／transport，保持可以讀完的基本版。
- 新組件先增加聚焦課程及接線說明，再於 Rumblebound 整合，不把所有教學分支塞入同一個 Client。
- Shared World 與 Rumblebound 各自擁有應用程式；這次從已驗證的基礎分出教學版本，後續不互相引用。共用能力留在 SDK，單純相似的遊戲 DTO 不為了消除重複而拉進框架。
- 完整遊戲在 `examples/rumblebound`。目前驗證範圍見 [VALIDATION](../docs/VALIDATION.md)，重製階段紀錄見 [歷史作業表](../docs/archive/worklogs/REMAINING_WORK.md)。
- `Check-Dependencies.py` 檢查基本範例不引入選用套件、教學程式不反向依賴完整遊戲。平台驗收與已知限制見 [VALIDATION](../docs/VALIDATION.md)。

## 啟動編號 3～9 的 Unity 專案

每課各有獨立 Unity 專案，引用相同的純 C# session 與 SDK。Windows／WebGL 建置、啟動及操作對照集中在 [Unity 教學說明](unity-common/README.md)；一般選單仍預設開啟可直接操作的網頁教學。
