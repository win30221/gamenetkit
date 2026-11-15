# 血條與拖牌修正 · 2026-11-13

最新公開版本：`20261113T074624Z-7cae3c74`。[開啟雙橋對決](https://tower-duel.172.233.67.181.sslip.io/)。最近合法落點修正與驗證見本文末段；後續房間退出修正見 [等待房間退出驗證](ROOM_EXIT_VALIDATION.md)。
下列保留版本 `20261113T024139Z-99d819bc` 的血條與拖牌驗證紀錄；當時使用 client-only 部署，12 個公開部署檔案均與 SHA-256 清單一致。

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

## 最近合法落點修正（2026-11-13，已部署）

- 拖到河道、建築、未開放區或場外時，模型與金色落點吸附距離瞄準位置最近的合法格；放手使用相同座標。
- 小隊會檢查全部編隊成員，拆塔開放區與重賽會立即影響落點。能量不足仍阻止出牌；短拖與拖回牌區維持取消。
- 法術維持自由瞄準，超出戰場時落點停在邊界。
- Unity 6000.6.8f1 客戶端回歸通過，新增兩席的河岸、建築、邊界、小隊、拆塔、重賽、能量不足、非有限座標與無合法格測試。
- WebGL release 建置成功，零建置錯誤；306 個 Unity 資產的 GUID／腳本引用檢查零錯誤、零警告。
- 真實 Chrome 滑鼠／觸控事件 fixture 通過兩席共 30 項檢查，驗證送出的最近合法座標，兩端無 browser page error；另目視確認桌面與手機的模型停在合法格。

產物位於 `artifacts/tower-duel/nearest-deployment/`：`client-tests/result.json` 與 `feedback-verified/result.json`，後者同目錄包含各情境截圖。
已於 2026-11-13 15:14（台灣時間）以 client-only 模式更新公開站，版本 `20261113T071419Z-9f94fba8`。
12 個部署檔案均與 SHA-256 清單一致，HTTPS 健康檢查、協定與地圖／規則雜湊通過；權威服務 PID `3110574` 維持不變。
公開網址重跑滑鼠／觸控 fixture，兩席共 30 項檢查全數通過，兩端無 browser page error；報告與截圖位於 `public-web/feedback/`。
部署與檔案核對紀錄位於 `artifacts/tower-duel/nearest-deployment/public-web/`。
