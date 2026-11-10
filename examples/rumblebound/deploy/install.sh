#!/bin/sh
set -eu
release=${1:?release required}
domain=${2:?domain required}
case "$release" in *[!A-Za-z0-9-]*|'') echo 'Invalid release' >&2; exit 1;; esac
case "$domain" in *[!A-Za-z0-9.-]*|'') echo 'Invalid domain' >&2; exit 1;; esac
base=/opt/rumblebound
next=$base/releases/$release
test "$(id -u)" = 0
test "$(uname -m)" = x86_64
command -v docker >/dev/null || { echo 'Install Docker Engine first' >&2; exit 1; }
docker info >/dev/null
test -f "$next/battle-api"
test -f "$next/public/unity/Build/WebGL.wasm"
find "$next" -type d -exec chmod 755 {} +
find "$next" -type f -exec chmod 644 {} +
# Reuse the installed immutable proxy image after the first deployment.
image=$(docker inspect -f '{{.Image}}' rumblebound-web 2>/dev/null || true)
if [ -n "$image" ]; then
    test "$(docker inspect -f '{{index .Config.Labels "app"}}' rumblebound-web)" = rumblebound
else
    docker pull caddy:2-alpine
    image=$(docker image inspect -f '{{.Id}}' caddy:2-alpine)
fi
docker run --rm -v "$next/Caddyfile:/etc/caddy/Caddyfile:ro" "$image" caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
id rumblebound >/dev/null 2>&1 || useradd --system --home-dir /var/lib/rumblebound --shell /usr/sbin/nologin rumblebound
install -d -o rumblebound -g rumblebound -m 750 /var/lib/rumblebound/replays
if [ ! -f /etc/rumblebound.env ]; then
    umask 077
    printf 'RB_DEV_KEY=%s\n' "$(openssl rand -hex 32)" > /etc/rumblebound.env
fi
chmod 755 "$next/battle-api"
previous=$(readlink "$base/current" || true)
if [ -f /etc/systemd/system/rumblebound.service ]; then
    cp /etc/systemd/system/rumblebound.service "$next/previous.service"
fi
if [ -f "$base/Caddyfile" ]; then cp "$base/Caddyfile" "$next/previous.Caddyfile"; fi
start_proxy() {
    if docker container inspect rumblebound-web >/dev/null 2>&1; then docker rm -f rumblebound-web; fi
    docker run -d --name rumblebound-web --label app=rumblebound --restart unless-stopped --network host \
        --log-opt max-size=10m --log-opt max-file=3 \
        -v "$base:/srv:ro" -v "$base/Caddyfile:/etc/caddy/Caddyfile:ro" \
        -v rumblebound-caddy-data:/data -v rumblebound-caddy-config:/config "$image"
}
rollback() {
    code=$?
    if [ "$code" -ne 0 ] && [ -n "$previous" ]; then
        echo 'Deployment failed; restoring previous release' >&2
        ln -sfn "$previous" "$base/current"
        cp "$next/previous.service" /etc/systemd/system/rumblebound.service
        systemctl daemon-reload
        systemctl restart rumblebound.service
        if [ -f "$next/previous.Caddyfile" ]; then
            cp "$next/previous.Caddyfile" "$base/Caddyfile"
            start_proxy
        fi
    fi
    exit "$code"
}
trap rollback EXIT
cat > /etc/systemd/system/rumblebound.service <<UNIT
[Unit]
Description=Rumblebound game API
After=network.target
[Service]
Type=simple
User=rumblebound
Group=rumblebound
WorkingDirectory=$base/current
EnvironmentFile=/etc/rumblebound.env
ExecStart=$base/current/battle-api -environment vm -http 127.0.0.1:18179 -origins https://$domain -replay-dir /var/lib/rumblebound/replays
Restart=on-failure
RestartSec=3
TimeoutStopSec=15
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=/var/lib/rumblebound
[Install]
WantedBy=multi-user.target
UNIT
if [ -n "$previous" ]; then echo "Previous release: $previous"; fi
ln -sfn "releases/$release" "$base/current.next"
mv -Tf "$base/current.next" "$base/current"
systemctl daemon-reload
systemctl enable rumblebound.service
systemctl restart rumblebound.service
ready=0
for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if curl -fsS -H 'X-Forwarded-Proto: https' -H 'X-Forwarded-For: 127.0.0.1' http://127.0.0.1:18179/healthz >/dev/null 2>&1; then ready=1; break; fi
    sleep 1
done
if [ "$ready" != 1 ]; then
    journalctl -u rumblebound -n 20 --no-pager
    exit 1
fi
cp "$next/Caddyfile" "$base/Caddyfile"
# Retire the temporary speed-test echo service that previously used TCP 443.
systemctl disable --now network-probe.service 2>/dev/null || true
start_proxy
printf '%s\n' "$image" > "$next/proxy-image.txt"
systemctl enable docker
rm -f "/tmp/rumblebound-$release.tar.gz"
echo "API started; HTTPS provisioning: https://$domain/"
