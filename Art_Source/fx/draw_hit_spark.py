# 명중 스파크 도트(2026-10-07) — 32×32 칸 4프레임, 1px 진한 외곽선 + 색 3단(밝음·중간·어두움). 기본(흰·연노랑) +
# 속성 5색 + 금색(귀 채찍 끝부분 강타) 7세트. 모양은 같고 색만 다르다(코드가 세트 이름으로 고름).
# 프레임: 0 작은 섬광(+자 4줄기) → 1 큰 별(굵은 4줄기 + 짧은 대각선) → 2 깨진 고리 + 흩어지는 조각 → 3 작은 잔불 점.
# 출력: Assets/Resources/Fx/hit_<세트>_<0~3>.png (+ 미리보기 Art_Source/fx/preview_hit_x6.png)
# 사용: python -X utf8 Art_Source/fx/draw_hit_spark.py
import math
import os
from PIL import Image

OUT = 'Assets/Resources/Fx'
O = (22, 18, 26)
SETS = {
    # 이름: (가장 밝음, 중간, 어두움)
    'base': ((255, 255, 255), (255, 240, 170), (240, 190, 90)),
    'fire': ((255, 240, 180), (255, 150, 50), (210, 70, 30)),
    'ice': ((240, 255, 255), (140, 220, 255), (60, 140, 220)),
    'electric': ((255, 255, 220), (255, 235, 60), (220, 160, 20)),
    'poison': ((245, 215, 255), (190, 110, 240), (110, 50, 170)),
    'earth': ((255, 235, 190), (210, 150, 80), (140, 90, 45)),
    'gold': ((255, 255, 230), (255, 215, 70), (210, 140, 20)),
}
N = 32
C = 15.5  # 가운데(16칸 사이)


def blank():
    return [[None] * N for _ in range(N)]


def put(g, x, y, v):
    if 0 <= x < N and 0 <= y < N:
        if v == 0 or g[y][x] is None or g[y][x] > v:
            g[y][x] = v  # 0 = 가장 밝음이 우선


def ray(g, ang, r0, r1, width_core=0):
    for i in range(int(r0 * 2), int(r1 * 2) + 1):
        r = i / 2
        x = C + math.cos(ang) * r
        y = C + math.sin(ang) * r
        t = (r - r0) / max(0.01, r1 - r0)
        put(g, int(round(x)), int(round(y)), 0 if t < 0.45 else (1 if t < 0.8 else 2))


def disc(g, rad, v):
    for y in range(N):
        for x in range(N):
            if (x - C) ** 2 + (y - C) ** 2 <= rad * rad:
                put(g, x, y, v)


def ring(g, rad, v, gaps=None):
    for k in range(int(rad * 8)):
        a = 2 * math.pi * k / int(rad * 8)
        if gaps and any(abs((a - ga + math.pi) % (2 * math.pi) - math.pi) < gw for ga, gw in gaps):
            continue
        put(g, int(round(C + math.cos(a) * rad)), int(round(C + math.sin(a) * rad)), v)


def frames():
    fs = []
    g = blank()  # 0: 작은 섬광
    disc(g, 2.2, 0)
    for k in range(4):
        ray(g, k * math.pi / 2, 2, 6)
    fs.append(g)
    g = blank()  # 1: 큰 별(굵은 4줄기 + 짧은 대각선) — 고리는 조준선처럼 보여 넣지 않음
    disc(g, 3.4, 0)
    for k in range(4):
        a = k * math.pi / 2
        ray(g, a, 3, 12)
        for off in (-1, 1):  # 안쪽 절반은 3px 굵기
            ox, oy = -math.sin(a) * off, math.cos(a) * off
            for i in range(6, 15):
                r = i / 2
                put(g, int(round(C + math.cos(a) * r + ox)), int(round(C + math.sin(a) * r + oy)), 0 if r < 5 else 1)
    for k in range(4):
        ray(g, math.pi / 4 + k * math.pi / 2, 3, 6)
    fs.append(g)
    g = blank()  # 2: 깨진 고리 + 조각
    ring(g, 11, 1, gaps=[(0.4, 0.35), (2.0, 0.3), (3.6, 0.4), (5.2, 0.3)])
    for k in range(8):
        a = k * math.pi / 4 + 0.2
        for d in (0, 1):
            put(g, int(round(C + math.cos(a) * (6 + d))), int(round(C + math.sin(a) * (6 + d))), 0 if d == 0 else 1)
    disc(g, 1.2, 1)
    fs.append(g)
    g = blank()  # 3: 잔불 점
    for k in range(6):
        a = k * math.pi / 3 + 0.5
        put(g, int(round(C + math.cos(a) * 13)), int(round(C + math.sin(a) * 13)), 2)
    for k in range(4):
        a = k * math.pi / 2 + 0.9
        put(g, int(round(C + math.cos(a) * 9)), int(round(C + math.sin(a) * 9)), 1)
    fs.append(g)
    return fs


def render(g, pal):
    img = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    for y in range(N):
        for x in range(N):
            if g[y][x] is not None:
                img.putpixel((x, y), pal[g[y][x]] + (255,))
    # 1px 외곽선(바깥쪽만, 대각선 포함하지 않음)
    for y in range(N):
        for x in range(N):
            if g[y][x] is None and any(0 <= x + dx < N and 0 <= y + dy < N and g[y + dy][x + dx] is not None
                                       for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                img.putpixel((x, y), O + (255,))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    fs = frames()
    sheet = Image.new('RGBA', (N * 4 + 12, (N + 4) * len(SETS)), (60, 70, 90, 255))
    for row, (name, pal) in enumerate(SETS.items()):
        for i, g in enumerate(fs):
            img = render(g, pal)
            img.save(os.path.join(OUT, 'hit_%s_%d.png' % (name, i)))
            sheet.alpha_composite(img, (i * (N + 4), row * (N + 4)))
    sheet.resize((sheet.width * 6, sheet.height * 6), Image.NEAREST).save('Art_Source/fx/preview_hit_x6.png')
    print('ok', len(SETS) * 4)


if __name__ == '__main__':
    main()
