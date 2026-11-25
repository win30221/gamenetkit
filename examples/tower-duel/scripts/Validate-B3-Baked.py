"""Read-only QA for fixed-light atlases: color, alpha, pairings and foot shadows."""
import json
from pathlib import Path
from PIL import Image,ImageChops,ImageFilter

ROOT=Path(__file__).resolve().parents[3]
UNITS=ROOT/'examples/tower-duel/client/Assets/Resources/TowerDuel/B3/Units'
SOURCES=ROOT/'examples/tower-duel/art/b3-production/atlases'

def luma(pixel):return pixel[0]*.2126+pixel[1]*.7152+pixel[2]*.0722

def validate():
    roles=[];total=0
    for folder in sorted(UNITS.iterdir()):
        if not folder.is_dir():continue
        d=json.loads((folder/'definition.json').read_text());tile=d['tileSize'];reference=d['referenceHeight']
        assert d.get('baked'),f'{folder.name}: not enabled'
        clips=[]
        for clip,columns in (('idle',d.get('idleFrames',1)),('walk',d['walkFrames']),('attack',d['attackFrames'])):
            source=Image.open(SOURCES/folder.name/(clip+'.png')).convert('RGBA')
            assert source.size==(tile*columns,tile*8),f'{folder.name}/{clip}: wrong source grid'
            sheet=Image.open(folder/(clip+'-baked.png')).convert('RGBA')
            assert sheet.size==(tile*columns,tile*16),f'{folder.name}/{clip}: wrong grid'
            body=sheet.crop((0,0,sheet.width,tile*8));shadows=sheet.crop((0,tile*8,sheet.width,tile*16))
            assert not ImageChops.difference(source.getchannel('A'),body.getchannel('A')).getbbox(),f'{folder.name}/{clip}: body alpha changed'
            gains=[]
            for direction in range(8):
                for frame in range(columns):
                    box=(frame*tile,direction*tile,(frame+1)*tile,(direction+1)*tile)
                    original=source.crop(box).load();baked=body.crop(box).load()
                    body_alpha=body.crop(box).getchannel('A')
                    opaque=body_alpha.point(lambda v:255 if v>=128 else 0).filter(ImageFilter.MaxFilter(5))
                    translucent=body_alpha.point(lambda v:255 if 21<=v<128 else 0)
                    stray=ImageChops.subtract(translucent,opaque)
                    assert sum(stray.histogram()[1:])<=64,f'{folder.name}/{clip}/{direction}/{frame}: translucent backdrop or frame fringe'
                    sides=[]
                    for sign in (-1,1):
                        offsets=sorted((sign*.10*reference,sign*.29*reference));before=after=0
                        for y in range(round(d['baseline']-.85*reference),round(d['baseline']-.35*reference),2):
                            for x in range(round(tile*.5+offsets[0]),round(tile*.5+offsets[1]),2):
                                p=original[x,y]
                                if p[3]>240 and max(p[:3])>85:before+=luma(p);after+=luma(baked[x,y])
                        sides.append(after/before if before else None)
                    # An asymmetric leaning pose can contain no painted surface
                    # in one sample band; it still receives every alpha/shadow check.
                    if all(g is not None for g in sides):
                        assert sides[1]>sides[0]*1.06,f'{folder.name}/{clip}/{direction}/{frame}: no baked right-light direction'
                        assert sides[0]>.75,f'{folder.name}/{clip}: dark side crushed'
                        gains.append(sides)
                    alpha=shadows.crop(box).getchannel('A');bounds=alpha.getbbox();assert bounds,f'{folder.name}/{clip}: missing paired shadow'
                    assert 30<alpha.getextrema()[1]<=67,f'{folder.name}/{clip}: wrong shadow opacity'
                    edge=alpha.copy();edge.paste(0,(2,2,tile-2,tile-2))
                    assert edge.getextrema()[1]<=2,f'{folder.name}/{clip}: clipped shadow'
                    patch=alpha.crop((round(tile*.55),round(tile*.25)-3,round(tile*.95),round(tile*.25)+6))
                    assert patch.getextrema()[1]>5,f'{folder.name}/{clip}: shadow detached from boots'
                    pixels=alpha.load();weight=mx=my=0
                    for y in range(bounds[1],bounds[3]):
                        for x in range(bounds[0],bounds[2]):
                            a=pixels[x,y];weight+=a;mx+=a*x;my+=a*y
                    assert mx/weight<tile*.71 and my/weight>tile*.29,f'{folder.name}/{clip}: shadow not lower-left'
                    total+=1
            assert len(gains)>=columns*8*.8,f'{folder.name}/{clip}: insufficient visible surface samples'
            clips.append(dict(clip=clip,frames=columns*8,surfaceSamples=len(gains),leftGain=sum(g[0] for g in gains)/len(gains),rightGain=sum(g[1] for g in gains)/len(gains)))
        roles.append(dict(kind=folder.name,clips=clips))
    report=dict(passed=True,characters=len(roles),bodyFrames=total,pairedShadowFrames=total,checks=['body alpha unchanged','no translucent backdrop','right-lit body pixels','colored readable dark side','same-sheet pairs','soft shadow opacity','unclipped shadows','connected boot shadows','lower-left direction'],roles=roles)
    output=ROOT/'artifacts/tower-duel/sprites-b3/baked-shadow-review/assets-validation.json';output.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:report[k] for k in ('passed','characters','bodyFrames','pairedShadowFrames','checks')}))

if __name__=='__main__':validate()
