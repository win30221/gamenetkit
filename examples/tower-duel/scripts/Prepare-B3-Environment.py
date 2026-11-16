"""Normalize generated B3 environment art; no raster art is synthesized here.

Original RGB drawing is preserved. Processing is limited to alpha cleanup,
layout, scale, and the paired silhouette ground-shadow export used by troops.
"""
from __future__ import annotations

import hashlib
import json
import math
from collections import deque
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "artifacts/tower-duel/environment-b3-v1"
OUTPUT = ROOT / "examples/tower-duel/art/environment-b3-v1"
SHADOW_SCALE = 2.2
SHADOW_X, SHADOW_Z = -.65, -.75055535
VARIANTS = ["blue-away", "blue-toward", "red-away", "red-toward"]


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def clean_alpha(image, minimum_component=40):
    """Remove generated haze and isolated flecks before creating new AA edges."""
    image = image.convert("RGBA")
    width, height = image.size
    mask = bytearray(image.getchannel("A").point(lambda a: 255 if a >= 128 else 0).tobytes())
    result = bytearray(len(mask))
    removed = 0
    for start in range(len(mask)):
        if not mask[start]:
            continue
        queue = deque([start])
        mask[start] = 0
        component = []
        while queue:
            point = queue.popleft()
            component.append(point)
            px = point % width
            neighbors = []
            if px:
                neighbors.append(point - 1)
            if px < width - 1:
                neighbors.append(point + 1)
            if point >= width:
                neighbors.append(point - width)
            if point + width < len(mask):
                neighbors.append(point + width)
            for neighbor in neighbors:
                if mask[neighbor]:
                    mask[neighbor] = 0
                    queue.append(neighbor)
        if len(component) >= minimum_component:
            for point in component:
                result[point] = 255
        else:
            removed += len(component)
    image.putalpha(Image.frombytes("L", (width, height), bytes(result)))
    return image, removed


def ground_profile(body, baseline, band_fraction=.25):
    """The illustrated plinth has depth; its lower contour is not a level line.

    Treat only the bottom quarter as footprint. An arch beam or tree crown
    must never become a ground contact merely because it ends a column.
    """
    alpha = body.getchannel("A")
    bounds = alpha.point(lambda a: 255 if a >= 128 else 0).getbbox()
    band = round((bounds[3]-bounds[1])*band_fraction)
    data = alpha.tobytes()
    profile = [baseline]*body.width
    contacts = []
    for x in range(body.width):
        for y in range(body.height-1, max(0, baseline-band)-1, -1):
            if data[y*body.width+x] >= 128:
                profile[x] = y+1
                contacts.append((x, y+1))
                break
    return profile, contacts


def paired_shadow(body, baseline, band_fraction=.25):
    width, height = body.size
    profile, contacts = ground_profile(body, baseline, band_fraction)
    s, x, z = SHADOW_SCALE, SHADOW_X, SHADOW_Z
    affine = (s, x*s/z, width/2 - .75*width*s - x*.25*height*s/z,
              0, s/z, baseline - .25*height*s/z)
    alpha = body.getchannel("A").transform(body.size, Image.Transform.AFFINE, affine,
                                          resample=Image.Resampling.BILINEAR)
    supersample = 3
    footprint = Image.new("L", (width*supersample, height*supersample))
    draw = ImageDraw.Draw(footprint)
    sin_pitch = math.sin(math.radians(60))
    valid = {x for x, _ in contacts}

    def point(x, foot, contact):
        depth = baseline-foot
        u = x-width/2 + (0 if contact else SHADOW_X*depth)
        v = depth/sin_pitch if contact else SHADOW_Z*depth
        return ((.75*width+u/SHADOW_SCALE)*supersample,
                (.25*height-v/SHADOW_SCALE)*supersample)
    # Include the illustrated plinth's ground footprint, from its actual
    # screen-space contact contour to the projected volume. Union the masks
    # before applying opacity so their overlap never creates a darker stripe.
    # Arches and foliage outside the base band contribute no footprint.
    for x, left in contacts:
        right = profile[x+1] if x+1 in valid else left
        draw.polygon([point(x,left,True), point(x+1,right,True),
                      point(x+1,right,False), point(x,left,False)], fill=255)
    footprint = footprint.resize(body.size, Image.Resampling.LANCZOS)
    alpha = ImageChops.lighter(alpha, footprint)
    alpha = alpha.filter(ImageFilter.GaussianBlur(.7)).point(lambda a: round(a*.26))
    shadow = Image.new("RGBA", body.size, (7, 10, 17, 0))
    shadow.putalpha(alpha)
    return shadow


