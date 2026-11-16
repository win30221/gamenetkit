"""Compare normalized B3 direction/animation poses without changing imported art."""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw

repo=Path(__file__).resolve().parents[3]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('kind')
kind=parser.parse_args().kind
config=json.loads((repo/f'examples/tower-duel/art/b3-production/{kind}.json').read_text(encoding='utf-8-sig'))
tile=config['tileSize']
root=repo/f'examples/tower-duel/client/Assets/Resources/TowerDuel/B3/Units/{kind}'
samples=[('idle',0),('walk',0),('walk',3),('attack',0),('attack',2),('attack',3),('attack',5)]
textures={name:Image.open(root/f'{name}.png').convert('RGBA') for name in ('idle','walk','attack')}
canvas=Image.new('RGB',(len(samples)*tile,8*(tile+20)),(87,106,67))
draw=ImageDraw.Draw(canvas)
for row,direction in enumerate(config['directions']):
    for column,(clip,frame) in enumerate(samples):
        image=textures[clip].crop((frame*tile,row*tile,(frame+1)*tile,(row+1)*tile))
        canvas.paste(image,(column*tile,row*(tile+20)),image)
        draw.text((column*tile+4,row*(tile+20)+tile+3),f'{direction} / {clip} {frame}',fill='white')
    draw.line((0,row*(tile+20)+config['baseline'],len(samples)*tile,row*(tile+20)+config['baseline']),fill=(125,145,110))
output=repo/f'artifacts/tower-duel/sprites-b3/{kind}/pose-comparison.png'
output.parent.mkdir(parents=True,exist_ok=True)
canvas.save(output)
print(str(output))
