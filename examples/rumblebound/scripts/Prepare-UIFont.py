"""Regenerate the licensed UI subset, retaining previous glyphs and current UI text.

Run with fonttools 4.61.1: python Prepare-UIFont.py /path/to/NotoSansTC.ttf
The source font is downloaded separately from the URL in font-manifest.json.
"""
import hashlib
import json
from pathlib import Path
import sys
from fontTools import subset, version
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1])
manifest_path = root / "game/unity-art/Art/UI/font-manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
digest = hashlib.sha256(source.read_bytes()).hexdigest()
if digest != manifest["sourceSha256"]:
    raise SystemExit("Source font hash differs from the licensed manifest")
output = root / "game/unity-art/Runtime/Resources/UI/RumbleboundUI.ttf"
with TTFont(output) as existing:
    characters = set(existing.getBestCmap())
characters.update(range(32, 127))
for folder in (root / "client/Assets/Scripts", root / "game/unity-art/Runtime"):
    for path in folder.rglob("*.cs"):
        characters.update(map(ord, path.read_text(encoding="utf-8-sig")))
font = instantiateVariableFont(TTFont(source), {"wght": 500}, inplace=True)
options = subset.Options()
options.name_IDs = ["*"]
options.name_legacy = True
options.name_languages = ["*"]
subsetter = subset.Subsetter(options=options)
subsetter.populate(unicodes=characters)
subsetter.subset(font)
names = {1: "Rumblebound UI", 2: "Regular", 3: "Rumblebound UI Regular", 4: "Rumblebound UI", 6: "RumbleboundUI-Regular", 16: "Rumblebound UI", 17: "Regular"}
for record in font["name"].names:
    if record.nameID in names:
        record.string = names[record.nameID].encode(record.getEncoding())
font.save(output)
manifest["fontToolsVersion"] = version
manifest["recipe"] = "examples/rumblebound/scripts/Prepare-UIFont.py; retained glyphs, Latin, client C# and shared presentation Runtime"
manifest["glyphCount"] = len(font.getGlyphOrder())
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Prepared {output}: {len(font.getGlyphOrder())} glyphs")
