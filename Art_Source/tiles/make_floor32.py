# 진짜 32px 바닥 타일 생성(B안, 2026-10-06). 원본 배치 기준 시트(tileset_{key}_32.png, 원본 2배 + 속성 색)에서
# 바닥 칸의 '바닥 무늬 색' 픽셀만 32px 디테일 바닥 텍스처로 바꾼다. 테두리·그림자·벽 픽셀은 그대로 둬서 이음매·배치는 원본과 같다.
# 텍스처는 32px 주기로 끝이 이어지는(토러스) 기본판 하나를 모든 칸이 공유하고, 장식(자갈·새싹·금)은 칸 안쪽 3px 여백 안에만
# 넣어 어떤 변형끼리 붙어도 이음매가 맞는다. 색은 백업 AI판(backup_v1)의 같은 속성 바닥에서 뽑았다.
from PIL import Image
import numpy as np
import random, sys

KEY = sys.argv[1] if len(sys.argv) > 1 else 'earth'
sys.path.insert(0, 'Art_Source/tiles')
from tile_palettes import FLOOR
PAL = FLOOR[KEY]

o = np.array(Image.open('Assets/ThirdParty/EdgarExample1/Example1/example1_tileset.png').convert('RGBA')).astype(int)
FLOOR_PATTERN = {(61, 37, 59), (54, 32, 48), (66, 44, 59), (72, 48, 64)}  # 원본 바닥 면·줄눈 색
FLOOR_CELLS = [(0, 6), (0, 7), (0, 8), (0, 9), (1, 1), (1, 2), (1, 3), (1, 4), (1, 6), (1, 7), (1, 8), (1, 9),
               (2, 1), (2, 2), (2, 3), (2, 4), (2, 6), (2, 7), (2, 8), (2, 9), (3, 1), (3, 2), (3, 3), (3, 4),
               (6, 0), (6, 1), (6, 2), (6, 3), (7, 0), (7, 1), (7, 2), (7, 3)]
# Floor random 타일이 쓰는 칸 → 장식 종류
DECOR = {(7, 0): 'plain', (7, 1): 'pebbles', (7, 2): 'sprout', (7, 3): 'crack',
         (0, 6): 'plain', (0, 7): 'sprouts', (0, 8): 'stone', (2, 2): 'plain2', (2, 3): 'tiny'}


def base_texture(seed=7):
    """32px 주기로 이어지는 흙 바닥. 얼룩·반점을 지터 격자로 고르게 흩어 줄무늬 반복이 안 보이게 한다."""
    rnd = random.Random(seed)
    t = np.zeros((32, 32, 3), int)
    t[:] = PAL['base']

    def put(x, y, c):
        t[y % 32, x % 32] = c

    def blob(cx, cy, color, rx, ry):
        for yy in range(-ry, ry + 1):
            for xx in range(-rx, rx + 1):
                if (xx / (rx + .5)) ** 2 + (yy / (ry + .5)) ** 2 <= 1 and rnd.random() < 0.8:
                    put(cx + xx, cy + yy, PAL[color])

    cells = [(gx, gy) for gx in range(4) for gy in range(4)]
    rnd.shuffle(cells)
    for i, (gx, gy) in enumerate(cells):  # 8px 격자마다 하나씩, 칸 안에서 무작위
        cx, cy = gx * 8 + rnd.randrange(8), gy * 8 + rnd.randrange(8)
        kind = i % 4
        if kind == 0:
            blob(cx, cy, 'mid', rnd.randint(2, 3), rnd.randint(1, 2))
        elif kind == 1:
            blob(cx, cy, 'light', rnd.randint(1, 2), 1)
        elif kind == 2:
            for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
                put(cx + dx, cy + dy, PAL['dark'])
        else:
            put(cx, cy, PAL['dark']); put(cx + 1, cy, PAL['mid'])
    return t


def variant_texture(seed):
    """테두리 4px는 공통 기본판(이음매 보장), 안쪽만 칸마다 다른 시드로 바꿔 반복감을 줄인다."""
    t = base_texture(7).copy()
    inner = base_texture(seed)
    t[4:28, 4:28] = inner[4:28, 4:28]
    return t


def pebble(t, x, y, w, h):
    """그림자(오른쪽 아래) → 몸 → 하이라이트(왼쪽 위) 순서의 둥근 자갈."""
    for yy in range(h + 1):
        for xx in range(1, w + 1):
            if not ((xx in (1, w)) and (yy in (0, h))):
                t[y + yy, x + xx] = PAL['pebsh']
    for yy in range(h):
        for xx in range(w):
            if not ((xx in (0, w - 1)) and (yy in (0, h - 1)) and w > 2 and h > 2):
                t[y + yy, x + xx] = PAL['peb']
    t[y, x + 1] = PAL['pebhi']
    if w > 3:
        t[y, x + 2] = PAL['pebhi']
    if h > 2:
        t[y + 1, x] = PAL['pebhi']


SPROUT = ["..L...L..",
          ".LG...GL.",
          "..GG.GG..",
          "...GDG...",
          "....D....",
          "...sss..."]


def sprout(t, x, y):
    cmap = {'L': 'glight', 'G': 'g', 'D': 'gdark', 's': 'dark'}
    for dy, row in enumerate(SPROUT):
        for dx, ch in enumerate(row):
            if ch in cmap:
                t[y + dy, x + dx] = PAL[cmap[ch]]


def decorate(t, kind):
    t = t.copy()
    if kind == 'pebbles':
        pebble(t, 8, 11, 5, 3); pebble(t, 15, 9, 4, 3); pebble(t, 14, 16, 3, 2)
    elif kind == 'stone':
        pebble(t, 11, 12, 8, 5)
    elif kind == 'tiny':
        pebble(t, 7, 19, 3, 2); pebble(t, 20, 7, 3, 2)
    elif kind == 'sprout':
        sprout(t, 11, 10)
    elif kind == 'sprouts':
        sprout(t, 5, 6); sprout(t, 17, 17)
    elif kind == 'crack':
        for x, y in ((7, 13), (8, 13), (9, 14), (10, 14), (11, 15), (12, 15), (13, 15), (14, 16), (15, 16),
                     (16, 17), (17, 17), (12, 16), (12, 17), (11, 18), (18, 18), (19, 18), (20, 19), (21, 19)):
            t[y, x] = PAL['fcrack']
    return t


def build():
    path = f'Art_Source/tiles/tileset_{KEY}_32.png'
    sheet = np.array(Image.open(path).convert('RGBA')).astype(int)
    base = base_texture(7)
    for i, (r, k) in enumerate(FLOOR_CELLS):
        tex = decorate(variant_texture(100 + i), DECOR.get((r, k), 'plain'))
        for y in range(16):
            for x in range(16):
                if o[r * 16 + y, k * 16 + x, 3] == 0 or tuple(o[r * 16 + y, k * 16 + x, :3]) not in FLOOR_PATTERN:
                    continue
                for sy in range(2):
                    for sx in range(2):
                        py, px = 2 * y + sy, 2 * x + sx
                        sheet[r * 32 + py, k * 32 + px, :3] = tex[py, px]
                        sheet[r * 32 + py, k * 32 + px, 3] = 255
    Image.fromarray(sheet.astype(np.uint8), 'RGBA').save(path)
    return base


if __name__ == '__main__':
    build()
    print('ok', KEY)
