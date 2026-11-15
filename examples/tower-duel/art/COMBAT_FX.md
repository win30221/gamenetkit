# 雙橋戰鬥特效

2026-11-15：重用 Rumblebound 已取得並完成 URP 適配的素材，本輪未下載任何新特效包。

| 呈現 | 重用來源 | 雙橋調整 |
| --- | --- | --- |
| 近戰刀光 | Sugawara Studio 的免費紅色刀光動畫 | 10 格圖集；依兵種調整大小；只在確認命中後播放 |
| 一般／重擊 | Cartoon FX Remaster Free 的 Hit D／Hit A | 小型火花與衝擊環；搭配角色短暫亮色、模型後仰 |
| 火球飛行 | Hun0FX 的 StylizedVFX Fire Pack | 重用 FireballDrop，保留火焰、餘燼與煙，補漸細拖尾 |
| 火球／炸彈爆炸 | Cartoon FX Remaster Free 的 Fire Explosion B | 重用 GroundImpact，依作用半徑縮放；火球在權威 end tick 清除 |
| 冰霜 | Cartoon FX Free 的 MagicAura B Runic | 藍色透明法陣、粒子循環；放在橋面上方，外圈維持實際範圍 |
| 死亡／破塔 | 同一爆炸素材的煙團 | 死亡小塵煙；破塔另加原創低面數碎石粒子 |

完整原始檔、取得紀錄、授權和既有 URP 轉換說明都留在
`examples/rumblebound/game/unity-art/Art/VFX`。本次沒有修改 Rumblebound 的素材或呈現程式。

## 匯入與重建

`TowerDuelVfxContent.Prepare()` 沿預製件、材質、貼圖、網格和 Shader 的 GUID 收集必要依賴，
保留原 `.meta`，將選取的 75 個既有檔案建立到 `client/Assets/ReusedVFX`。
該目錄已忽略提交，避免在儲存庫再保存一份相同來源；`reuse-manifest.json` 記錄來源路徑、SHA-256 和 `downloadedFiles: 0`。
雙橋專用的 8 個預製件與材質也在此目錄生成。

初次開啟 Editor 時自動準備；建置與客戶端測試也會重新準備。
手動入口為 **Tower Duel → Prepare reused combat effects**。所需內建模組為 `com.unity.modules.particlesystem@1.0.0`。

第三方授權沿用原紀錄：Cartoon FX、Hun0FX 為 Unity Asset Store EULA，Sugawara 刀光允許遊戲商用與修改，
不允許將原始素材另作素材包再分發。生成匯入鏡像不改變原素材授權。

## 時間與資源上限

- 法術與交戰粒子由 SDK `RenderTick` 手動推進；快照停頓或恢復連線時不自行外推命中。
- 命中特效以 `hitTick` 去重；同一快照不會重播刀光、爆炸或破塔。
- 弓箭／炸彈只在伺服器確認完成攻擊後建立，使用相同呈現時間軸，在確認的命中 tick 抵達；晚到時直接呈現命中。
- 角色位置、傷害、部署時間、冰霜範圍與數值仍由既有規則和伺服器處理。
- 短暫效果使用 48 個槽位的惰性物件池：命中 12、刀光 12、重擊 6、爆炸 6、死亡 8、破塔 4。
  法術持續效果另依目前有效的法術建立並在結束時回收。
- 比賽結束後只讓最後的碎石／煙團短暫播完；重賽、離開與恢復快照會清除舊呈現。

## 驗證入口

```powershell
./examples/tower-duel/scripts/Test-Client.ps1
./examples/tower-duel/scripts/Build.ps1 -Target WebGL
```

客戶端測試含既有連線／部署／冰霜邊界驗證，以及命中特效去重、Shader／材質、粒子時鐘暫停、倒轉重播、
物件池上限、法術到期及重賽清除。**Tower Duel → Capture combat effects review** 會使用真實 Unity 場景和攝影機
產生一次性示範截圖，輸出到 `artifacts/tower-duel/vfx/native`；不儲存示範單位或修改場景檔案。

### 本輪結果

- Unity 6000.6.8f1：客戶端網路／部署回歸、新增命中去重與粒子測試皆通過。
- `.meta`／GUID／腳本引用：538 個檔案，0 錯誤、0 警告；最後 Unity Console 為 0 錯誤、0 警告。
- 雙人及單人 release WebGL：皆建置成功，0 錯誤。
- Chrome WebGL：11 項特效／SDK 連線檢查通過；火球到期後畫面殘留差異為 0 像素。
- 真實 Go 後端：單人練習、自由測試、桌面／觸控部署、重連、重整恢復、離開清除等 9 項檢查通過，瀏覽器錯誤為 0。

報告與截圖位於 `artifacts/tower-duel/vfx`。本機單人入口為 `http://127.0.0.1:18211/`，
雙人入口為 `http://127.0.0.1:18210/`。正式站使用既有合併部署流程更新，實際發布版本記錄於部署產物 manifest。
