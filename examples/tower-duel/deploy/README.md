# Tower Duel multiplayer-only deployment

Deploying with `examples/tower-duel/scripts/Deploy.ps1` now switches a merged
Practice/Sandbox/Multiplayer installation to **multiplayer-only** in the same
operation. It packages only the normal WebGL player and authoritative Go API.

From the repository root in PowerShell 7:

```powershell
.\examples\tower-duel\scripts\Deploy.ps1 `
  -VmHost 172.233.67.181 `
  -Domain tower-duel.172.233.67.181.sslip.io `
  -IdentityFile "$HOME\.ssh\id_rsa"
```

The remote `install.sh` validates the new Caddy routes, activates the normal
server, verifies HTTP health, removes the legacy Solo redirect, and disables
`tower-duel-modes`, `tower-duel-solo-api`, and `tower-duel-solo-web` services.
Old release directories remain available for recovery. Existing unrelated
Rumblebound configuration is preserved; Caddy is reloaded only when needed.
If deployment fails, the installer attempts to restore the previous release,
routes and service states.

`-StaticOnly` and `-ClientOnly` remain available for later multiplayer-only
updates. They explicitly reject installations with active/enabled Solo services
or a remaining legacy redirect: run a full deploy to migrate first.

**Do not run `solo/Disable-Modes.ps1` after deploying multiplayer-only.** That
command is for releases created by `solo/Deploy-Merged.ps1` and requires its
bundled `disable-modes.sh`. A normal release intentionally does not carry it.

Run isolated installation/rollback tests (in WSL or Linux):

```sh
sh examples/tower-duel/deploy/test-install.sh
```

These tests use temporary fixture directories and mocked `systemctl`, Docker,
and health checks. They do not contact the production VM.
