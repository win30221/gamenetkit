# Rumblebound 本地啟動與 VM 部署

所有命令從 gamenetkit 根目錄以 PowerShell 7 執行。啟動統一使用 `examples/Run.ps1`；部署工具位於範例自己的 `scripts` 目錄。

```powershell
./examples/Run.ps1 -Example Rumblebound
# 修改 Unity 後先重新建置，再啟動。
./examples/Build-Unity.ps1 -Project RumbleboundExample -Target Web
./examples/Run.ps1 -Example Rumblebound
# 本地原生 Player 與開發用 WebTransport。
./examples/Run.ps1 -Example Rumblebound -Unity -WebTransport

# 換成自己的 VM 與已設定 DNS 的網域。
./examples/rumblebound/scripts/Deploy.ps1 -VmHost your-vm.example.com -Domain game.example.com
# 自訂 SSH 帳號／埠／金鑰。
./examples/rumblebound/scripts/Deploy.ps1 -VmHost your-vm.example.com -Domain game.example.com -SshUser deploy -SshPort 22 -IdentityFile C:/path/to/your_key
# 僅產生部署包，供檢查；不連線 VM。
./examples/rumblebound/scripts/Deploy.ps1 -VmHost your-vm.example.com -Domain game.example.com -PackageOnly
```

本地服務預設 `127.0.0.1:18093`。部署必須明確傳入 `-VmHost` 與 `-Domain`；首次部署需可透過 SSH 登入 Ubuntu x86_64，已安裝 Docker Engine、systemd、curl、openssl，且 TCP 80/443 可到達。非 root 帳號需 `sudo -n` 權限。網域 DNS A 記錄需事先指向 VM，工具不會註冊網域或修改 DNS。

部署時 `-BuildWeb` 會先重新建置 Unity WebGL；省略時使用現有建置，API 則每次重新編譯。

部署範本與遠端安裝腳本位於 `examples/rumblebound/deploy`。

## VM 配置

- 根網址 `/` 直接載入遊戲，無須 `/unity/`。VM 的舊 `/unity/` 入口重新導向根網址；舊資產路徑仍相容。Caddy 提供 Unity 檔案、WASM MIME、壓縮、HTTPS 自動憑證與續期，並轉送 `/api/*`、`/ws`、`/healthz`。
- VM 採用量測過的 WSS；開發用的短效憑證 WebTransport 與 `/diagnostics/` C# 主機仍由本地入口提供，未發布到 VM。其他同步模組維持遊戲原有選項。
- API 使用獨立系統帳號與 `rumblebound.service`。`-environment vm` 要求精確 HTTPS Origin，只接受來自 loopback HTTPS 代理的轉送資訊，保留 Secure cookie 和每個真實客戶端的登入限流。Local 不信任轉送標頭。
- Caddy 容器 `rumblebound-web` 使用 host network 與 restart policy。首次取得官方 `caddy:2-alpine`；實際 image ID 寫入 release，後續部署重用該 image，不隨部署自動升級。
- 正式與開發金鑰分開，VM 在 `/etc/rumblebound.env` 自行生成並保留，不上傳本機金鑰。
- 每次部署建立獨立 release 和檔案 SHA-256 manifest。切換前驗證代理設定；API 啟動失敗會還原上一版；憑證/DNS 外部問題則報錯供修正，不會偽稱 HTTPS 已完成。
- 會停止之前占用 443 的 `network-probe.service`。不改 SSH 或主機防火牆。

## 維護與資料

```sh
systemctl status rumblebound
journalctl -u rumblebound -n 80 --no-pager
docker logs --tail 80 rumblebound-web
systemctl restart rumblebound
```

Release 在 `/opt/rumblebound/releases/`，`/opt/rumblebound/current` 指向目前版本。部署包在本機 `.local/deploy/`；不自動刪除舊版本。Caddy 憑證保存在 Docker volume `rumblebound-caddy-data`，不用每次重新申請。

遊戲仍使用現有記憶體身份／房間／比賽狀態；部署或重啟會結束進行中的比賽，使用者可能需要重新訪客登入。這個部署工具沒有把狀態改成資料庫。伺服器 journal 保存在 `/var/lib/rumblebound/replays`，跨版本保留，未設定自動清理，需依使用量定期匯出／清理及監測磁碟。

## 驗證

```powershell
./examples/rumblebound/scripts/Test-Deployment.ps1 -Origin https://game.example.com
```

檢查實際 HTTPS、Unity loader/WASM MIME、來源拒絕與 Secure cookie，再透過真正 C# SDK 走雙人房間、載入、WSS 移動／攻擊、差分恢復和重連等流程。部署檢查不等於瀏覽器 GPU／人工 UI 或多人容量驗收。