def normalize_gate_span(image, width, height):
    """Match the two authored transparent portals to the canonical 2.5m gates.

    This is deterministic atlas geometry normalization: retain all drawn
    parts, set both centres to x=+-5 in a 20m field, and preserve true alpha.
    """
    alpha = image.getchannel("A")
    y = round(image.height*.7)
    runs = []
    start = None
    for x in range(image.width+1):
        clear = x < image.width and alpha.getpixel((x,y)) < 128
        if clear and start is None:
            start = x
        elif not clear and start is not None:
            if x-start > image.width*.025:
                runs.append((start,x))
            start = None
    assert len(runs) == 2, f"Expected two transparent portal runs, got {runs}"
    source_knots = [0, runs[0][0], runs[0][1], runs[1][0], runs[1][1], image.width]
    target_knots = [0, round(width*.1875), round(width*.3125),
                    round(width*.6875), round(width*.8125), width]
    output = Image.new("RGBA", (width,height))
    for index in range(len(source_knots)-1):
        part = image.crop((source_knots[index],0,source_knots[index+1],image.height))
        part = part.resize((target_knots[index+1]-target_knots[index],height),Image.Resampling.LANCZOS)
        output.paste(part,(target_knots[index],0))
    alpha = output.getchannel("A")
    near = alpha.point(lambda a: 255 if a >= 128 else 0).filter(ImageFilter.MaxFilter(9))
    output.putalpha(ImageChops.darker(alpha, near))
    return output


def alpha_qa(body, shadow, baseline, band_fraction=.25):
    a, sa = body.getchannel("A"), shadow.getchannel("A")
    bounds = a.getbbox()
    assert bounds and bounds[0] > 0 and bounds[1] > 0
    assert bounds[2] < body.width and bounds[3] < body.height
    assert sa.getbbox() and max(sa.getextrema()) <= 67
    opaque = a.point(lambda n: 255 if n >= 128 else 0)
    partial = a.point(lambda n: 255 if 0 < n < 128 else 0)
    far = ImageChops.subtract(partial, opaque.filter(ImageFilter.MaxFilter(9)))
    fringe = sum(v > 0 for v in far.tobytes())
    assert fringe == 0, f"Detached partial-alpha fringe: {fringe}"
    total = sx = sy = 0
    for y in range(sa.height):
        for x in range(sa.width):
            value = sa.getpixel((x, y))
            total += value
            sx += value*x
            sy += value*y
    centroid = [round(sx/total, 2), round(sy/total, 2)]
    assert centroid[0] < .75*body.width
    assert centroid[1] > .25*body.height
    assert not any(sa.crop(edge).getbbox() for edge in
                   [(0, 0, body.width, 1), (0, body.height-1, body.width, body.height),
                    (0, 0, 1, body.height), (body.width-1, 0, body.width, body.height)])
    opaque_bounds = opaque.getbbox()
    _, footprint = ground_profile(body, baseline, band_fraction)
    contact_samples = 0
    for foot_x, foot_y in footprint:
        px = round(.75*body.width + (foot_x-body.width/2)/SHADOW_SCALE)
        py = round(.25*body.height - (baseline-foot_y)/math.sin(math.radians(60))/SHADOW_SCALE)
        if sa.crop((px-1,py-1,px+2,py+2)).getextrema()[1] >= 8:
            contact_samples += 1
    coverage = contact_samples/max(1,len(footprint))
    assert coverage >= .98, f"Ground shadow must connect across the full footprint: {coverage}"
    return {"bodyBounds": list(bounds), "bodyAlphaMax": a.getextrema()[1],
            "shadowAlphaMax": sa.getextrema()[1], "shadowCentroid": centroid,
            "detachedPartialAlphaPixels": fringe, "bottomContourShadowContacts": contact_samples,
            "footprintColumns": len(footprint), "footprintContactCoverage": coverage,
            "footprintBandFraction": band_fraction,
            "shadowClipped": False, "baseline": baseline}


