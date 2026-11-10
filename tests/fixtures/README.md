# 跨語言 Codec 測試資料

| 檔案 | 用途 |
| --- | --- |
| [codec.json](codec.json) | 五組正式 Go codec 編碼與原文，供 Unity SDK 測試及 C# codec 課程解碼比對。 |
| [generate-codec/main.go](generate-codec/main.go) | 產生上述測試資料；不參與伺服器或遊戲啟動。 |

一般測試直接讀取已保存的 JSON。修改 codec 格式或案例後，從 gamenetkit 根目錄重新產生：

```powershell
. ./tools/internal/Go-Env.ps1
& $Go -C ./server run ../tests/fixtures/generate-codec/main.go
./examples/modules/client/Run.ps1 -Lesson codec
./tests/scripts/Test-UnitySdk.ps1
```

產生器在 server 的 Go 模組環境執行，引用正式 codec，不增加獨立 Go module。預設輸出相對於 server 的 ../tests/fixtures/codec.json；可用 -out 指定其他檔案進行比較。
