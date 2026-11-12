from pathlib import Path
from shutil import copy2
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parent
source = root.parents[2] / 'tests/ConduitPlus.Core.Tests/bin/Debug/net8.0-windows/artifacts/pr-d'
for png in source.glob('bar-*.png'):
    copy2(png, root / png.name)
themes = ['paper', 'night', 'smoke', 'blueprint']
fixtures = ['signed-below', 'signed-above', 'worst-first', 'within', 'within-interaction', 'precision-midpoints',
            'precision-small', 'precision-integers', 'precision-mixed-scale', 'scrolled-selection']
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 11)
width = 360
padding = 10
row_heights = [max(Image.open(root / f'bar-{fixture}-{theme}.png').height // 2 for theme in themes) + 30 for fixture in fixtures]
sheet = Image.new('RGB', (4 * width, sum(row_heights)), '#eeeeee')
draw = ImageDraw.Draw(sheet)
y = 0
for fixture, height in zip(fixtures, row_heights):
    for col, theme in enumerate(themes):
        draw.text((col * width + 8, y + 2), f'{theme} / {fixture}', fill='black', font=font)
        with Image.open(root / f'bar-{fixture}-{theme}.png') as original:
            thumb = original.convert('RGBA')
            thumb.thumbnail((width - padding, height - 30), Image.Resampling.LANCZOS)
            sheet.paste(thumb, (col * width, y + 20), thumb)
    y += height
sheet.save(root / 'bar-review-contact-sheet.png')
contrast = Image.new('RGB', (1440, 740))
draw = ImageDraw.Draw(contrast)
for row, background in enumerate(['#eeeeee', '#202124']):
    draw.rectangle((0, row * 370, 1440, (row + 1) * 370), fill=background)
    for col, theme in enumerate(themes):
        draw.text((col * 360 + 8, row * 370 + 2), f'{theme} on {background}', fill='black' if row == 0 else 'white', font=font)
        with Image.open(root / f'bar-scrolled-selection-{theme}.png') as original:
            thumb = original.convert('RGBA')
            thumb.thumbnail((350, 340), Image.Resampling.LANCZOS)
            contrast.paste(thumb, (col * 360 + 5, row * 370 + 25), thumb)
contrast.save(root / 'bar-data-contrast-light-dark.png')
print(f'Rendered {len(list(root.glob("bar-*.png")))} PNGs in {root}')
print(f'Contact sheet: {sheet.size}')
