"""대시 먼지 밝게 + 1px 외곽선(2026-10-08). 원본: Pimen 'Smoke n Dust 03'(https://pimen.itch.io/smoke-n-dust-03).
원본 Assets/Art/Fx/Dash/SmokeNDust P03 VFX N.png 는 그대로 두고 밝은 사본을 만든다.
  VFX 2(64×64 8칸, 옆으로 차는 먼지) → dash_dust_kick.png  (대시 시작)
  VFX 5(32×32 8칸, 작은 먼지)        → dash_dust_trail.png (대시 중 꼬리)
실행: python Art_Source/fx/brighten_dash_dust.py (프로젝트 루트)
"""
from PIL import Image

MAP = {(179, 194, 202): (240, 244, 246), (133, 154, 169): (196, 206, 214)}
OUTLINE = (52, 54, 66, 255)
D = 'Assets/Art/Fx/Dash/'


def convert(src, dst, cell):
    im = Image.open(D + src).convert('RGBA')
    w, h = im.size
    out = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    px, po = im.load(), out.load()
    for y in range(h):
        for x in range(w):
            p = px[x, y]
            if p[3] > 0:
                po[x, y] = MAP.get(p[:3], p[:3]) + (255,)
    # 1px 외곽선(칸 경계를 넘지 않게)
    for y in range(h):
        for x in range(w):
            if px[x, y][3] > 0:
                continue
            cx = x // cell
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and nx // cell == cx and px[nx, ny][3] > 0:
                    po[x, y] = OUTLINE
                    break
    out.save(D + dst)


convert('SmokeNDust P03 VFX 2.png', 'dash_dust_kick.png', 64)
convert('SmokeNDust P03 VFX 5.png', 'dash_dust_trail.png', 32)
print('ok')
