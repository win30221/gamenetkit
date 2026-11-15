#!/bin/sh
set -eu
prefix=${TOWER_DUEL_TEST_ROOT:-}
base=$prefix/opt/rumblebound/tower-duel
site=$base/tower-duel.caddy
current=$(readlink -f "$base/current")
case "$current" in "$base"/releases/*) ;; *) echo 'Unexpected current release path' >&2; exit 1;; esac
test "$(id -u)" = 0
test -f "$current/site.normal.caddy"
test -f "$current/public/tower-duel-build.txt"
test -f "$current/public/Build/WebGL.wasm.gz"
exec 9>"$prefix/run/lock/tower-duel-modes.lock"
flock -n 9
pid=$(systemctl show -p MainPID --value tower-duel.service)
active=0
enabled=0
if systemctl is-active --quiet tower-duel-modes.service; then active=1; fi
if systemctl is-enabled --quiet tower-duel-modes.service; then enabled=1; fi
backup=$(mktemp "$base/disable-modes.XXXXXX")
cp "$site" "$backup"
changed=0
rollback() {
    result=$?
    trap - EXIT
    if [ "$result" != 0 ]; then
        cp "$backup" "$site"
        if [ "$enabled" = 1 ]; then systemctl enable tower-duel-modes.service >/dev/null 2>&1 || true; fi
        if [ "$active" = 1 ]; then systemctl start tower-duel-modes.service || true; fi
        if [ "$changed" = 1 ]; then docker kill --signal=USR1 rumblebound-web >/dev/null; fi
    fi
    exit "$result"
}
trap rollback EXIT
cp "$current/site.normal.caddy" "$site"
docker exec rumblebound-web caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
changed=1
docker kill --signal=USR1 rumblebound-web >/dev/null
curl -fsS http://127.0.0.1:18110/healthz >/dev/null
test "$(systemctl show -p MainPID --value tower-duel.service)" = "$pid"
systemctl disable --now tower-duel-modes.service
echo 'Single-player modes disabled; the same website now serves the bundled multiplayer-only player. Refresh the page.'
