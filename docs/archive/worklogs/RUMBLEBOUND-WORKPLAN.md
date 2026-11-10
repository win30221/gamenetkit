> 歷史工作紀錄：以下授權描述、路徑與完成狀態僅指原作業，不代表本次遷移的指令或驗證結果。現行工具與結果見根目錄 docs/MIGRATION.md、docs/VALIDATION.md。

# 完整遊戲的實作順序

使用者授權持續完成所有剩餘項目，依 [持續作業表](REMAINING_WORK.md) 執行，不逐項等待確認。

1. **移動與碰撞**：原遊戲規則／地圖／案例集中於 `examples/rumblebound/game`，保留來源 hash；共用模擬 owner 執行原有碰撞／推擠。C# GameContent 驗證設定，GameMovement 共用幾何與採樣，BattlePresentation 管理原場地與角色。覆蓋原案例與真實兩人連線。
2. **呈現與恢復**：把已完成教學的預測／校正／插值／時鐘接到新遊戲 DTO；重用原遊戲跨語言案例，維持生命、身份／席位、attempt 邊界；選用模組可停用。
3. **對戰入口**：先接身份／房間／選角／地圖載入確認，再接戰鬥，避免未載完的玩家參與模擬。HTTP 採 Gin，服務生命週期與 domain 分離。
4. **戰鬥**：依序做攻擊／技能／彈體、命中／HP／冷卻、死亡／復活；重用原伺服器 domain 規則與測試，拆出可審查責任。輸入不帶可相信的傷害或任意世界狀態。
5. **玩法與呈現**：據點／輪替／勝負、練習 AI、結算，之後接原素材與 UI。新的場景、玩家與效果呈現分開管理，不擴充成巨型 MonoBehaviour。
6. **進階組件**：在完整對戰情境接回動作呈現、歷史／補償、範圍投影、codec、錄製／離線播放；保留每課獨立範例。
7. **對照收斂**：行為／素材／命令對等、Unity 資產、Windows／WebGL 與實際操作、選用套件移除。驗收完成前保留 sample；不把啟動 sample 當成新 example 完成。

地圖、數值與幾何是遊戲 application 的一部分，不放進 generic SDK。`examples/rumblebound/game/MIGRATION.json` 保留抽離前的來源 hash。暫時 rules 副本已移除，sample 與 example 改用共用遊戲層；保留 sample 的入口、呈現與整合測試作行為對照。

1～6 已完成接線與自動測試。第 7 項已通過原跨語言／FPS 案例、雙 Windows Player、實體移除選用套件、原生 UI／3D、實際瀏覽器與 WebGL 的主要流程；驗收發現的輸入焦點、結算保留、長網址與過期房間問題均已修正。真實 Google 帳號依使用者要求暫緩，Linux／macOS 僅交叉編譯；完整證據與限制集中於 [VALIDATION](../../VALIDATION.md)。

正式入口已收斂：移除 32 個 v1 過渡程式／頁面／測試，教學保留在獨立 shared-world。完整網頁統一為 web，Unity 只保留 Battle 場景／應用，兩個 Go 命令共用 launcher。原 sample 與共用 domain／原案例仍保留。
