#!/bin/sh
set -eu
release=${1:?release required}
case "$release" in *[!A-Za-z0-9-]*|'') echo 'Invalid release' >&2; exit 1;; esac
base=/opt/rumblebound/tower-duel
next=$base/releases/$release
site=$base/tower-duel.caddy
legacy=$base/tower-duel-solo.caddy
test "$(id -u)" = 0
test "$(uname -m)" = x86_64
test -f "$next/tower-duel-api"
test -f "$next/tower-duel-modes"
test -f "$next/public/tower-duel-build.txt"
test -f "$next/modes-public/solo-build.txt"
test -f /etc/systemd/system/tower-duel.service
grep -Fqx 'import /srv/tower-duel/*.caddy' /opt/rumblebound/Caddyfile
test "$(docker inspect -f '{{index .Config.Labels "app"}}' rumblebound-web)" = rumblebound
exec 9>/run/lock/tower-duel-modes.lock
flock -n 9
(cd "$next" && sha256sum -c SHA256SUMS)
if ss -H -ltn 'sport = :18112' | grep -q . && ! systemctl is-active --quiet tower-duel-solo-web.service && ! systemctl is-active --quiet tower-duel-modes.service; then
    echo 'Port 18112 is used by an unrelated process' >&2; exit 1
fi
previous=$(readlink "$base/current")
test -n "$previous"
cp "$site" "$next/previous.site"
if [ -f "$legacy" ]; then cp "$legacy" "$next/previous.legacy"; fi
if [ -f /etc/systemd/system/tower-duel-modes.service ]; then cp /etc/systemd/system/tower-duel-modes.service "$next/previous.modes.service"; fi
for unit in tower-duel-modes tower-duel-solo-api tower-duel-solo-web; do
    if systemctl is-active --quiet "$unit.service"; then touch "$next/active.$unit"; fi
    if systemctl is-enabled --quiet "$unit.service"; then touch "$next/enabled.$unit"; fi
done
changed=0
reloaded=0
rollback() {
    result=$?
    trap - EXIT
    if [ "$result" != 0 ]; then
        echo 'Merged deployment failed; restoring previous Tower Duel release and routes' >&2
        if [ "$changed" = 1 ]; then
            systemctl stop tower-duel-modes.service || true
            ln -sfn "$previous" "$base/current"
            if [ -f "$next/previous.modes.service" ]; then cp "$next/previous.modes.service" /etc/systemd/system/tower-duel-modes.service;
            else systemctl disable tower-duel-modes.service >/dev/null 2>&1 || true; rm -f /etc/systemd/system/tower-duel-modes.service; fi
            cp "$next/previous.site" "$site"
            if [ -f "$next/previous.legacy" ]; then cp "$next/previous.legacy" "$legacy"; else rm -f "$legacy"; fi
            systemctl daemon-reload
            systemctl restart tower-duel.service || true
            for unit in tower-duel-modes tower-duel-solo-api tower-duel-solo-web; do
                if [ -f "$next/enabled.$unit" ]; then systemctl enable "$unit.service" >/dev/null 2>&1 || true; fi
                if [ -f "$next/active.$unit" ]; then systemctl start "$unit.service" || true; fi
            done
            if [ "$reloaded" = 1 ]; then docker kill --signal=USR1 rumblebound-web >/dev/null; fi
        fi
    fi
    exit "$result"
}
trap rollback EXIT
find "$next" -type d -exec chmod 755 {} +
find "$next" -type f -exec chmod 644 {} +
chmod 755 "$next/tower-duel-api" "$next/tower-duel-modes"
changed=1
systemctl stop tower-duel-solo-web.service tower-duel-solo-api.service tower-duel-modes.service 2>/dev/null || true
ln -sfn "releases/$release" "$base/current.next"
mv -Tf "$base/current.next" "$base/current"
cp "$next/tower-duel-modes.service" /etc/systemd/system/tower-duel-modes.service
systemctl daemon-reload
systemctl restart tower-duel.service
systemctl enable tower-duel-modes.service
systemctl restart tower-duel-modes.service
ready=0
for attempt in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do
    if curl -fsS http://127.0.0.1:18110/healthz >/dev/null && curl -fsS http://127.0.0.1:18112/solo/config | grep -q tower-duel-solo; then ready=1; break; fi
    sleep 1
done
if [ "$ready" != 1 ]; then journalctl -u tower-duel -u tower-duel-modes -n 30 --no-pager; exit 1; fi
cp "$next/site.merged.caddy" "$site"
cp "$next/site.legacy.caddy" "$legacy"
docker exec rumblebound-web caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
reloaded=1
docker kill --signal=USR1 rumblebound-web >/dev/null
systemctl disable tower-duel-solo-api.service tower-duel-solo-web.service >/dev/null 2>&1 || true
curl -fsS -H 'X-Forwarded-Proto: https' -H 'X-Forwarded-For: 127.0.0.1' http://127.0.0.1:18179/healthz >/dev/null
echo "Merged Tower Duel release $release activated; optional modes can be disabled with $base/current/disable-modes.sh"
