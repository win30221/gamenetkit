#!/bin/sh
# Isolated fixture: no VM, Docker daemon, Caddy or systemd required.
set -eu
script=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)/install.sh
work=${1:-"${TMPDIR:-/tmp}"}
mkdir -p "$work"
work=$(cd "$work" && pwd)

write_core_receipt() (
    set -eu
    cd "$1"
    : > core-assets.sha256
    : > public/index.html
    for name in WebGL.loader.js WebGL.framework.js.gz WebGL.data.gz WebGL.wasm.gz; do
        line=$(sha256sum "public/Build/$name")
        printf '%s\n' "$line" >> core-assets.sha256
        digest=$(printf '%s' "$line" | cut -d ' ' -f 1)
        printf '/assets/core/%s/%s\n' "$digest" "$name" >> public/index.html
    done
)

verify_core() (
    set -eu
    cd "$1"
    for name in WebGL.loader.js WebGL.framework.js.gz WebGL.data.gz WebGL.wasm.gz; do
        digest=$(sha256sum "public/Build/$name" | cut -d ' ' -f 1)
        cmp "$base/assets/core/$digest/$name" "public/Build/$name"
    done
)

write_unit_receipt() (
    set -eu
    cd "$1"
    payload=$2
    rm -rf public/assets/bundles
    mkdir -p public/assets/bundles
    printf '%s\n' "$payload" > public/assets/bundles/unit.tmp
    digest=$(sha256sum public/assets/bundles/unit.tmp | cut -d ' ' -f 1)
    mkdir -p "public/assets/bundles/$digest"
    mv public/assets/bundles/unit.tmp "public/assets/bundles/$digest/griffin.bundle"
    printf '{"version":1,"bundles":[{"kind":"griffin","sha256":"%s"}]}\n' "$digest" > public/unit-bundles.json
    sha256sum public/unit-bundles.json "public/assets/bundles/$digest/griffin.bundle" > unit-bundles.sha256
)

