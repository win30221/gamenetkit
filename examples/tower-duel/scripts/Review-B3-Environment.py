"""Lay out native Unity captures and sprite alpha contact sheets for review."""
from pathlib import Path
import json

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / "examples/tower-duel/art/environment-b3-v1"
REVIEW = ROOT / "artifacts/tower-duel/environment-b3-v1/review"


def font(size):
    return ImageFont.truetype("C:/Windows/Fonts/msjh.ttc", size)


def main():
    native = [REVIEW/"south-blue-view.png", REVIEW/"north-red-view.png"]
    board = Image.new("RGB", (1536, 1120), "#fff5df")
    draw = ImageDraw.Draw(board)
    draw.text((32, 16), "雙門伏擊戰場 · B3 美術第一版", font=font(31), fill="#172b45")
    draw.text((32, 62), "我方武器朝上攻向對面 · 右上受光／左下投影", font=font(20), fill="#47566a")
    for index, path in enumerate(native):
        image = Image.open(path).crop((0, 120, 1024, 1390))
        image.save(ART / ("preview.png" if index == 0 else "preview-north.png"))
        image = image.resize((736, 913), Image.Resampling.LANCZOS)
        x = 24+index*752
        label = "藍方視角｜右側泥路" if index == 0 else "紅方視角｜左側泥路"
        draw.text((x, 105), label, font=font(22), fill="#1753a4" if index == 0 else "#a52435")
        board.paste(image, (x, 145))
    draw.text((32, 1076), "Unity 相機實際渲染；此批為靜態美術，尚未接入地形減速與視線規則。", font=font(20), fill="#47566a")
    board.save(REVIEW/"environment-overview.png")

    manifest = json.loads((ART/"manifest.json").read_text(encoding="utf-8-sig"))
    board = Image.new("RGB", (1280, 1720), "#f7f1e4")
    draw = ImageDraw.Draw(board)
    draw.text((24, 14), "B3 sprite sheet · body alpha review", font=font(26), fill="#172b45")
    positions = []
    for asset in manifest["assets"]:
        if "bodyOnlyPath" not in asset:
            continue
        sheet = Image.open(ART/asset["bodyOnlyPath"]).convert("RGBA")
        width, height = asset["technical"]["tileSize"]
        for frame in asset["technical"]["frames"]:
            col, row = frame["column"], frame["bodyRow"]
            image = sheet.crop((col*width, row*height, (col+1)*width, (row+1)*height))
            if asset["id"] == "wall-gates":
                x, y, cw, ch = 16, 1280+frame["bodyRow"]*204, 1248, 174
            else:
                index = len(positions)
                x, y, cw, ch = (index%4)*320+12, 70+(index//4)*300, 296, 254
                positions.append(frame["name"])
            cell = Image.new("RGBA", (cw,ch), "#dddddd")
            check = ImageDraw.Draw(cell)
            for sy in range(0,ch,16):
                for sx in range(0,cw,16):
                    if (sx//16+sy//16)%2:
                        check.rectangle((sx,sy,sx+15,sy+15),fill="#eeeeee")
            image = image.crop(image.getbbox())
            image.thumbnail((cw-20,ch-20), Image.Resampling.LANCZOS)
            cell.alpha_composite(image, ((cw-image.width)//2,(ch-image.height)//2))
            board.paste(cell.convert("RGB"), (x,y))
            draw.text((x,y+ch+1), asset["id"]+" / "+frame["name"], font=font(14), fill="#172b45")
    board.save(REVIEW/"sprite-contact-sheet.png")
    before_path = REVIEW/"before-contact-fix/south-blue-view.png"
    if before_path.exists():
        before = Image.open(before_path)
        after = Image.open(native[0])
        board = Image.new("RGB", (1400, 1080), "#fff5df")
        draw = ImageDraw.Draw(board)
        draw.text((24, 12), "底座投影接合檢查", font=font(30), fill="#172b45")
        draw.text((24, 58), "修正前", font=font(24), fill="#9b4b4b")
        draw.text((724, 58), "修正後", font=font(24), fill="#245f3a")
        regions = [("牆腳", (80,700,945,865), 106, 210),
                   ("主堡", (330,1100,665,1366), 346, 320),
                   ("箭塔", (155,995,360,1190), 706, 320)]
        for label, box, top, height in regions:
            for index, image in enumerate((before, after)):
                x = 24+index*700
                draw.text((x,top), label, font=font(20), fill="#172b45")
                crop = image.crop(box)
                ratio = min(650/crop.width, (height-32)/crop.height)
                crop = crop.resize((round(crop.width*ratio), round(crop.height*ratio)), Image.Resampling.LANCZOS)
                board.paste(crop, (x+(650-crop.width)//2,top+32))
        board.save(REVIEW/"contact-fix-comparison.png")
    print(json.dumps({"overview": str(REVIEW/"environment-overview.png"),
                      "contactSheet": str(REVIEW/"sprite-contact-sheet.png")}))


if __name__ == "__main__":
    main()
