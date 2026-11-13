# Three-unit art brief

This records the original three-unit milestone. The six-unit / eight-card expansion,
new equipment, effects and audio are documented in [EXPANSION_ASSETS.md](EXPANSION_ASSETS.md).

Target: Tower Duel, Unity 6000.6.8f1 / URP 17.6, fixed portrait overview.
Keep the current KayKit diorama, flat color atlas, clear blue/red ownership, and
the same map scale. No changes to the arena's tower or bridge coordinates.

| Role | Silhouette | Equipment | Motion |
| --- | --- | --- | --- |
| Guard | Wide armored front line | Sword + large shield | Weighted walk, short strike, fall |
| Archer | Light hooded back line | Bow + quiver | Light walk, aim/release, fall |
| Sword squad | Three smaller melee silhouettes | One-handed swords | Quick walk, slash, fall |

Use the free KayKit Adventurers 2.0 and compatible free Character Animations.
Record original archives, hashes, creator and CC0 license before importing.
Only selected runtime FBX models, weapons, clips and atlas textures belong in
the client. Paid EXTRA or SOURCE content is outside this task.

Technical targets (to be measured, not assumed): roughly 1.5–1.8 world metres tall,
feet at local origin, +Z forward, no root motion, one shared atlas per source
family. Initial target was about 8k triangles including equipment; the measured
Ranger with bow is 9,600 triangles, so this iteration retains its original rigged
geometry under a 10k ceiling. LOD and reduction remain a measured optimization
task, not a mobile performance claim. Use the Go
circle footprint for gameplay; render meshes do not provide authoritative collision.

Required clips: idle, walk/run, melee or bow attack, death. Imported animation
events must never deal damage. The server attack tick drives the attack trigger;
server snapshots drive movement and deletion. A removed soldier may finish a short
visual death clip but cannot remain a target.

Validate one equipped guard at actual battle zoom before expanding the family.
Check clip loops, bone bindings, weapon attachment, facing, material conversion,
import scale, triangle counts, and in-engine screenshots. No human playtest or
mobile performance claim follows from asset import alone.

## Import results

The three original rigs bind correctly to the selected shared animation clips.
Unity pose sampling changed the right upper-arm rotation by about 31, 55 and 31
degrees for guard, archer and swords respectively. The actual runtime additionally
checks moving leg/arm bones and death pose changes in two Windows Players.

| Prefab | Height | Triangles incl. equipment / marker | Renderers |
| --- | --- | --- | --- |
| guard / Knight | 1.80 m | 6,378 | 12 |
| archer / Ranger | 1.70 m | 9,600 | 10 |
| swords / Rogue (each) | 1.55 m | 7,878 | 9 |

The wrapper rotates the original character's -Z front to the game's +Z forward.
Weapon FBX roots retain their original 100x scale and axis rotation when attached
to handslot.l/r; resetting those transforms would make the weapons too small.
Source meshes, UVs, weights and atlas PNGs are unchanged. Unity texture imports
use 512 px with mipmaps. Root motion is disabled; Go positions remain authoritative.

An equipped pose render is saved locally at
`artifacts/tower-duel/characters-preview.png`. This is a Unity render, not generated
concept art. Character polycounts and renderer counts above are measurements,
not a performance guarantee for eighty animated units.

## Reproducing the source selection

Sources and archive SHA-256 values are recorded in `character-manifest.json`.
The selected original FBX/PNG/license files are checked into the client, so a
normal build does not need a new download or Blender.

To obtain the pinned free archives again from the repository root:

```powershell
./examples/tower-duel/scripts/Fetch-Free-KayKit.ps1 -Slug kaykit-adventurers -Output .local/tower-duel/characters/Adventurers-Free-2.0.zip
./examples/tower-duel/scripts/Fetch-Free-KayKit.ps1 -Slug kaykit-character-animations -Output .local/tower-duel/characters/Animations-Free.zip
Expand-Archive .local/tower-duel/characters/Adventurers-Free-2.0.zip .local/tower-duel/characters/adventurers -Force
Expand-Archive .local/tower-duel/characters/Animations-Free.zip .local/tower-duel/characters/animations -Force
./examples/tower-duel/scripts/Import-Characters.ps1
```

The helper uses the publisher's public free-download flow without an account or
payment. A changed archive hash stops the import workflow for version review.
