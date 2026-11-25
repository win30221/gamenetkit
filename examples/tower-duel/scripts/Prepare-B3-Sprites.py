"""Slice generated B3 action grids into anchored atlases; requires Pillow.

This performs layout/alpha normalization only. Character artwork comes from image_gen.
Run with a Python that has Pillow, or set PYTHONPATH to .local/sprite-tools.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import statistics
from pathlib import Path

from PIL import Image, ImageChops, ImageOps, ImageFilter

REPO = Path(__file__).resolve().parents[3]
RESOURCES = REPO / "examples/tower-duel/client/Assets/Resources/TowerDuel/B3"


def read_rgba(path: Path, alpha_floor: int = 24) -> Image.Image:
    image = Image.open(path).convert("RGBA")
    alpha = image.getchannel("A")
    if alpha.getextrema()[0] > 16:
        raise ValueError(f"Source has no usable transparent background: {path}")
    alpha = alpha.point(lambda n: 0 if n < alpha_floor else 255 if n >= 240 else n)
    image.putalpha(alpha)
    return image


def cells(image: Image.Image, columns: int, rows: int, row_gutters: bool = False) -> list[list[Image.Image]]:
    w, h = image.size
    opaque = image.getchannel("A").point(lambda n: 255 if n >= 128 else 0)
    def gutters(length: int, count: int, horizontal: bool, row_box=None) -> list[int]:
        cuts = [0]
        for index in range(1, count):
            expected = length*index/count
            radius = length/count*.23
            scores = []
            for position in range(max(1, round(expected-radius)), min(length-1, round(expected+radius))):
                strip = opaque.crop((position, row_box[0] if row_box else 0, position+1, row_box[1] if row_box else h) if horizontal else (row_box[0] if row_box else 0, position, row_box[1] if row_box else w, position+1))
                scores.append((sum(strip.histogram()[1:]), position))
            minimum = min(score for score, _ in scores)
            candidates = [position for score, position in scores if score == minimum]
            # The generated layout can drift a few pixels; cut at an actual empty gutter.
            groups = []
            for position in candidates:
                if groups and position == groups[-1][-1]+1: groups[-1].append(position)
                else: groups.append([position])
            group = min(groups, key=lambda g: abs(statistics.mean(g)-expected))
            cuts.append(round(statistics.mean(group)))
        return cuts+[length]
    xs, ys = gutters(w, columns, True), gutters(h, rows, False)
    result = []
    for r in range(rows):
        row_xs = gutters(w, columns, True, (ys[r], ys[r+1])) if row_gutters else xs
        row = []
        for c in range(columns):
            # A raised axe in one column can overlap the next row's Y range
            # without touching that row's figure. Find the empty gutter within
            # this column rather than cutting a weapon at a global grid line.
            cell_ys = gutters(h, rows, False, (row_xs[c], row_xs[c+1])) if row_gutters else ys
            row.append(image.crop((row_xs[c], cell_ys[r], row_xs[c+1], cell_ys[r+1])))
        result.append(row)
    return result


def bounds(image: Image.Image) -> tuple[int, int, int, int]:
    box = image.getchannel("A").point(lambda n: 255 if n >= 128 else 0).getbbox()
    if box is None:
        raise ValueError("Empty animation cell")
    return box


def head_anchor(image: Image.Image, box: tuple[int, int, int, int], landmark: str = "silver") -> float:
    left, top, right, bottom = box
    pixels = image.load()
    def selected(pixel):
        r, g, b, a = pixel
        if a < 160:
            return False
        if landmark == "green":
            return g > r*1.08 and g > b*1.15 and g >= 45
        if landmark == "purple":
            return r > g*1.2 and b > g*1.2 and min(r,b) >= 45
        if landmark == "orange":
            return r >= 130 and g >= 60 and r > g*1.45 and g > b*1.7
        return min(r,g,b) >= 90 and max(r,g,b)-min(r,g,b) < 78
    head_start = .18 if landmark == "silver" else .10
    positions = [x for y in range(round(top+(bottom-top)*head_start), round(top+(bottom-top)*.43))
                 for x in range(left, right) if selected(pixels[x,y])]
    return statistics.mean(positions) if positions else (left+right)/2


def boot_anchor(image: Image.Image, box: tuple[int, int, int, int]) -> float:
    left, top, right, bottom = box
    pixels = image.load()
    positions = [x for y in range(round(top+(bottom-top)*.82), bottom)
                 for x in range(left, right)
                 if pixels[x, y][3] >= 160 and pixels[x, y][0] > pixels[x, y][1]*1.22
                 and pixels[x, y][1] > pixels[x, y][2]*1.15]
    return statistics.mean(positions) if positions else (left+right)/2


def boot_floor(image: Image.Image, box: tuple[int, int, int, int], palette: str = "") -> int:
    left, top, right, bottom = box
    pixels = image.load()
    mask = Image.new('L',image.size)
    selected = mask.load()
    for y in range(round(top+(bottom-top)*.72),bottom):
        for x in range(left,right):
            r,g,b,a = pixels[x,y]
            brown = r>g*1.22 and g>b*1.15
            if palette == "leather":
                # Exclude the bright gold axe collar below crouching knees.
                brown = 60<=r<=195 and 30<=g<=115 and 15<=b<=80 and r>g*1.25 and g>b*1.2
            if a>=160 and brown:selected[x,y]=255
    if not mask.getbbox():return bottom
    # Include the dark boot outline while excluding a silver weapon below it.
    mask=ImageChops.multiply(mask.filter(ImageFilter.MaxFilter(11)),image.getchannel('A').point(lambda n:255 if n>=128 else 0))
    return mask.getbbox()[3]


def normalize_row(row: list[Image.Image], tile: int, baseline: int,
                  reference_height: int, head_offset: float | None = None,
                  landmark: str = "silver", anchor_feet: bool = False, boot_palette: str = "") -> tuple[list[Image.Image], dict]:
    boxes = [bounds(image) for image in row]
    median_height = statistics.median(b[3]-b[1] for b in boxes)
    max_width = max(b[2]-b[0] for b in boxes)
    scale = min(reference_height/median_height, (tile-12)/max_width)
    if head_offset is None:
        head_offset = (boot_anchor(row[0], boxes[0])-head_anchor(row[0], boxes[0],landmark))*scale
    output = []
    for image, box in zip(row, boxes):
        left, top, right, bottom = box
        if left <= 0 or right >= image.width or top <= 0 or bottom >= image.height:
            raise ValueError(f"Sprite touches a source cell boundary: {box}/{image.size}")
        crop = image.crop(box)
        size = (max(1, round(crop.width*scale)), max(1, round(crop.height*scale)))
        crop = crop.resize(size, Image.Resampling.LANCZOS)
        # Register the helmet or hood to the idle pose's foot/head relation;
        # moving weapons must not recenter the body between action frames.
        anchor = head_anchor(image, box,landmark)+head_offset/scale
        x = round(tile/2 + (left-anchor)*scale)
        floor=boot_floor(image,box,boot_palette) if anchor_feet else bottom
        y = baseline-round((floor-top)*scale)
        if x < 0 or x+crop.width > tile or y < 0 or y+crop.height > tile:
            raise ValueError(f"Normalized sprite would clip: {(x, y, *size)}")
        frame = Image.new("RGBA", (tile, tile))
        frame.alpha_composite(crop, (x, y))
        output.append(frame)
    return output, {"scale": round(scale, 5), "headOffset": head_offset, "sourceMedianHeight": median_height,
                    "distinctFrames": len({hashlib.sha256(f.tobytes()).hexdigest() for f in output})}


def atlas(rows: list[list[Image.Image]], tile: int) -> Image.Image:
    columns = len(rows[0])
    result = Image.new("RGBA", (columns*tile, len(rows)*tile))
    for r, row in enumerate(rows):
        if len(row) != columns:
            raise ValueError("Inconsistent animation frame count")
        for c, frame in enumerate(row):
            result.alpha_composite(frame, (c*tile, r*tile))
    return result


def prepare(kind: str) -> None:
    config_path = REPO / f"examples/tower-duel/art/b3-production/{kind}.json"
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    tile, baseline, reference = (config[k] for k in ("tileSize", "baseline", "referenceHeight"))
    source = {key: REPO/path for key, path in config["sources"].items()}
    target = REPO / "examples/tower-duel/client/Assets/TowerDuelArt/Units" / kind
    target.mkdir(parents=True, exist_ok=True)
    source_target = REPO / "examples/tower-duel/art/b3-production/atlases" / kind
    source_target.mkdir(parents=True, exist_ok=True)
    report = {"kind": kind, "directions": config["directions"], "tile": tile, "clips": {}}

    cutoffs=config.get("sourceAlphaCutoff",{})
    def read_source(name):
        # Remove a generated translucent backdrop before resizing. The resize
        # then restores a clean antialiased outline around the real silhouette.
        return read_rgba(source[name],cutoffs.get(name,24))
    report["sourceAlphaCutoff"]=cutoffs
    counts = {clip: config.get(clip+"Frames", default) for clip, default in
              (("idle", 1), ("walk", 6), ("attack", 6))}
    rates = {clip: config.get(clip+"Fps", 8) for clip in ("idle", "walk")}
    impact = config["impactFrame"]
    if (any(type(n) is not int or n < (1 if clip == "idle" else 2)
            for clip, n in counts.items()) or not 1 <= impact < counts["attack"]
            or any(not 0 < fps <= 60 for fps in rates.values())
            or tile*16 > 4096 or tile*max(counts.values()) > 4096):
        raise ValueError("Invalid clip counts, impact, FPS or atlas dimensions")

    def action_rows(clip):
        n = counts[clip]
        gutters = config.get("rowGutters", False)
        if clip == "idle" and n == 1 and "idle" in source:
            # Original eight-facing layout: 4 columns x 2 rows.
            rows = [[im] for row in cells(read_source("idle"), 4, 2, gutters) for im in row]
        elif clip in source:
            rows = cells(read_source(clip), n, 8, gutters)
        else:
            rows = (cells(read_source(f"{clip}-a"), n, 4, gutters)
                    + cells(read_source(f"{clip}-b"), n, 4, gutters))
        if clip == "idle":
            source_directions = config.get("idleSourceDirections", config["directions"])
            if len(source_directions) != 8 or set(source_directions) != set(config["directions"]):
                raise ValueError("Idle source directions must contain each of the eight facings once")
            rows = [rows[source_directions.index(direction)] for direction in config["directions"]]
        return rows

    offsets = []
    for clip in ("idle", "walk", "attack"):
        raw_rows = action_rows(clip)
        normalized, row_reports = [], []
        for direction_index, row in enumerate(raw_rows):
            frames, row_report = normalize_row(row, tile, baseline, reference,
                                              None if clip == "idle" else offsets[direction_index],
                                              config.get("headLandmark","silver"),config.get("anchorFeet",False),config.get("bootPalette",""))
            if clip == "idle": offsets.append(row_report["headOffset"])
            if row_report["distinctFrames"] < min(4, counts[clip]):
                raise ValueError(f"Too few distinct {clip} poses: {row_report}")
            normalized.append(frames)
            row_reports.append(row_report)
        sheet = atlas(normalized, tile)
        original = source_target / f"{clip}.png"
        sheet.save(original)
        if not config.get("baked", False):
            (target / f"{clip}.png").write_bytes(original.read_bytes())
        report["clips"][clip] = {"size": sheet.size, "frames": sum(map(len, normalized)),
                                    "rows": row_reports}
        for direction, frames in zip(config["directions"], normalized):
            preview = REPO / f"artifacts/tower-duel/sprites-b3/{kind}/{clip}-{direction}.gif"
            preview.parent.mkdir(parents=True, exist_ok=True)
            backgrounds = []
            for frame in frames:
                background = Image.new("RGBA", frame.size, (98, 127, 74, 255))
                background.alpha_composite(frame)
                backgrounds.append(background.convert("RGB"))
            backgrounds[0].save(preview, save_all=True, append_images=backgrounds[1:],
                                duration=round(1000/rates[clip]) if clip in rates else 150, loop=0)

    cards = RESOURCES / "Cards"
    cards.mkdir(parents=True, exist_ok=True)
    card = Image.open(source["card"]).convert("RGB")
    ImageOps.fit(card, (384, 512), method=Image.Resampling.LANCZOS).save(cards/f"{kind}.png")
    if "frame" in source:
        frame = read_rgba(source["frame"])
        frame = frame.crop(bounds(frame)).resize((256, 352), Image.Resampling.LANCZOS)
        if frame.getchannel("A").getpixel((128, 176)) > 16:
            raise ValueError("Card frame center is not transparent")
        ui = RESOURCES / "UI"
        ui.mkdir(parents=True, exist_ok=True)
        frame.save(ui / "CardFrame.png")

    definition = {"kind": kind, "tileSize": tile, "baseline": baseline,
                  "referenceHeight": reference, "unitHeight": config["unitHeight"],
                  "worldHeight": config["worldHeight"], "idleFrames": counts["idle"],
                  "walkFrames": counts["walk"], "attackFrames": counts["attack"],
                  "idleFps": rates["idle"], "walkFps": rates["walk"], "impactFrame": impact}
    if "groundKind" in config:
        definition["groundKind"] = config["groundKind"]
    definition["teamMaskTop"] = config.get("teamMaskTop",2.0)
    definition["baked"] = config.get("baked",False)
    (target/"definition.json").write_text(json.dumps(definition, indent=2)+"\n", encoding="utf-8")
    report_path = REPO / f"artifacts/tower-duel/sprites-b3/{kind}/normalization.json"
    report_path.write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"kind": kind, "frames": 8*sum(counts.values()), "target": str(target), "report": str(report_path)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("kind")
    prepare(parser.parse_args().kind)
