import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('dependencies', Path(__file__).resolve().parents[1] / 'scripts' / 'Check-Dependencies.py')
dependencies = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dependencies)


class DependencyGuards(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.write(dependencies.CANONICAL_TRANSPORT, 'export function createTransport() {}')
        self.write('examples/minimal-unity/Packages/manifest.json', '{"dependencies":{}}')

    def write(self, path, text):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding='utf-8')

    def test_clean_runtime_and_game_protocol_test_fixtures_are_allowed(self):
        self.write('packages/unity/transport/Browser/Tests/opaque.test.js', 'JSON.parse(data); const ping = "ping";')
        self.assertEqual([], dependencies.check(self.root))

    def test_parsing_in_runtime_or_bridge_is_rejected(self):
        for path in (dependencies.CANONICAL_TRANSPORT, 'packages/unity/transport/Runtime/Plugins/WebGL/RealtimeTransport.jslib'):
            with self.subTest(path=path):
                self.write(path, 'JSON . parse (data)')
                self.assertTrue(any(str(path).replace('\\', '/') in e.replace('\\', '/') and 'parses' in e for e in dependencies.check(self.root)))
                self.write(path, '')

    def test_game_probe_policy_without_json_parse_is_rejected(self):
        for policy in ('message.matchID', 'message.connectionRttMS', 'raw.startsWith(\'"ping"\')'):
            with self.subTest(policy=policy):
                self.write(dependencies.CANONICAL_TRANSPORT, policy)
                self.assertTrue(any('game probe policy' in e for e in dependencies.check(self.root)))

    def test_canonical_transport_is_required(self):
        (self.root / dependencies.CANONICAL_TRANSPORT).unlink()
        self.assertTrue(any('canonical transport missing' in e for e in dependencies.check(self.root)))

    def test_existing_go_csharp_and_optional_package_guards(self):
        self.write('server/bad.go', 'import "github.com/win30221/gamenetkit/examples/rumblebound/game/sim"')
        self.write('packages/unity/core/Runtime/Bad.cs', 'using Rumblebound; class PlayerState {}')
        self.write('packages/unity/core/Runtime/Bad.asmdef', '{"noEngineReferences":false}')
        self.write('examples/minimal-unity/Packages/manifest.json', '{"dependencies":{"com.realtime.prediction":"file:bad"}}')
        self.assertEqual(5, len(dependencies.check(self.root)))

    def test_gin_is_allowed_in_application_but_not_synchronization_sdk(self):
        self.write('examples/rumblebound/server/main.go', 'import "github.com/gin-gonic/gin"')
        self.assertEqual([], dependencies.check(self.root))
        self.write('server/loop/http.go', 'import "github.com/gin-gonic/gin"')
        self.write('server/go.mod', 'module github.com/win30221/gamenetkit/server\nrequire github.com/gin-gonic/gin v1.12.0\n')
        self.assertEqual(2, len(dependencies.check(self.root)))

    def test_synchronization_sdk_module_cannot_require_examples(self):
        self.write('server/go.mod', 'module github.com/win30221/gamenetkit/server\nrequire github.com/win30221/gamenetkit/examples/rumblebound/game v0.0.0\n')
        self.assertTrue(any('must not require an example' in e for e in dependencies.check(self.root)))

    def test_teaching_examples_stay_basic_and_independent_of_full_game(self):
        self.write('examples/shared-world/client/Packages/manifest.json', '{"dependencies":{"com.realtime.prediction":"file:bad"}}')
        self.write('examples/shared-world/client/Assets/Game.cs', 'using Rumblebound.Example;')
        self.write('examples/modules/server/main.go', 'import "github.com/win30221/gamenetkit/examples/rumblebound/server"')
        self.assertEqual(3, len(dependencies.check(self.root)))


if __name__ == '__main__':
    unittest.main()