def prepare(name, raw_name, boxes, columns, tile, baseline, target_height, names,
            folder, visible_width, max_width=None, footprint_bands=None):
    source = Image.open(SOURCE / raw_name)
    images, removed = [], []
    for box in boxes:
        image, flecks = clean_alpha(source.crop(box))
        images.append(image.crop(image.getbbox()))
        removed.append(flecks)
    scales = []
    for index, image in enumerate(images):
        h = target_height[index] if isinstance(target_height, list) else target_height
        scales.append(min(h/image.height, (tile[0]-32)/image.width))
    if not isinstance(target_height, list):
        scales = [min(scales)] * len(images)
    rows = (len(images) + columns - 1)//columns
    body_sheet = Image.new("RGBA", (tile[0]*columns, tile[1]*rows))
    baked_sheet = Image.new("RGBA", (tile[0]*columns, tile[1]*rows*2))
    frames, qa = [], []
    for index, (image, scale) in enumerate(zip(images, scales)):
        width = round(image.width*scale)
        height = round(image.height*scale)
        # Wall strips use a calibrated elevation height, independent of span.
        if max_width is not None:
            width = max_width
            height = target_height
            image = normalize_gate_span(image, width, height)
        else:
            image = image.resize((width, height), Image.Resampling.LANCZOS)
        body = Image.new("RGBA", tile)
        body.alpha_composite(image, ((tile[0]-width)//2, baseline-height))
        band = .25 if footprint_bands is None else footprint_bands[index]
        shadow = paired_shadow(body, baseline, band)
        report = alpha_qa(body, shadow, baseline, band)
        report.update(name=names[index], removedSourceFleckPixels=removed[index])
        qa.append(report)
        col, row = index % columns, index//columns
        position = (col*tile[0], row*tile[1])
        body_sheet.paste(body, position)
        baked_sheet.paste(body, position)
        baked_sheet.paste(shadow, (position[0], (row+rows)*tile[1]))
        frames.append({"name": names[index], "column": col, "bodyRow": row,
                       "shadowRow": row+rows, "bodyBounds": report["bodyBounds"],
                       "footprintBandFraction": band})
    destination = OUTPUT / folder
    destination.mkdir(parents=True, exist_ok=True)
    body_sheet.save(destination / f"{name}.png")
    baked_sheet.save(destination / f"{name}-baked.png")
    return {"id": name, "role": folder, "path": f"{folder}/{name}-baked.png",
            "bodyOnlyPath": f"{folder}/{name}.png", "status": "awaiting-user-review",
            "sha256": hashlib.sha256((destination/f"{name}-baked.png").read_bytes()).hexdigest(),
            "technical": {"dimensions": list(baked_sheet.size), "tileSize": list(tile),
                          "columns": columns, "bodyRows": rows, "shadowRowOffset": rows,
                          "alpha": True, "bodyPivot": [.5, 1-baseline/tile[1]],
                          "shadowPivot": [.75, .75], "shadowScale": SHADOW_SCALE,
                          "shadowFootprint": "lower contour within per-frame base band; camera pitch 60 degrees",
                          "pixelsPerUnit": tile[0], "filtering": "bilinear",
                          "mipmaps": False, "compression": "none",
                          "visibleWorldWidth": visible_width, "frames": frames},
            "visual_locks": ["B3", "palette", "silhouette", "upper-right-light",
                             "lower-left-shadow", "facing"],
            "source": {"kind": "generated-and-normalized", "url_or_tool": "built-in image_gen",
                       "creator": "OpenAI imagegen / Codex technical normalization",
                       "created_at": "2026-11-16", "license": "generated for this project",
                       "attribution": "", "raw": str((SOURCE/raw_name).relative_to(ROOT)).replace('\\', '/'),
                       "prompt_or_source_record": "artifacts/tower-duel/environment-b3-v1/prompts"},
            "verification": {"native_scale_reviewed": False, "in_engine_reviewed": False,
                             "technical_checks_passed": True, "approved_by": ""}}, qa


def grid_boxes(path, cols, rows):
    with Image.open(SOURCE/path) as image:
        width, height = image.size
    return [(round(col*width/cols), round(row*height/rows),
             round((col+1)*width/cols), round((row+1)*height/rows))
            for row in range(rows) for col in range(cols)]


def main():
    assets, reports = [], {}
    for name in ("keep", "tower"):
        raw = name + "-static-lit-raw.png"
        asset, report = prepare(name+"-static", raw, grid_boxes(raw, 2, 2), 2,
                                (512, 512), 484, 448, VARIANTS, "buildings",
                                3.8 if name == "keep" else 2.6)
        assets.append(asset)
        reports[name] = report
    raw = "wall-gates-atlas-raw.png"
    asset, report = prepare("wall-gates", raw, grid_boxes(raw, 1, 2), 1,
                            (2048, 256), 236, 212, ["south", "north"],
                            "wall", 20, max_width=2016)
    assets.append(asset)
    reports["wall"] = report
    boxes = [(0, 0, 565, 670), (565, 0, 1024, 670),
             (0, 670, 565, 1110), (565, 670, 1024, 1110),
             (0, 1110, 565, 1536), (565, 1110, 1024, 1536)]
    asset, report = prepare("border-props", "border-props-atlas-raw.png", boxes, 2,
                            (384, 384), 354, [330, 270, 200, 190, 130, 120],
                            ["tree-large", "tree-small", "bush", "boulder", "rocks", "flowers"],
                            "props", [2.8, 1.8, 1.1, .9, 1.05, .6],
                            footprint_bands=[.10, .10, .25, .4, .4, .15])
    assets.append(asset)
    reports["props"] = report
    (OUTPUT / "terrain").mkdir(parents=True, exist_ok=True)
    for view in ("south", "north"):
        image = Image.open(SOURCE / f"ground-{view}-raw.png").convert("RGB")
        assert image.size == (1024, 1536)
        image.save(OUTPUT / f"terrain/ground-{view}.png")
        assets.append({"id": f"ground-{view}", "role": "terrain",
                       "path": f"terrain/ground-{view}.png", "status": "awaiting-user-review",
                       "technical": {"dimensions": list(image.size), "alpha": False,
                                     "worldSize": [20, 30], "filtering": "bilinear", "mipmaps": False},
                       "visual_locks": ["B3", "upper-right-light", "continuous-mud-through-gate"],
                       "source": {"kind": "generated", "url_or_tool": "built-in image_gen",
                                  "created_at": "2026-11-16", "license": "generated for this project",
                                  "prompt_or_source_record": f"artifacts/tower-duel/environment-b3-v1/prompts/ground-{view}.txt"},
                       "verification": {"native_scale_reviewed": False, "in_engine_reviewed": False,
                                        "technical_checks_passed": True, "approved_by": ""}})
    write_json(OUTPUT / "manifest.json", {"schema_version": 1,
        "art_direction_brief": "examples/tower-duel/art/environment-b3-v1/README.md",
        "scope": "environment artwork; not deployed to the battle scene",
        "camera": {"pitch": 60, "orthographic": True, "bodyUprightScaleY": "1/camera.up.y"},
        "lighting": {"source": "screen-upper-right", "castShadow": "screen-lower-left",
                     "body": "authored in imagegen; no runtime self-shading overlay",
                     "shadow": "paired sprite in same PNG, projected offline from body silhouette"},
        "viewMapping": {"southSeat": {"ground": "south", "wall": "south", "own": "blue-away", "enemy": "red-toward"},
                        "northSeat": {"ground": "north", "wall": "north", "own": "red-away", "enemy": "blue-toward"}},
        "assets": assets})
    write_json(SOURCE / "technical-qa.json", {"passed": True, "frames": sum(map(len, reports.values())),
                                             "assets": reports})
    print(json.dumps({"passed": True, "atlasFrames": sum(map(len, reports.values())),
                      "terrainViews": 2, "output": str(OUTPUT)}, ensure_ascii=True))


if __name__ == "__main__":
    main()
