#!/bin/sh
set -eu
release=${1:?release required}
case "$release" in *[!A-Za-z0-9-]*|'') echo 'Invalid release' >&2; exit 1;; esac
base=/opt/rumblebound/tower-duel-solo
next=$base/releases/$release
site=/opt/rumblebound/tower-duel/tower-duel-solo.caddy
test "$(id -u)" = 0
test "$(uname -m)" = x86_64
test -f "$next/public/solo-build.txt"
test -f "$next/tower-duel-api"
test -f "$next/tower-duel-solo"
grep -Fqx 'import /srv/tower-duel/*.caddy' /opt/rumblebound/Caddyfile
test "$(docker inspect -f '{{index .Config.Labels "app"}}' rumblebound-web)" = rumblebound
exec 9>/run/lock/tower-duel-solo-deploy.lock
flock -n 9
(cd "$next" && sha256sum -c SHA256SUMS)
original_pid=$(systemctl show -p MainPID --value tower-duel.service)
previous=$(readlink "$base/current" || true)
changed=0
reloaded=0
if [ -f "$site" ]; then cp "$site" "$next/previous.site"; fi
for name in tower-duel-solo-api tower-duel-solo-web; do
    if [ -f "/etc/systemd/system/$name.service" ]; then cp "/etc/systemd/system/$name.service" "$next/previous.$name.service"; fi
done
rollback() {
    result=$?
    trap - EXIT
    if [ "$result" != 0 ]; then
        echo 'Solo deployment failed; restoring only the solo test services and site' >&2
        if [ "$changed" = 1 ]; then systemctl stop tower-duel-solo-web.service tower-duel-solo-api.service || true; fi
        if [ -n "$previous" ]; then ln -sfn "$previous" "$base/current"; else rm -f "$base/current"; fi
        for name in tower-duel-solo-api tower-duel-solo-web; do
            if [ -f "$next/previous.$name.service" ]; then cp "$next/previous.$name.service" "/etc/systemd/system/$name.service";
            else systemctl disable "$name.service" >/dev/null 2>&1 || true; rm -f "/etc/systemd/system/$name.service"; fi
        done
        if [ -f "$next/previous.site" ]; then cp "$next/previous.site" "$site"; else rm -f "$site"; fi
        systemctl daemon-reload
        if [ -n "$previous" ] && [ "$changed" = 1 ]; then systemctl start tower-duel-solo-api.service tower-duel-solo-web.service || true; fi
        if [ "$reloaded" = 1 ]; then docker kill --signal=USR1 rumblebound-web >/dev/null; fi
    fi
    exit "$result"
}
trap rollback EXIT
for port in 18111 18112; do
    unit=tower-duel-solo-api.service
    if [ "$port" = 18112 ]; then unit=tower-duel-solo-web.service; fi
    if ss -H -ltn "sport = :$port" | grep -q . && ! systemctl is-active --quiet "$unit"; then echo "Test port $port is already in use" >&2; exit 1; fi
done
find "$next" -type d -exec chmod 755 {} +
find "$next" -type f -exec chmod 644 {} +
chmod 755 "$next/tower-duel-api" "$next/tower-duel-solo"
cp "$next/site.caddy" "$site"
docker exec rumblebound-web caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
id towerduelsolo >/dev/null 2>&1 || useradd --system --user-group --home-dir /var/lib/tower-duel-solo --shell /usr/sbin/nologin towerduelsolo
cp "$next/tower-duel-solo-api.service" /etc/systemd/system/tower-duel-solo-api.service
cp "$next/tower-duel-solo-web.service" /etc/systemd/system/tower-duel-solo-web.service
ln -sfn "releases/$release" "$base/current.next"
mv -Tf "$base/current.next" "$base/current"
changed=1
systemctl daemon-reload
systemctl enable tower-duel-solo-api.service tower-duel-solo-web.service
systemctl restart tower-duel-solo-api.service tower-duel-solo-web.service
ready=0
for attempt in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do
    if curl -fsS http://127.0.0.1:18112/healthz | grep -q tower-duel-solo; then ready=1; break; fi
    sleep 1
done
if [ "$ready" != 1 ]; then journalctl -u tower-duel-solo-api -u tower-duel-solo-web -n 30 --no-pager; exit 1; fi
docker kill --signal=USR1 rumblebound-web >/dev/null
reloaded=1
test "$(systemctl show -p MainPID --value tower-duel.service)" = "$original_pid"
curl -fsS http://127.0.0.1:18110/healthz >/dev/null
echo "Solo test release $release installed; original multiplayer PID $original_pid retained"
