#!/bin/sh
# Extend the already installed Rumblebound proxy without restarting it.
set -eu
release=${1:?release required}
case "$release" in *[!A-Za-z0-9-]*|'') echo 'Invalid release' >&2; exit 1;; esac
base=/opt/rumblebound/tower-duel
next=$base/releases/$release
proxy=/opt/rumblebound/Caddyfile
test "$(id -u)" = 0
test "$(uname -m)" = x86_64
test -f "$next/tower-duel-api"
test -f "$next/public/Build/WebGL.wasm.gz"
test -f "$proxy"
test "$(docker inspect -f '{{index .Config.Labels "app"}}' rumblebound-web)" = rumblebound
find "$next" -type d -exec chmod 755 {} +
find "$next" -type f -exec chmod 644 {} +
chmod 755 "$next/tower-duel-api"
previous=$(readlink "$base/current" || true)
mode=${2:-full}
case "$mode" in full|static|client) ;; *) echo 'Invalid deployment mode' >&2; exit 1;; esac
if [ "$mode" = static ] || [ "$mode" = client ]; then
    test -n "$previous"
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
    ln -sfn "releases/$release" "$base/current.next"
    mv -Tf "$base/current.next" "$base/current"
    if ! curl -fsS http://127.0.0.1:18110/healthz >/dev/null; then
        ln -sfn "$previous" "$base/current"
        exit 1
    fi
    echo "Tower Duel $mode release $release activated without restarting the game"
    exit 0
fi
cp "$proxy" "$next/previous.Caddyfile"
if [ -f "$base/tower-duel.caddy" ]; then cp "$base/tower-duel.caddy" "$next/previous.site"; fi
if [ -f /etc/systemd/system/tower-duel.service ]; then cp /etc/systemd/system/tower-duel.service "$next/previous.service"; fi
service_changed=0
proxy_changed=0
rollback() {
    code=$?
    trap - EXIT
    if [ "$code" -ne 0 ]; then
        echo 'Deployment failed; restoring Tower Duel and proxy configuration' >&2
        if [ -n "$previous" ]; then ln -sfn "$previous" "$base/current"; fi
        if [ "$service_changed" = 1 ]; then
            if [ -f "$next/previous.service" ]; then
                cp "$next/previous.service" /etc/systemd/system/tower-duel.service
                systemctl daemon-reload
                systemctl restart tower-duel.service
            else
                systemctl disable --now tower-duel.service || true
            fi
        fi
        if [ -f "$next/previous.site" ]; then
            cp "$next/previous.site" "$base/tower-duel.caddy"
        else
            rm -f "$base/tower-duel.caddy"
        fi
        if [ "$proxy_changed" = 1 ]; then
            cat "$next/previous.Caddyfile" > "$proxy"
            docker kill --signal=USR1 rumblebound-web >/dev/null
        fi
    fi
    exit "$code"
}
trap rollback EXIT
cp "$next/site.caddy" "$base/tower-duel.caddy"
cp "$proxy" "$next/Caddyfile.candidate"
if ! grep -Fqx 'import /srv/tower-duel/*.caddy' "$next/Caddyfile.candidate"; then
    printf '\nimport /srv/tower-duel/*.caddy\n' >> "$next/Caddyfile.candidate"
fi
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
ln -sfn "releases/$release" "$base/current.next"
mv -Tf "$base/current.next" "$base/current"
service_changed=1
systemctl daemon-reload
systemctl enable tower-duel.service
systemctl restart tower-duel.service
ready=0
for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if curl -fsS http://127.0.0.1:18110/healthz >/dev/null 2>&1; then ready=1; break; fi
    sleep 1
done
if [ "$ready" != 1 ]; then journalctl -u tower-duel -n 20 --no-pager; exit 1; fi
# A server-only gameplay change does not require reloading the shared proxy.
if ! cmp -s "$next/Caddyfile.candidate" "$proxy" || ! cmp -s "$next/site.caddy" "$next/previous.site"; then
    # Preserve the inode of the file mounted into the running container.
    cat "$next/Caddyfile.candidate" > "$proxy"
    proxy_changed=1
    docker kill --signal=USR1 rumblebound-web >/dev/null
fi
curl -fsS -H 'X-Forwarded-Proto: https' -H 'X-Forwarded-For: 127.0.0.1' http://127.0.0.1:18179/healthz >/dev/null
echo "Tower Duel release $release installed; HTTPS certificate provisioning started"
