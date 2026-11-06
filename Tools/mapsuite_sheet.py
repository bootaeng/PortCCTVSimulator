"""Contact sheet + crops from a map-suite capture folder. Usage: mapsuite_sheet.py <capture_dir> [docs_images_dir]"""
import glob
import os
import sys
from PIL import Image

d = sys.argv[1]
docs = sys.argv[2] if len(sys.argv) > 2 else None
n = sorted(x for x in glob.glob(os.path.join(d, '*_default')) if os.path.isdir(x))
s = Image.new('RGB', (1920, 360 * ((len(n) + 1) // 2)))
for i, x in enumerate(n):
    s.paste(Image.open(os.path.join(x, 'rgb.png')).convert('RGB').resize((960, 360)), ((i % 2) * 960, (i // 2) * 360))
s.save(os.path.join(d, 'sheet.png'))
crops = {
    'crop_ctB_hill.png': ('container_terminal_01_ct_B_breakwater_head_default', (0, 0, 960, 540)),
    'crop_ctD_low.png': ('container_terminal_01_ct_D_hill_tower_default', (0, 540, 1920, 1080)),
    'crop_fpB_low.png': ('fishing_port_01_fp_B_hillside_default', (0, 540, 1920, 1080)),
    'crop_mrB_low.png': ('marina_01_mr_B_tower_default', (0, 540, 1920, 1080)),
}
for name, (folder, box) in crops.items():
    p = os.path.join(d, folder, 'rgb.png')
    if os.path.exists(p):
        Image.open(p).crop(box).save(os.path.join(d, name))
if docs:
    s.save(os.path.join(docs, 'maps_player_sheet.png'))
    for folder, out in [('container_terminal_01_ct_A_quay_east_mast_default', 'map_container_camA'),
                        ('container_terminal_01_ct_D_hill_tower_default', 'map_container_camD'),
                        ('fishing_port_01_fp_A_market_roof_default', 'map_fishing_port_camA'),
                        ('fishing_port_01_fp_B_hillside_default', 'map_fishing_port_camB'),
                        ('fishing_port_01_fp_C_breakwater_default', 'map_fishing_port_camC'),
                        ('marina_01_mr_A_promenade_default', 'map_marina_camA'),
                        ('marina_01_mr_B_tower_default', 'map_marina_camB')]:
        Image.open(os.path.join(d, folder, 'rgb.png')).convert('RGB').resize((1280, 720)).save(os.path.join(docs, out + '.png'))
print('sheet ok', len(n))