verify_units() (
    set -eu
    cd "$1"
    for file in public/assets/bundles/*/*.bundle; do
        digest=$(basename "$(dirname "$file")")
        cmp "$file" "$base/assets/bundles/$digest/$(basename "$file")"
    done
)

prepare() {
    TEST_ROOT=$(mktemp -d "$work/tower-duel-install.XXXXXX")
    export TEST_ROOT
    base=$TEST_ROOT/opt/rumblebound/tower-duel
    proxy=$TEST_ROOT/opt/rumblebound/Caddyfile
    state=$TEST_ROOT/state
    mkdir -p "$base/releases/old" "$base/releases/new/public/Build" \
        "$TEST_ROOT/etc/systemd/system" "$TEST_ROOT/run/lock" "$TEST_ROOT/bin" "$state"
    printf 'other-game.example {\n  respond "unrelated"\n}\nimport /srv/tower-duel/*.caddy\n' > "$proxy"
    printf 'merged route /solo/*\n' > "$base/tower-duel.caddy"
    printf 'legacy solo redirect\n' > "$base/tower-duel-solo.caddy"
    printf 'original core unit\n' > "$TEST_ROOT/etc/systemd/system/tower-duel.service"
    printf 'multiplayer-only route\n' > "$base/releases/new/site.caddy"
    printf 'binary\n' > "$base/releases/new/tower-duel-api"
    for name in WebGL.loader.js WebGL.framework.js.gz WebGL.data.gz WebGL.wasm.gz; do
        printf '%s\n' "$name fixture" > "$base/releases/new/public/Build/$name"
    done
    write_core_receipt "$base/releases/new"
    write_unit_receipt "$base/releases/new" "original griffin"
    ln -s releases/old "$base/current"
    for unit in tower-duel-modes tower-duel-solo-api tower-duel-solo-web; do
        : > "$state/active.$unit.service"
        : > "$state/enabled.$unit.service"
    done
    cat > "$TEST_ROOT/bin/id" <<'MOCK'
#!/bin/sh
case "$1" in -u) echo 0;; towerduel) exit 0;; *) exec /usr/bin/id "$@";; esac
MOCK
    cat > "$TEST_ROOT/bin/systemctl" <<'MOCK'
#!/bin/sh
set -eu
printf 'systemctl %s\n' "$*" >> "$TEST_ROOT/actions"
case "$1" in
  is-active|is-enabled)
    action=$1; shift
    if [ "${1:-}" = --quiet ]; then shift; fi
    if [ "$action" = is-active ]; then test -f "$TEST_ROOT/state/active.$1";
    else test -f "$TEST_ROOT/state/enabled.$1"; fi
    ;;
  enable)
    : > "$TEST_ROOT/state/enabled.$2"
    ;;
  disable)
    shift
    stop=0
    if [ "${1:-}" = --now ]; then stop=1; shift; fi
    rm -f "$TEST_ROOT/state/enabled.$1"
    if [ "$stop" = 1 ]; then rm -f "$TEST_ROOT/state/active.$1"; fi
    if [ "${FAIL_DISABLE_UNIT:-}" = "$1" ]; then exit 42; fi
    ;;
  stop) shift; for unit in "$@"; do rm -f "$TEST_ROOT/state/active.$unit"; done ;;
  start|restart) : > "$TEST_ROOT/state/active.$2" ;;
  daemon-reload) : ;;
  *) echo "Unexpected systemctl: $*" >&2; exit 60;;
esac
MOCK
    cat > "$TEST_ROOT/bin/docker" <<'MOCK'
#!/bin/sh
set -eu
printf 'docker %s\n' "$*" >> "$TEST_ROOT/actions"
case "$1" in
  inspect) echo rumblebound ;;
  exec)
    if [ "${FAIL_VALIDATE:-0}" = 1 ]; then exit 25; fi
    test ! -e "$TEST_ROOT/opt/rumblebound/tower-duel/tower-duel-solo.caddy"
    ;;
  kill)
    if [ "${FAIL_RELOAD:-0}" = 1 ]; then exit 26; fi
    ;;
  *) exit 50;;
esac
MOCK
    cat > "$TEST_ROOT/bin/curl" <<'MOCK'
#!/bin/sh
case " $* " in
  *18179*) if [ "${FAIL_HTTPS:-0}" = 1 ]; then exit 22; fi ;;
esac
printf ok
MOCK
    cat > "$TEST_ROOT/bin/flock" <<'MOCK'
#!/bin/sh
exit 0
MOCK
    chmod 755 "$TEST_ROOT"/bin/*
    # Rewrite only machine-global paths into the disposable fixture.
    sed -e "s|/opt/rumblebound|$TEST_ROOT/opt/rumblebound|g" \
        -e "s|/etc/systemd/system|$TEST_ROOT/etc/systemd/system|g" \
        -e "s|/run/lock|$TEST_ROOT/run/lock|g" "$script" > "$TEST_ROOT/install-test.sh"
    chmod 755 "$TEST_ROOT/install-test.sh"
    export PATH="$TEST_ROOT/bin:$ORIGINAL_PATH"
}

verify_rollback() {
    test "$(readlink "$base/current")" = releases/old
    grep -q 'merged route' "$base/tower-duel.caddy"
    grep -q 'legacy solo redirect' "$base/tower-duel-solo.caddy"
    grep -q 'original core unit' "$TEST_ROOT/etc/systemd/system/tower-duel.service"
    grep -q 'unrelated' "$proxy"
    for unit in tower-duel-modes tower-duel-solo-api tower-duel-solo-web; do
        test -f "$state/active.$unit.service"
        test -f "$state/enabled.$unit.service"
    done
}

ORIGINAL_PATH=$PATH
prepare
sh "$TEST_ROOT/install-test.sh" new full >/dev/null
test "$(readlink "$base/current")" = releases/new
verify_core "$base/releases/new"
verify_units "$base/releases/new"
grep -q 'multiplayer-only route' "$base/tower-duel.caddy"
test ! -e "$base/tower-duel-solo.caddy"
grep -q 'unrelated' "$proxy"
for unit in tower-duel-modes tower-duel-solo-api tower-duel-solo-web; do
    test ! -e "$state/active.$unit.service"
    test ! -e "$state/enabled.$unit.service"
done
# The fixture includes the assets compared during partial updates.
mkdir -p "$base/releases/new/protocol"
for name in map.json rules.json; do printf 'protocol\n' > "$base/releases/new/protocol/$name"; done
printf 'js\n' > "$base/releases/new/public/transport.js"
# A clean full redeployment must not needlessly reload shared Caddy.
cp -a "$base/releases/new" "$base/releases/repeat"
rm -f "$base/releases/repeat"/previous.* "$base/releases/repeat"/was-* "$base/releases/repeat/Caddyfile.candidate"
reloads_before=$(grep -c '^docker kill' "$TEST_ROOT/actions" || true)
sh "$TEST_ROOT/install-test.sh" repeat full >/dev/null
reloads_after=$(grep -c '^docker kill' "$TEST_ROOT/actions" || true)
test "$reloads_before" = "$reloads_after"
# A partial update after cleanup does not restart the core service.
cp -a "$base/releases/new" "$base/releases/client"
for name in tower-duel-api site.caddy; do cmp "$base/releases/new/$name" "$base/releases/client/$name"; done
printf 'new WASM content\n' > "$base/releases/client/public/Build/WebGL.wasm.gz"
write_core_receipt "$base/releases/client"
write_unit_receipt "$base/releases/client" "new griffin"
sh "$TEST_ROOT/install-test.sh" client client >/dev/null
test "$(readlink "$base/current")" = releases/client
verify_core "$base/releases/new"
verify_core "$base/releases/client"
verify_units "$base/releases/new"
verify_units "$base/releases/client"
test "$(find "$base/assets/bundles" -type f | wc -l | tr -d ' ')" = 2
# The original 4 assets and one changed WASM are still available.
test "$(find "$base/assets/core" -type f | wc -l | tr -d ' ')" = 5
cp -a "$base/releases/client" "$base/releases/badunit"
printf 'bad receipt\n' > "$base/releases/badunit/unit-bundles.sha256"
if sh "$TEST_ROOT/install-test.sh" badunit client >"$TEST_ROOT/log" 2>&1; then
    echo 'Expected invalid unit receipt rejection' >&2; exit 1
fi
test "$(readlink "$base/current")" = releases/client
printf 'PASS: old unit URL retention and invalid bundle receipt rejection\n'
cp -a "$base/releases/client" "$base/releases/bad"
printf 'bad receipt\n' > "$base/releases/bad/core-assets.sha256"
if sh "$TEST_ROOT/install-test.sh" bad client >"$TEST_ROOT/log" 2>&1; then
    echo 'Expected invalid core receipt rejection' >&2; exit 1
fi
test "$(readlink "$base/current")" = releases/client
digest=$(sha256sum "$base/releases/client/public/Build/WebGL.wasm.gz" | cut -d ' ' -f 1)
printf 'corrupt immutable bytes\n' > "$base/assets/core/$digest/WebGL.wasm.gz"
if sh "$TEST_ROOT/install-test.sh" client client >"$TEST_ROOT/log" 2>&1; then
    echo 'Expected corrupt immutable asset rejection' >&2; exit 1
fi
grep -q 'Corrupt immutable core asset' "$TEST_ROOT/log"
test "$(readlink "$base/current")" = releases/client
printf 'PASS: content-addressed core reuse, old URL retention, invalid receipt and corruption rejection\n'
printf 'PASS: full migration disables optional modes; later client-only update works\n'

prepare
if sh "$TEST_ROOT/install-test.sh" new client >"$TEST_ROOT/log" 2>&1; then
    echo 'Expected partial deployment to reject stale Solo routes' >&2; exit 1
fi
verify_rollback
printf 'PASS: partial deployment rejects a merged installation without touching it\n'

prepare
if FAIL_VALIDATE=1 sh "$TEST_ROOT/install-test.sh" new full >"$TEST_ROOT/log" 2>&1; then
    echo 'Expected Caddy validation failure' >&2; exit 1
fi
verify_rollback
printf 'PASS: failed Caddy validation restores both routes\n'

prepare
if FAIL_HTTPS=1 sh "$TEST_ROOT/install-test.sh" new full >"$TEST_ROOT/log" 2>&1; then
    echo 'Expected HTTPS health failure' >&2; exit 1
fi
verify_rollback
printf 'PASS: failed HTTPS check restores release, routes and services\n'

prepare
if FAIL_RELOAD=1 sh "$TEST_ROOT/install-test.sh" new full >"$TEST_ROOT/log" 2>&1; then
    echo 'Expected Caddy reload failure' >&2; exit 1
fi
verify_rollback
printf 'PASS: failed Caddy reload restores the previous configuration\n'

prepare
if FAIL_DISABLE_UNIT=tower-duel-solo-api.service sh "$TEST_ROOT/install-test.sh" new full >"$TEST_ROOT/log" 2>&1; then
    echo 'Expected optional service disable failure' >&2; exit 1
fi
verify_rollback
printf 'PASS: partial optional-mode cleanup rolls back all prior service changes\n'
