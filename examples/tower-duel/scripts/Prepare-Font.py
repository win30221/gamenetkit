"""Make the OFL UI font and warmup list with fontTools 4.61.1 and the pinned source."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
repo = root.parents[1]
sys.path.insert(0, str(repo / ".local/font-tools"))
from fontTools import version
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

source = Path(sys.argv[1]) if len(sys.argv) > 1 else repo / ".local/font-source/NotoSansTC.ttf"
digest = hashlib.sha256(source.read_bytes()).hexdigest()
if digest != "864727d210d54f2537bbe23b3a839436c3992af72de9322af5270897246bd44f":
    raise SystemExit("Unexpected source font; see font-manifest.json")
characters = set(range(32, 127))
for pattern in ("*.cs", "*.uxml", "*.uss"):
    for path in (root / "client/Assets").rglob(pattern):
        characters.update(map(ord, path.read_text(encoding="utf-8-sig")))
font = instantiateVariableFont(TTFont(source), {"wght": 500}, inplace=True)
# Retain CJK coverage for player names; prewarm only the fixed interface text.
names = {1: "Tower Duel UI", 2: "Regular", 3: "Tower Duel UI Regular", 4: "Tower Duel UI", 6: "TowerDuelUI-Regular", 16: "Tower Duel UI", 17: "Regular"}
for record in font["name"].names:
    if record.nameID in names:
        record.string = names[record.nameID].encode(record.getEncoding())
output = root / "client/Assets/Resources/TowerDuel/TowerDuelUI.ttf"
font.save(output)
manifest = dict(source="https://github.com/google/fonts/tree/main/ofl/notosanstc", sourceFont="NotoSansTC[wght].ttf", sourceSha256=digest,
                license="SIL Open Font License 1.1", licenseFile="OFL.txt", derivedFont="Tower Duel UI", weight=500,
                fontToolsVersion=version, glyphCount=len(font.getGlyphOrder()), recipe="examples/tower-duel/scripts/Prepare-Font.py")
(output.parent / "font-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
# TextCore dynamically adds player names from the full source font at runtime.
cmap = font.getBestCmap()
(output.parent / "glyphs.txt").write_text("".join(chr(c) for c in sorted(characters) if c in cmap), encoding="utf-8")
print(f"Prepared {output}: {len(font.getGlyphOrder())} glyphs")
