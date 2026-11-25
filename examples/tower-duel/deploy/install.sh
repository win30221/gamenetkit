#!/bin/sh
# Deploy multiplayer-only beside Rumblebound; retire optional Solo modes safely.
set -eu
release=${1:?release required}
case "$release" in *[!A-Za-z0-9-]*|'') echo 'Invalid release' >&2; exit 1;; esac
base=/opt/rumblebound/tower-duel
next=$base/releases/$release
proxy=/opt/rumblebound/Caddyfile
site=$base/tower-duel.caddy
legacy_site=$base/tower-duel-solo.caddy
optional_units='tower-duel-modes tower-duel-solo-api tower-duel-solo-web'
test "$(id -u)" = 0
test "$(uname -m)" = x86_64
test -f "$next/tower-duel-api"
test -f "$next/public/Build/WebGL.wasm.gz"
test -f "$proxy"
test "$(docker inspect -f '{{index .Config.Labels "app"}}' rumblebound-web)" = rumblebound
# Serialize with Deploy-Merged and Disable-Modes (both use this lock).
exec 9>/run/lock/tower-duel-modes.lock
flock -n 9
find "$next" -type d -exec chmod 755 {} +
find "$next" -type f -exec chmod 644 {} +
chmod 755 "$next/tower-duel-api"
previous=$(readlink "$base/current" || true)
mode=${2:-full}
case "$mode" in full|static|client) ;; *) echo 'Invalid deployment mode' >&2; exit 1;; esac

# Publish exact staged content before exposing any page that references it.
# Retain past hashes for in-flight downloads, older open pages and rollback.
# The fixed Build paths remain in the release for legacy diagnostics and comparisons.
publish_core_assets() (
    set -eu
    cd "$next"
    names='WebGL.loader.js WebGL.framework.js.gz WebGL.data.gz WebGL.wasm.gz'
    test -f core-assets.sha256
    test -f public/index.html
    expected=$(for name in $names; do sha256sum "public/Build/$name" || exit 1; done)
    if [ "$expected" != "$(cat core-assets.sha256)" ]; then
        echo 'Core asset checksum receipt does not match the staged Player' >&2
        exit 1
    fi
    for name in $names; do
        digest=$(sha256sum "public/Build/$name")
        digest=$(printf '%s' "$digest" | cut -d ' ' -f 1)
        if ! grep -Fq "/assets/core/$digest/$name" public/index.html; then
            echo "Core URL missing from staged page: $name" >&2
            exit 1
        fi
        target="$base/assets/core/$digest/$name"
        if [ -e "$target" ]; then
            if ! cmp -s "$target" "public/Build/$name"; then
                echo "Corrupt immutable core asset: $target (refusing to overwrite)" >&2
                exit 1
            fi
            continue
        fi
        mkdir -p "$(dirname "$target")"
        chmod 755 "$base/assets" "$base/assets/core" "$(dirname "$target")"
        temporary=$(mktemp "$(dirname "$target")/.publish.XXXXXX")
        if ! install -m 644 "public/Build/$name" "$temporary" || ! mv -T "$temporary" "$target"; then
            rm -f "$temporary"
            exit 1
        fi
    done
)

if [ "$mode" = static ] || [ "$mode" = client ]; then
    test -n "$previous"
    # Partial releases may only update an installation that is already multiplayer-only.
    # Otherwise the old routes/services could survive without a full migration.
    if [ -e "$legacy_site" ]; then
        echo 'Optional Solo route still exists; use a full deployment to remove it' >&2
        exit 1
    fi
    for unit in $optional_units; do
        if systemctl is-active --quiet "$unit.service" || systemctl is-enabled --quiet "$unit.service"; then
            echo "Optional service $unit is still active or enabled; use a full deployment" >&2
            exit 1
        fi
    done
    # Keep the authoritative service and protocol identical during client updates.
    unchanged='tower-duel-api protocol/map.json protocol/rules.json site.caddy public/transport.js'
    if [ "$mode" = static ]; then
        unchanged="$unchanged public/Build/WebGL.loader.js public/Build/WebGL.framework.js.gz public/Build/WebGL.data.gz public/Build/WebGL.wasm.gz"
    fi
    for asset in $unchanged; do
        if ! cmp -s "$base/current/$asset" "$next/$asset"; then
            echo "Deployment without server restart rejected: $asset changed" >&2
            exit 1
        fi
    done
    publish_core_assets
    ln -sfn "releases/$release" "$base/current.next"
    mv -Tf "$base/current.next" "$base/current"
    if ! curl -fsS http://127.0.0.1:18110/healthz >/dev/null; then
        ln -sfn "$previous" "$base/current"
        exit 1
    fi
    echo "Tower Duel $mode release $release activated without restarting the game"
    exit 0
fi

publish_core_assets

# Save the state of *both* the core deployment and previous optional modes.
cp "$proxy" "$next/previous.Caddyfile"
if [ -f "$site" ]; then cp "$site" "$next/previous.site"; fi
if [ -f "$legacy_site" ]; then cp "$legacy_site" "$next/previous.legacy.site"; fi
if [ -f /etc/systemd/system/tower-duel.service ]; then cp /etc/systemd/system/tower-duel.service "$next/previous.service"; fi
for unit in $optional_units; do
    if systemctl is-active --quiet "$unit.service"; then : > "$next/was-active.$unit"; fi
    if systemctl is-enabled --quiet "$unit.service"; then : > "$next/was-enabled.$unit"; fi
