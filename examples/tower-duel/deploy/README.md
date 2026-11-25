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


## Content-addressed WebGL core (P1-1)

Rebuild WebGL before deploying this version. The first migration requires a full
deploy (no -StaticOnly or -ClientOnly), because the shared Caddy route changes.
The normal commands remain the same.

Packaging rewrites the staged multiplayer index to use
/assets/core/<sha256>/WebGL.* and writes core-assets.sha256 and
public/core-assets.json. SHA-256 is calculated on the exact transferred bytes,
including gzip. The local build and optional-mode packages retain relative
Build paths. The installed release also retains its fixed Build files for
existing diagnostics and deployment comparisons.

The installer verifies the receipt and page references, publishes missing
assets under /opt/rumblebound/tower-duel/assets/core/<sha256>/ before activation,
and refuses to overwrite an existing corrupt immutable path. Old hashes are
intentionally retained for older tabs and rollback; no automatic garbage
collection is enabled. Monitor disk use before adding a retention policy.

Caddy keeps the page, manifest and legacy Build paths revalidated; only valid
content-addressed core URLs receive a one-year immutable response policy on
successful responses. The Unity loader also uses immutable for those same-origin
URLs. The first load after migration downloads the new URLs once; unchanged URLs
can be reused on subsequent releases, subject to browser cache limits.

To inspect the active manifest:
  Invoke-RestMethod "https://YOUR_DOMAIN/core-assets.json"

Compare URLs before and after a rebuild, and use Chrome Network with Disable
cache unchecked. Check a hashed resource's response headers and reload
behavior. A changed data file still needs a full download: AssetBundle splitting
and preloading are a separate follow-up, not part of P1-1.


## WebGL unit animation bundles (P1-2)

The normal multiplayer Build, Run and Deploy commands are unchanged. The first
deployment of this Caddy route requires a full deploy. The source definitions
and baked animation atlases now live at client/Assets/TowerDuelArt/Units;
their Unity metadata/GUIDs move with them. Use the existing prepare/bake
commands to update them. Do not recreate the old Resources/B3/Units folder.

Only the three idle/walk/attack textures of each unit form are split.
Prefabs, sprite-set metadata, materials, shaders, card art, map and effects
stay in the Player for this iteration. Unity builds LZ4 texture-only bundles,
embeds a pinned manifest in the WebGL Player, and temporarily detaches the
atlas references. It verifies the Player build report before publishing the
bundles and restores the Editor references in a finally/Dispose scope. If the
Editor is interrupted during a build, run Tower Duel > Prepare playable scene
before testing or building a Windows Player.

WebGL opens the existing lobby while downloading units sequentially; an
inspected card temporarily shows its illustration and prioritizes its animation
next. All unit forms must load before matchmaking, creating/joining rooms, or
restoring a saved session. A failed download retries up to three times and then
offers a manual retry; successful atlases remain loaded for the session.
Editor/Windows retain direct references and require no downloads.

The build produces unit-bundles.json and assets/bundles/<sha256>/<kind>.bundle.
The build receipt binds the manifest; packaging validates all bundle bytes.
The VM installs missing hashes under the shared assets/bundles directory before
switching the active release; old hashes remain available for older clients.
The browser can reuse unchanged URLs. Deployment still uploads the complete
archive; server-side publication is deduplicated, not network upload.
No automatic garbage collection is enabled.

Read client/Builds/web-build-size.json to compare startupTransferBytes with
unitBundleBytes; the latter is loaded after the lobby appears. Use the Network
panel to verify cache behavior, matching unit animations, both griffin forms,
deck editing during loading, retry on failed requests, and reconnect behavior.
Verify the Windows/Editor build as well. A slow cold-cache reconnect can still
exceed the server's existing resume grace period.
