#!/bin/sh
set -eu
work=${1:?provide a workspace fixture directory}
mkdir -p "$work"
work=$(cd "$work" && pwd)
fixture=$(mktemp -d "$work/disable.XXXXXX")
case "$fixture" in "$work"/disable.*) ;; *) exit 1;; esac
export TOWER_DUEL_TEST_ROOT=$fixture
base=$fixture/opt/rumblebound/tower-duel
mkdir -p "$base/releases/fixture/public/Build" "$fixture/run/lock" "$fixture/bin" "$fixture/etc/systemd/system"
printf 'original shared proxy\n' > "$fixture/opt/rumblebound/Caddyfile"
printf 'original game unit\n' > "$fixture/etc/systemd/system/tower-duel.service"
printf 'original game executable\n' > "$base/releases/fixture/tower-duel-api"
printf 'multiplayer-only\n' > "$base/releases/fixture/site.normal.caddy"
printf 'normal build\n' > "$base/releases/fixture/public/tower-duel-build.txt"
printf 'wasm fixture\n' > "$base/releases/fixture/public/Build/WebGL.wasm.gz"
printf 'merged modes\n' > "$base/tower-duel.caddy"
sha256sum "$fixture/opt/rumblebound/Caddyfile" "$fixture/etc/systemd/system/tower-duel.service" "$base/releases/fixture/tower-duel-api" > "$fixture/unchanged.sha256"
cat > "$fixture/bin/id" <<'MOCK'
#!/bin/sh
echo 0
MOCK
cat > "$fixture/bin/readlink" <<'MOCK'
#!/bin/sh
printf '%s/opt/rumblebound/tower-duel/releases/fixture\n' "$TOWER_DUEL_TEST_ROOT"
MOCK
cat > "$fixture/bin/systemctl" <<'MOCK'
#!/bin/sh
if [ "$1" = show ]; then echo 4242; exit 0; fi
case "$*" in *tower-duel.service*) echo 'Attempted to mutate the authoritative server' >&2; exit 99;; esac
printf 'systemctl %s\n' "$*" >> "$TOWER_DUEL_TEST_ROOT/actions"
MOCK
cat > "$fixture/bin/docker" <<'MOCK'
#!/bin/sh
if [ "$1" = exec ] && [ "${FAIL_CADDY:-0}" = 1 ]; then exit 1; fi
printf 'docker %s\n' "$*" >> "$TOWER_DUEL_TEST_ROOT/actions"
MOCK
cat > "$fixture/bin/curl" <<'MOCK'
#!/bin/sh
if [ "${FAIL_HEALTH:-0}" = 1 ]; then exit 1; fi
echo ok
MOCK
cat > "$fixture/bin/flock" <<'MOCK'
#!/bin/sh
exit 0
MOCK
chmod 755 "$fixture"/bin/*
export PATH=$fixture/bin:$PATH
script=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)/disable-modes.sh
if FAIL_CADDY=1 sh "$script"; then echo 'Expected Caddy validation failure' >&2; exit 1; fi
grep -q 'merged modes' "$base/tower-duel.caddy"
sha256sum -c "$fixture/unchanged.sha256"
if FAIL_HEALTH=1 sh "$script"; then echo 'Expected backend health failure' >&2; exit 1; fi
grep -q 'merged modes' "$base/tower-duel.caddy"
sha256sum -c "$fixture/unchanged.sha256"
sh "$script"
grep -q 'multiplayer-only' "$base/tower-duel.caddy"
grep -q 'systemctl disable --now tower-duel-modes.service' "$fixture/actions"
sha256sum -c "$fixture/unchanged.sha256"
sh "$script"
sha256sum -c "$fixture/unchanged.sha256"
echo 'PASS: fast removal, repeated removal and failure rollback preserve the authoritative server and shared proxy'
