#!/bin/sh
set -eu
case "${1:-}" in ''|--purge) ;; *) echo 'Usage: uninstall.sh [--purge]' >&2; exit 1;; esac
test "$(id -u)" = 0
base=/opt/rumblebound/tower-duel-solo
site=/opt/rumblebound/tower-duel/tower-duel-solo.caddy
original_pid=$(systemctl show -p MainPID --value tower-duel.service)
exec 9>/run/lock/tower-duel-solo-deploy.lock
flock -n 9
if [ -f "$site" ]; then
    cp "$site" "$base/removed-site.backup"
    rm -f "$site"
    if ! docker exec rumblebound-web caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile; then
        cp "$base/removed-site.backup" "$site"; exit 1
    fi
    docker kill --signal=USR1 rumblebound-web >/dev/null
fi
for name in tower-duel-solo-web tower-duel-solo-api; do
    systemctl disable --now "$name.service" >/dev/null 2>&1 || true
    rm -f "/etc/systemd/system/$name.service"
done
systemctl daemon-reload
systemctl reset-failed tower-duel-solo-web.service tower-duel-solo-api.service >/dev/null 2>&1 || true
test "$(systemctl show -p MainPID --value tower-duel.service)" = "$original_pid"
curl -fsS http://127.0.0.1:18110/healthz >/dev/null
if [ "${1:-}" = --purge ]; then
    test "$base" = /opt/rumblebound/tower-duel-solo
    test ! -L "$base"
    rm -rf -- "$base"
fi
echo 'Removed the solo test site and services; original multiplayer service retained'
