# 진짜 32px 벽 타일(B안, 2026-10-06). 원본 배치 기준 시트에서 벽돌 면 픽셀(원본 (110,74,72)/(120,81,79))만 다시 그린다.
# - 벽돌 하나 = 칸 안의 연결된 벽돌 픽셀 덩어리. 덩어리 위·왼쪽 1px 하이라이트, 아래·오른쪽 1px 그림자.
#   단 칸 경계에서 잘린 쪽은 선을 긋지 않는다(다른 칸과 붙었을 때 이음매에 선이 생기지 않게).
# - 안쪽은 사암 반점, 일부 벽돌에 짧은 금·호박 결정, 앞면에 뿌리·윗면에 이끼(칸 위치로 정해 매번 같은 결과).
#   같은 벽 칸이 줄지어 반복 배치되므로 장식은 드물고 작게 둔다.
# - 줄눈·외곽선·어둠 픽셀은 원본 배치 그대로. 너무 밝던 바닥 테두리 띠(원본 (76,47,73))는 사암 밝은 색으로 낮춘다.
from PIL import Image
import numpy as np
import random, sys

KEY = sys.argv[1] if len(sys.argv) > 1 else 'earth'
sys.path.insert(0, 'Art_Source/tiles')
from tile_palettes import WALL
PAL = WALL[KEY]

o = np.array(Image.open('Assets/ThirdParty/EdgarExample1/Example1/example1_tileset.png').convert('RGBA')).astype(int)
BRICK = {(110, 74, 72), (120, 81, 79)}
BAND = (76, 47, 73)
WALL_CELLS = [(0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (0, 5), (1, 0), (1, 5), (2, 0), (2, 5), (3, 0), (3, 5),
              (4, 0), (4, 1), (4, 2), (4, 3), (4, 4), (4, 5), (5, 0), (5, 1), (5, 2), (5, 3), (5, 4), (5, 5)]


def components(mask):
    h, w = mask.shape
    lab = np.zeros((h, w), int)
    n = 0
    for y in range(h):
        for x in range(w):
            if mask[y, x] and not lab[y, x]:
                n += 1
                stack = [(y, x)]
                lab[y, x] = n
                while stack:
                    cy, cx = stack.pop()
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not lab[ny, nx]:
                            lab[ny, nx] = n
                            stack.append((ny, nx))
    return lab, n


def build():
    path = f'Art_Source/tiles/tileset_{KEY}_32.png'
    sheet = np.array(Image.open(path).convert('RGBA')).astype(int)
    for (r, k) in WALL_CELLS:
        rnd = random.Random(r * 31 + k)
        orig = o[r * 16:r * 16 + 16, k * 16:k * 16 + 16]
        brick = np.array([[tuple(orig[y // 2, x // 2, :3]) in BRICK and orig[y // 2, x // 2, 3] > 0
                           for x in range(32)] for y in range(32)])
        band = np.array([[tuple(orig[y // 2, x // 2, :3]) == BAND for x in range(32)] for y in range(32)])
        cell = sheet[r * 32:r * 32 + 32, k * 32:k * 32 + 32]
        cell[band, :3] = PAL['band']
        lab, n = components(brick)
        for i in range(1, n + 1):
            ys, xs = np.where(lab == i)
            if len(ys) < 6:
                cell[ys, xs, :3] = PAL['base']
                continue
            cell[ys, xs, :3] = PAL['base']
            for y, x in zip(ys, xs):
                up = y - 1 >= 0 and lab[y - 1, x] == i
                left = x - 1 >= 0 and lab[y, x - 1] == i
                down = y + 1 < 32 and lab[y + 1, x] == i
                right = x + 1 < 32 and lab[y, x + 1] == i
                if (not up and y > 0) or (not left and x > 0):
                    cell[y, x, :3] = PAL['hi']
                if (not down and y < 31) or (not right and x < 31):
                    cell[y, x, :3] = PAL['sh']
            inner = [(y, x) for y, x in zip(ys, xs)
                     if 1 <= y < 31 and 1 <= x < 31 and all(lab[y + dy, x + dx] == i for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
            for _ in range(max(1, len(inner) // 18)):
                if inner:
                    y, x = rnd.choice(inner)
                    cell[y, x, :3] = PAL[rnd.choice(['speck', 'speck', 'speck2'])]
            deco = rnd.random()
            if inner and len(inner) > 30:
                y, x = rnd.choice(inner)
                if deco < 0.06:  # 금(같은 칸이 줄지어 반복되므로 짧고 드물게)
                    for j in range(2):
                        yy, xx = y + j // 2, x + j
                        if (yy, xx) in inner:
                            cell[yy, xx, :3] = PAL['crack']
                elif deco < 0.16:  # 호박 결정
                    for dy, dx, c in ((0, 1, 'amberhi'), (1, 0, 'amber'), (1, 1, 'amber'), (1, 2, 'ambersh'), (2, 1, 'ambersh')):
                        if (y + dy, x + dx) in inner:
                            cell[y + dy, x + dx, :3] = PAL[c]
        # 벽 앞면(아래쪽 칸)에 늘어진 뿌리, 윗면 칸에 이끼
        if False and r in (4, 5):  # 뿌리: 같은 칸 반복 때문에 금처럼 보여 끔(2026-10-06)
            x = rnd.randint(6, 25)
            length = rnd.randint(5, 10)
            for j in range(length):
                yy = 2 + j
                xx = x + (1 if (j // 3) % 2 else 0)
                if brick[yy, xx]:
                    cell[yy, xx, :3] = PAL['root']
                    if xx + 1 < 32 and brick[yy, xx + 1] and j % 2:
                        cell[yy, xx + 1, :3] = PAL['rootlt']
        if r == 0 and rnd.random() < 0.6:
            x = rnd.randint(4, 24)
            for j in range(rnd.randint(3, 6)):
                for yy in range(32):
                    if brick[yy, x + j]:
                        cell[yy, x + j, :3] = PAL['moss'] if j % 2 else PAL['mosslt']
                        break
        sheet[r * 32:r * 32 + 32, k * 32:k * 32 + 32] = cell
    Image.fromarray(sheet.astype(np.uint8), 'RGBA').save(path)


if __name__ == '__main__':
    build()
    print('ok', KEY)
