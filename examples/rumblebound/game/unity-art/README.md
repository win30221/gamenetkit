# Rumblebound 共用呈現

此遊戲專用 UPM package 包含角色、動畫、場景、特效與音效，與通用 SDK 分開。`Runtime` 只顯示傳入的狀態與採樣位置，不送出輸入、不計算傷害或決定碰撞。

素材從 sample 搬入時保留 `.meta` GUID，來源記錄見上層 `MIGRATION.json`。原授權與來源說明保留在 `Art`。

中文字體原由舊 sample 的 `Prepare-UIFont.py` 產生；該工具目前不在此工作區。來源、字形數與 OFL 授權記在 `Art/UI/font-manifest.json`。
