from pathlib import Path
import json, re, os

CANONICAL_TRANSPORT = Path('packages/unity/transport/Browser/transport.js')
GO_EXAMPLE_DEPENDENCY = re.compile(r'github\.com/win30221/gamenetkit/examples/|rumblebound\.(?:local|example)/')

def source_files(folder):
    # Unity/IL2CPP output is generated, may be locked, and does not describe
    # source dependencies. Do not descend into its caches or built players.
    ignored = {'Library', 'Temp', 'Builds', 'Logs', 'obj', 'bin', '.local', '.git'}
    for directory, folders, files in os.walk(folder):
        folders[:] = [name for name in folders if name not in ignored]
        for name in files:
            yield Path(directory) / name


def check(root):
    """Guard known dependency boundaries; this is not a complete JS/C# parser."""
    errors = []
    for source in (root / 'server').rglob('*.go'):
        text = source.read_text(encoding='utf-8')
        if GO_EXAMPLE_DEPENDENCY.search(text):
            errors.append(f'{source}: server SDK imports the demo')
        if 'github.com/gin-gonic/gin' in text:
            errors.append(f'{source}: Gin belongs to application HTTP composition, not the synchronization SDK')
    sdk_module = root / 'server/go.mod'
    if sdk_module.exists():
        text = sdk_module.read_text(encoding='utf-8')
        if 'github.com/gin-gonic/gin' in text:
            errors.append(f'{sdk_module}: synchronization SDK must not require Gin')
        if GO_EXAMPLE_DEPENDENCY.search(text):
            errors.append(f'{sdk_module}: synchronization SDK must not require an example')
    for source in (root / 'packages/unity').rglob('*.cs'):
        text = source.read_text(encoding='utf-8-sig')
        if re.search(r'\b(?:using|namespace)\s+Rumblebound\b', text):
            errors.append(f'{source}: Unity SDK depends on demo namespace')
        if re.search(r'\b(?:PlayerState|HeroRules|ArenaCollision|ProbeMessage|SessionReply)\b', text):
            errors.append(f'{source}: Unity SDK contains a game DTO/rule')
    for assembly in (root / 'packages/unity').rglob('*.asmdef'):
        config = json.loads(assembly.read_text(encoding='utf-8'))
        if not config.get('noEngineReferences'):
            errors.append(f'{assembly}: SDK algorithms unexpectedly depend on the engine')
    for source in (root / 'packages/unity/transport').rglob('*'):
        if source.suffix not in ('.js', '.jslib') or 'Tests' in source.parts:
            continue
        text = source.read_text(encoding='utf-8-sig')
        # Runtime transports must not interpret application JSON. The bridge may
        # still serialize its own error notifications for the existing C# ABI.
        if re.search(r'\bJSON\s*\.\s*parse\s*\(', text):
            errors.append(f'{source}: transport runtime parses application JSON')
        if re.search(r'''\b(?:connectionRttMS|matchID|generation|ProbeMessage)\b|["'](?:ping|pong)["']''', text):
            errors.append(f'{source}: transport runtime contains game probe policy')
    canonical = root / CANONICAL_TRANSPORT
    if not canonical.is_file():
        errors.append(f'{canonical}: canonical transport missing')
    for project in ('examples/minimal-unity', 'examples/shared-world/client'):
        manifest_path = root / project / 'Packages/manifest.json'
        if not manifest_path.exists():
            continue
        manifest = json.loads(manifest_path.read_text())
        for name in ('interpolation', 'prediction', 'correction', 'diagnostics', 'simulation', 'clock', 'replication', 'actions', 'codec', 'playback'):
            if f'com.realtime.{name}' in manifest['dependencies']:
                errors.append(f'{project}: basic teaching example must work without optional {name}')
    # Keep learning code independent from the full game's growing application.
    for folder in ('examples/unity-common', 'examples/browser-common',
                   'examples/shared-world/client/Assets', 'examples/shared-world/server',
                   'examples/shared-world/web', 'examples/shared-world/tests', 'examples/modules',
                   'examples/interpolation/client/Assets', 'examples/interpolation/browser-host',
                   'examples/interpolation/web', 'examples/interpolation/tests',
                   'examples/prediction/client', 'examples/prediction/server',
                   'examples/prediction/browser-host', 'examples/prediction/web', 'examples/prediction/tests',
                   'examples/network-recovery/client', 'examples/network-recovery/server',
                   'examples/network-recovery/browser-host', 'examples/network-recovery/web', 'examples/network-recovery/tests',
                   'examples/objects-actions/client', 'examples/objects-actions/server',
                   'examples/objects-actions/browser-host', 'examples/objects-actions/web', 'examples/objects-actions/tests',
                   'examples/replay/client', 'examples/replay/server',
                   'examples/replay/browser-host', 'examples/replay/web', 'examples/replay/tests',
                   'examples/interest/client', 'examples/interest/server',
                   'examples/interest/browser-host', 'examples/interest/web', 'examples/interest/tests',
                   'examples/codec/client', 'examples/codec/server',
                   'examples/codec/browser-host', 'examples/codec/web', 'examples/codec/tests',
                   'examples/history-lagcomp/client', 'examples/history-lagcomp/server',
                   'examples/history-lagcomp/browser-host', 'examples/history-lagcomp/web', 'examples/history-lagcomp/tests'):
        for source in source_files(root / folder):
            if source.suffix not in ('.cs', '.go', '.js', '.mjs', '.asmdef', '.csproj') and source.name != 'go.mod':
                continue
            if re.search(r'Rumblebound\.|rumblebound\.(?:local|example)|(?:examples|samples)[/\\]rumblebound', source.read_text(encoding='utf-8-sig')):
                errors.append(f'{source}: teaching code must not depend on the full game')
    for source in (root / 'examples/rumblebound/game/unity/Runtime').glob('*.cs'):
        text = source.read_text(encoding='utf-8-sig')
        if re.search(r'\bUnityEngine\b|\bRumblebound\.(?:G0|Example|Presentation)\b', text):
            errors.append(f'{source}: shared gameplay must remain engine/application independent')
    return errors


if __name__ == '__main__':
    errors = check(Path(__file__).resolve().parents[2])
    for error in errors:
        print(error)
    print(f'DEPENDENCIES {"FAIL" if errors else "PASS"}: known SDK boundaries, canonical JS source and minimal dependencies')
    raise SystemExit(bool(errors))