done
service_changed=0
site_changed=0
proxy_changed=0
reload_attempted=0
optional_changed=0
rollback() {
    code=$?
    trap - EXIT
    if [ "$code" -ne 0 ]; then
        echo 'Deployment failed; restoring Tower Duel, optional services and proxy configuration' >&2
        if [ "$service_changed" = 1 ]; then
            if [ -n "$previous" ]; then ln -sfn "$previous" "$base/current"; else rm -f "$base/current"; fi
            if [ -f "$next/previous.service" ]; then
                cp "$next/previous.service" /etc/systemd/system/tower-duel.service
                systemctl daemon-reload || true
                systemctl restart tower-duel.service || true
            else
                systemctl disable --now tower-duel.service || true
            fi
        fi
        if [ "$site_changed" = 1 ]; then
            if [ -f "$next/previous.site" ]; then cp "$next/previous.site" "$site"; else rm -f "$site"; fi
            if [ -f "$next/previous.legacy.site" ]; then cp "$next/previous.legacy.site" "$legacy_site"; else rm -f "$legacy_site"; fi
        fi
        if [ "$proxy_changed" = 1 ]; then cat "$next/previous.Caddyfile" > "$proxy"; fi
        if [ "$reload_attempted" = 1 ]; then docker kill --signal=USR1 rumblebound-web >/dev/null || true; fi
        if [ "$optional_changed" = 1 ]; then
            for unit in $optional_units; do
                if [ -f "$next/was-enabled.$unit" ]; then systemctl enable "$unit.service" >/dev/null 2>&1 || true; fi
                if [ -f "$next/was-active.$unit" ]; then
                    if [ "$unit" = tower-duel-modes ]; then
                        systemctl restart "$unit.service" || true
                    else
                        systemctl start "$unit.service" || true
                    fi
                fi
            done
        fi
    fi
    exit "$code"
}
trap rollback EXIT

site_changed=1
cp "$next/site.caddy" "$site"
rm -f "$legacy_site"
cp "$proxy" "$next/Caddyfile.candidate"
if ! grep -Fqx 'import /srv/tower-duel/*.caddy' "$next/Caddyfile.candidate"; then
    printf '\nimport /srv/tower-duel/*.caddy\n' >> "$next/Caddyfile.candidate"
fi
# Validation sees the new multiplayer-only route, not the stale Solo redirect.
docker exec rumblebound-web caddy validate --config "/srv/tower-duel/releases/$release/Caddyfile.candidate" --adapter caddyfile
id towerduel >/dev/null 2>&1 || useradd --system --home-dir /var/lib/tower-duel --shell /usr/sbin/nologin towerduel
cat > /etc/systemd/system/tower-duel.service <<UNIT
[Unit]
Description=Tower Duel authoritative game server
After=network.target
[Service]
Type=simple
User=towerduel
Group=towerduel
WorkingDirectory=$base/current
ExecStart=$base/current/tower-duel-api -http 127.0.0.1:18110 -map protocol/map.json -rules protocol/rules.json -transport-runtime public/transport.js
Restart=on-failure
RestartSec=3
TimeoutStopSec=10
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
[Install]
WantedBy=multi-user.target
UNIT
# Do not reload the shared proxy unless the active routes really changed.
if ! cmp -s "$next/Caddyfile.candidate" "$proxy" ||
   ! cmp -s "$next/site.caddy" "$next/previous.site" ||
   [ -f "$next/previous.legacy.site" ]; then
    if ! cmp -s "$next/Caddyfile.candidate" "$proxy"; then
        # Keep the bind-mounted proxy file inode stable.
        proxy_changed=1
        cat "$next/Caddyfile.candidate" > "$proxy"
    fi
    reload_attempted=1
    docker kill --signal=USR1 rumblebound-web >/dev/null
fi
# The new content-addressed route must be active before the new page is exposed.
# A running modes process can refer to the previous release. Restore it on failure.
optional_changed=1
service_changed=1
ln -sfn "releases/$release" "$base/current.next"
mv -Tf "$base/current.next" "$base/current"
systemctl daemon-reload
systemctl enable tower-duel.service
systemctl restart tower-duel.service
ready=0
for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if curl -fsS http://127.0.0.1:18110/healthz >/dev/null 2>&1; then ready=1; break; fi
    sleep 1
done
if [ "$ready" != 1 ]; then journalctl -u tower-duel -n 20 --no-pager; exit 1; fi
curl -fsS -H 'X-Forwarded-Proto: https' -H 'X-Forwarded-For: 127.0.0.1' http://127.0.0.1:18179/healthz >/dev/null

# Retire optional services only after the multiplayer route and API are healthy.
for unit in $optional_units; do
    if systemctl is-active --quiet "$unit.service" || systemctl is-enabled --quiet "$unit.service"; then
        systemctl disable --now "$unit.service"
    fi
    if systemctl is-active --quiet "$unit.service" || systemctl is-enabled --quiet "$unit.service"; then
        echo "Optional service $unit was not fully disabled" >&2
        exit 1
    fi
done
test ! -e "$legacy_site"
echo "Tower Duel multiplayer-only release $release installed; optional modes disabled and legacy redirect removed"
