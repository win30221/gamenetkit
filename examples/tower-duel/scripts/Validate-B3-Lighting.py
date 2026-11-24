"""Check actual Unity GPU captures for light response and connected foot shadows.

Run after TowerDuelSpriteReview.Capture(); Pillow is the only dependency.
"""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageStat, ImageFilter


def mean_patch(image, x, y):
    return ImageStat.Stat(image.crop((x-8, y-8, x+8, y+8))).mean


def luminance(rgb):
    return sum(a*b for a, b in zip(rgb, (.2126, .7152, .0722)))


def validate(folder):
    probes = json.loads((folder/'lighting-probes.json').read_text())
    results, rows = [], []
    for probe in probes:
        view = probe['view']
        names = ('day', 'ambient', 'warm', 'shadow', 'no-shadow', 'no-contact', 'hit', 'recovered')
        images = {n: Image.open(folder/f'lighting-{view}-{n}.png').convert('RGB') for n in names}
        colors = {n: mean_patch(im, probe['sampleX'], probe['sampleY']) for n, im in images.items()}
        # Isolate the actual sprite silhouette. A wider review camera also sees
        # blue river pixels, which must not be counted as blue team clothing.
        repo=Path(__file__).resolve().parents[3]
        resource=repo/'examples/tower-duel/client/Assets/Resources/TowerDuel/B3/Units'/probe['kind']
        definition=json.loads((resource/'definition.json').read_text())
        tile=definition['tileSize'];height=definition['worldHeight']
        pixels_per_unit=600/(2*max(1.15,height*1.15))*height*tile/definition['referenceHeight']
        alpha=Image.open(resource/'idle.png').convert('RGBA').crop((0,0,tile,tile)).getchannel('A')
        alpha=alpha.resize((round(pixels_per_unit),round(pixels_per_unit)),Image.Resampling.LANCZOS)
        mask=Image.new('L',(600,600));mask.paste(alpha,(round(probe['footX']-pixels_per_unit*.5),round(probe['footY']-pixels_per_unit*definition['baseline']/tile)))
        mask=mask.point(lambda n:255 if n>250 else 0).filter(ImageFilter.MinFilter(5))
        sprite=list(getattr(mask,'get_flattened_data',mask.getdata)())
        team_palette=None
        enemy_path=folder/f'lighting-{view}-enemy.png'
        if enemy_path.exists():
            normal=images['day'];enemy_image=Image.open(enemy_path).convert('RGB')
            day=list(getattr(normal,'get_flattened_data',normal.getdata)());enemy=list(getattr(enemy_image,'get_flattened_data',enemy_image.getdata)())
            top=probe.get('teamMaskTopY',0)
            blue=[i for i,(r,g,b) in enumerate(day) if sprite[i] and i//normal.width>=top and b>100 and b-r>40 and b-g>28 and g>=r*.9]
            if blue:
                assert sum(enemy[i][0] for i in blue)>sum(enemy[i][2] for i in blue)*1.3, f'{view}: team blue did not become red'
            purple=[i for i,(r,g,b) in enumerate(day) if sprite[i] and r>g*1.3 and b>g*1.3 and min(r,b)>40]
            if probe.get('kind')=='bomber':
                assert len(purple)>50, f'{view}: bomber lost its purple identity'
                assert sum(sum(abs(a-b) for a,b in zip(day[i],enemy[i])) for i in purple)/len(purple)<3, f'{view}: team palette recolored the purple uniform'
                protected=ImageChops.difference(normal,enemy_image).crop((0,0,normal.width,max(1,min(normal.height,top-2))))
                assert max(high for low,high in protected.getextrema())<3, f'{view}: bomber goggles or hat changed team color'
            team_palette=dict(bluePixels=len(blue),purplePixels=len(purple))
        fixed=probe.get('baked',False)
        if fixed:
            for case in ('ambient','warm'):
                difference=ImageChops.difference(images['day'],images[case])
                assert max(ImageStat.Stat(difference,mask=mask).mean)<1.5,f'{view}: baked body was lit a second time ({case})'
        else:
            assert luminance(colors['day']) > luminance(colors['ambient'])*1.8, f'{view}: sprite ignored main-light intensity'
        assert luminance(colors['shadow']) < luminance(colors['day'])*.8, f'{view}: sprite did not receive the occluder shadow'
        if not fixed:assert colors['warm'][0]/max(1, colors['warm'][1]) > colors['day'][0]/max(1, colors['day'][1])*1.35, f'{view}: sprite ignored light color'
        # Larger units can put the fixed world-space probe on dark boot ink,
        # which intentionally preserves its color during a hit. Test the body.
        hit_body=ImageStat.Stat(images['hit'],mask=mask).mean
        day_body=ImageStat.Stat(images['day'],mask=mask).mean
        assert hit_body[0]/max(1,hit_body[1]) > day_body[0]/max(1,day_body[1])*1.5, f'{view}: damage flash was not red'
        assert max(abs(a-b) for a,b in zip(colors['day'],colors['recovered']))<2, f'{view}: damage flash did not clear'
        direction=None
        reverse_path=folder/f'lighting-{view}-reverse.png'
        if reverse_path.exists() and probe.get('leftX'):
            reverse=Image.open(reverse_path).convert('RGB')
            reference_pixels=pixels_per_unit*definition['referenceHeight']/tile
            def side_colors(sign):
                offsets=sorted((sign*.10*reference_pixels,sign*.29*reference_pixels))
                region=Image.new('L',mask.size)
                ImageDraw.Draw(region).rectangle((round(probe['footX']+offsets[0]),round(probe['footY']-.85*reference_pixels),round(probe['footX']+offsets[1]),round(probe['footY']-.35*reference_pixels)),fill=255)
                region=ImageChops.multiply(region,mask)
                assert region.getbbox(), f'{view}: no sprite pixels in directional-light sample'
                return [luminance(ImageStat.Stat(im,mask=region).mean) for im in (images['day'],reverse)]
            left_day,left_reverse=side_colors(-1)
            right_day,right_reverse=side_colors(1)
            if fixed:
                assert abs(left_reverse-left_day)<2 and abs(right_reverse-right_day)<2,f'{view}: fixed light direction changed after baking'
            else:assert left_reverse>left_day*1.4 and right_day>right_reverse*1.4, f'{view}: left/right surfaces did not respond to light direction'
            direction=dict(left=[left_day,left_reverse],right=[right_day,right_reverse])
        delta = ImageChops.difference(images['day'], images['no-shadow']).convert('L').point(lambda n: 255 if n > 5 else 0)
        bounds = delta.getbbox()
        x, y = probe['footX'], probe['footY']
        assert bounds and bounds[0] < x and bounds[3] > y, f'{view}: ground shadow did not extend lower-left'
        assert delta.crop((x-85, y-4, x+85, y+8)).getbbox(), f'{view}: ground shadow detached from the boots'
        contact = ImageChops.difference(images['day'], images['no-contact']).getbbox()
        if fixed:assert not contact,f'{view}: extra contact shadow doubled the baked shadow'
        else:assert contact, f'{view}: missing feathered foot contact'
        results.append(dict(view=view, colors=colors, shadowBounds=bounds, contactBounds=contact,teamPalette=team_palette,directionalShading=direction))
        rows.append([images[n] for n in ('day', 'ambient', 'warm', 'shadow')])
    for name in ('day', 'ambient', 'warm', 'shadow'):
        assert max(abs(a-b) for a, b in zip(results[0]['colors'][name], results[1]['colors'][name])) < 3, f'{name}: lighting differs between seats'
    fixed=all(p.get('baked',False) for p in probes)
    report = dict(passed=True, views=results, checks=(['fixed baked color','no duplicate contact'] if fixed else ['intensity','light color','contact shadow'])+['received building shadow','connected foot shadow','red hit and recovery','both seats'])
    if all(result['teamPalette'] is not None for result in results):report['checks'].append('team palette')
    if all(result['directionalShading'] is not None for result in results):report['checks'].append('baked light remains fixed' if fixed else 'left/right surface lighting')
    (folder/'lighting-validation.json').write_text(json.dumps(report, indent=2)+'\n')
    # A review contact sheet only; the imported artwork is not modified.
    sheet = Image.new('RGB', (1200, 660), '#152435')
    draw = ImageDraw.Draw(sheet)
    font_path = Path('C:/Windows/Fonts/msjh.ttc')
    font = ImageFont.truetype(str(font_path), 18) if font_path.exists() else ImageFont.load_default()
    for row, images in enumerate(rows):
        for col, (im, title) in enumerate(zip(images, ('正常主光', '只有環境光', '暖色主光', '遮光物陰影'))):
            px, py = col*300, row*330
            draw.text((px+10, py+6), f'{"我方" if row == 0 else "對方"}・{title}', fill='white', font=font)
            sheet.paste(im.resize((300, 300), Image.Resampling.LANCZOS), (px, py+30))
    sheet.save(folder/'lighting-comparison.png')
    for probe in probes:
        view=probe['view']
        pictures=[Image.open(folder/f'lighting-{view}-{n}.png').convert('RGB') for n in ('day','hit','recovered')]
        pictures[0].save(folder/f'hit-{view}.gif',save_all=True,append_images=pictures[1:],duration=[450,130,450],loop=0)
    motion=folder/'motion'
    if (motion/'timing.json').exists():
        timing=json.loads((motion/'timing.json').read_text())
        for clip in ('walk','attack'):
            count=timing.get(clip+'Frames',6)
            impact=timing.get('impactFrame',3)
            pictures=[Image.open(motion/f'{clip}-{i}.png').convert('RGB') for i in range(count)]
            durations=([max(1,round(1000/timing.get('walkFps',8)))]*count if clip=='walk' else
                       [max(1,round(timing['windupTicks']/30/impact*1000))]*impact+
                       [max(1,round((timing['cooldownTicks']-timing['windupTicks'])/30/(count-impact)*1000))]*(count-impact))
            pictures[0].save(folder/f'{clip}-native.gif',save_all=True,append_images=pictures[1:],duration=durations,loop=0)
    print(json.dumps(dict(passed=True, views=len(results), checks=report['checks'])))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    validate(parser.parse_args().folder)
