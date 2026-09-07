"""Render native-terrain comparisons from a saved V13 matrix (requires Pillow).

Usage: python Render-RegionsExtended.py MATRIX_DIRECTORY
Outputs are diagnostic exports of actual saved maps, not game UI screenshots.
"""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

PALETTE = [(139, 104, 73), (64, 93, 128), (126, 126, 118), (65, 130, 77)]
FONT = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 15)
SMALL = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 12)

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def markers(im, ref):
    d = ImageDraw.Draw(im)
    side = im.width
    for pos in ref['neutral_preview_positions']:
        x = (pos['x'] - ref['bounds_left'] + .5) * side / ref['size']
        y = (pos['y'] - ref['bounds_top'] + .5) * side / ref['size']
        d.rectangle((x-3, y-3, x+3, y+3), fill='black')
        d.rectangle((x-2, y-2, x+2, y+2), fill=(160, 160, 160))
    for i, pos in enumerate(ref['player_starts']):
        x = (pos['x'] - ref['bounds_left'] + .5) * side / ref['size']
        y = (pos['y'] - ref['bounds_top'] + .5) * side / ref['size']
        d.ellipse((x-8, y-8, x+8, y+8), fill='#202020', outline='white')
        d.text((x, y), chr(65+i), font=SMALL, anchor='mm', fill='white')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('matrix', type=Path)
    args = parser.parse_args()
    folder = args.matrix.resolve()
    manifest = read(folder/'manifest.json')
    out = folder/'review'
    out.mkdir(exist_ok=True)
    rows = []
    for offset in range(0, len(manifest), 5):
        cases = manifest[offset:offset+5]
        row = Image.new('RGB', (1320, 340), '#171b20')
        d = ImageDraw.Draw(row)
        for col, case in enumerate(cases):
            p = folder/case['id']
            ref = read(p/'actual/reference.json')
            settings = case['settings']
            size = ref['size']
            im = Image.new('RGB', (size, size))
            im.putdata([PALETTE[value] for value in (p/'actual/semantic.u8').read_bytes()])
            im = im.resize((256, 256), Image.Resampling.NEAREST)
            row.paste(im, (264*col, 28))
            d.text((264*col, 5), case['id'], font=FONT, fill='white')
            metrics = ref['metrics']
            d.text((264*col, 289), f"Water {metrics['water_percent_map']:.1f}%  G/M {metrics['gravel_percent_land']:.1f}/{metrics['moss_percent_land']:.1f}", font=SMALL, fill='white')
            d.text((264*col, 307), f"Seed {settings['seed']}", font=SMALL, fill='white')
            d.text((264*col, 323), f"W:{settings['water_amount']} G:{settings['gravel_moss_amount']} R:{settings['original_surface_relations']}", font=SMALL, fill='#bac0c7')
        group = offset//5+1
        row.save(out/f'comparison-{group:02}.jpg', quality=92)
        rows.append(row)
        if group in (2, 4, 7, 13, 15, 17):
            sheet = Image.new('RGB', (1940, 434), '#171b20')
            draw = ImageDraw.Draw(sheet)
            for col, case in enumerate(cases):
                p = folder/case['id']
                with Image.open(p/'actual/textures.png') as source:
                    im = source.convert('RGB').resize((384, 384), Image.Resampling.LANCZOS)
                markers(im, read(p/'actual/reference.json'))
                sheet.paste(im, (col*388, 48))
                draw.text((col*388+4, 4), case['id'], font=FONT, fill='white')
                draw.text((col*388+4, 25), 'Actual saved terrain; actor positions overlaid', font=SMALL, fill='#bac0c7')
            sheet.save(out/f'textures-{group:02}.jpg', quality=93)
    for start in range(0, len(rows), 6):
        page = Image.new('RGB', (1320, 340*len(rows[start:start+6])), '#171b20')
        for i, row in enumerate(rows[start:start+6]):
            page.paste(row, (0, i*340))
        page.save(out/f'overview-{start//6+1}.jpg', quality=85)
    print(out)

if __name__ == '__main__':
    main()
