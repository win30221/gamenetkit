#!/bin/sh
# Run only in an empty container with no production mounts and no network.
set -eu
mkdir -p /tmp/solo-test-bin /run/lock /etc/systemd/system
mkdir -p /opt/rumblebound/tower-duel/current /opt/rumblebound/tower-duel-solo/releases/fixture
printf 'original proxy\n' > /opt/rumblebound/Caddyfile
printf 'original multiplayer site\n' > /opt/rumblebound/tower-duel/tower-duel.caddy
printf 'original game binary\n' > /opt/rumblebound/tower-duel/current/tower-duel-api
printf 'original game unit\n' > /etc/systemd/system/tower-duel.service
printf 'solo site\n' > /opt/rumblebound/tower-duel/tower-duel-solo.caddy
touch /etc/systemd/system/tower-duel-solo-api.service /etc/systemd/system/tower-duel-solo-web.service
ln -s releases/fixture /opt/rumblebound/tower-duel-solo/current
sha256sum /opt/rumblebound/Caddyfile /opt/rumblebound/tower-duel/tower-duel.caddy /opt/rumblebound/tower-duel/current/tower-duel-api /etc/systemd/system/tower-duel.service > /tmp/original-files.sha256
cat > /tmp/solo-test-bin/systemctl <<'MOCK'
#!/bin/sh
if [ "$1" = show ]; then printf '4242\n'; exit 0; fi
case "$*" in *tower-duel.service*) echo 'Attempted to mutate original game' >&2; exit 99;; esac
printf 'systemctl %s\n' "$*" >> /tmp/solo-test-commands
MOCK
cat > /tmp/solo-test-bin/docker <<'MOCK'
#!/bin/sh
if [ "$1" = exec ] && [ "${FAKE_CADDY_FAIL:-0}" = 1 ]; then exit 1; fi
printf 'docker %s\n' "$*" >> /tmp/solo-test-commands
MOCK
cat > /tmp/solo-test-bin/curl <<'MOCK'
#!/bin/sh
printf 'ok\n'
MOCK
cat > /tmp/solo-test-bin/flock <<'MOCK'
#!/bin/sh
exit 0
MOCK
chmod 755 /tmp/solo-test-bin/*
export PATH=/tmp/solo-test-bin:$PATH
if FAKE_CADDY_FAIL=1 sh /test/uninstall.sh; then echo 'Expected configuration rejection' >&2; exit 1; fi
test -f /opt/rumblebound/tower-duel/tower-duel-solo.caddy
sha256sum -c /tmp/original-files.sha256
sh /test/uninstall.sh
test ! -f /opt/rumblebound/tower-duel/tower-duel-solo.caddy
test ! -f /etc/systemd/system/tower-duel-solo-api.service
test ! -f /etc/systemd/system/tower-duel-solo-web.service
test -d /opt/rumblebound/tower-duel-solo/releases/fixture
sha256sum -c /tmp/original-files.sha256
sh /test/uninstall.sh --purge
test ! -e /opt/rumblebound/tower-duel-solo
sha256sum -c /tmp/original-files.sha256
echo 'PASS: removal, purge and config-rejection rollback preserve original game fixtures'
