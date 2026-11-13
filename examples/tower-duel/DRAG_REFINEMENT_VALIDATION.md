# 血條與拖牌修正 · 2026-11-13

公開版本：`20261113T024139Z-99d819bc`。[開啟雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)。
本次使用 client-only 部署，既有伺服器與正在進行的對局維持運作。12 個公開部署檔案均與 SHA-256 清單一致。

## 修正

- 血條依場景的畫面高度縮放。同高度下加寬視窗，血條不會跟著放大；塔與部隊改為細血條，數字維持顯示。
- 卡面從原卡槽位置跟著指標移動，保留按住的位置與尺寸；原位留空，取消時恢復。
- 改用卡牌中心前方的落點判斷模型切換，拖出約卡高的 20% 即可開始轉換，指標不必完全離開整個牌區。
- 進入合法地形後顯示模型；能量不足或移至非法格時保留紅色模型提示，模型與放手採用相同落點。
- 滑鼠與觸控分別調整前移量，桌面不再套用過大的手指偏移。

## 驗證

- Unity 6000.6.8f1 的 WebGL release 與 Windows development 建置成功。
- 最終 UI fixture 通過兩席共 23 項檢查，涵蓋拆塔放置規則與本輪拖牌修正；兩端無 browser page error。
- 以實際畫面像素檢查卡槽下緣已留空、拖出的卡牌沒有跳離抓取位置。
- 在指標仍位於牌區上緣時，預覽已轉換成模型，放手能送出同一落點。
- 1920×1080 與 1366×1080 的血條寬高差不超過 1 像素；填色部分均不超過 100×8 像素。
- 另目視確認 390×844 手機的血量、計時器與拖曳卡面。
- 公開站真實 HTTPS／WSS 雙人對局通過：滑鼠與觸控拖曳、短拖與拖回取消、能量不足、格心落點、重整恢復、七種尺寸與同房重賽皆正常；兩端無 browser page error。

本輪產物位於 `artifacts/tower-duel/drag-refinement/feedback-final/`，包含 `result.json`、`desktop-health-1920-seat0.png`、`card-follows-from-slot-seat0.png`、`model-before-pointer-leaves-dock-seat0.png` 與手機畫面。
Fixture 使用合成快照驗證客戶端放置決策與畫面；實際公開網路對局另存於 `artifacts/tower-duel/drag-refinement/public-web/`。
