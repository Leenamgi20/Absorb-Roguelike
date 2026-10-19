# 속성 상태 이펙트 도트(2026-10-07) — 1px 진한 외곽선 + 색 3단(명중 스파크와 같은 규칙). 몸 위에 겹쳐 반복 재생하는 작은 조각들.
# - 화상 flame_0~2 (8×12): 일렁이는 불꽃 3프레임
# - 독 bubble_0~3 (8×12): 거품이 올라가다 터짐 4프레임
# - 냉기 frost_1~3 (32×12): 발밑 얼음 결정, 스택 1~3단계(클수록 많고 높음)
# - 감전 zap_0~2 (32×32): 몸 주위를 감는 지그재그 전기 3프레임(무작위로 깜빡)
# - 경직·마비 star_earth / star_electric (7×7): 머리 위를 도는 별(코드가 타원으로 돌림)
# 출력: Assets/Resources/Fx/status_*.png, 미리보기 Art_Source/fx/preview_status_x6.png
# 사용: python -X utf8 Art_Source/fx/draw_status_fx.py
import os
import random
from PIL import Image

OUT = 'Assets/Resources/Fx'
O = (22, 18, 26)
PAL = {
    'W': (255, 250, 220), 'Y': (255, 200, 60), 'R': (225, 80, 30),         # 불
    'p': (245, 215, 255), 'P': (190, 110, 240), 'D': (110, 50, 170),       # 독
    'i': (240, 255, 255), 'I': (140, 220, 255), 'B': (60, 140, 220),       # 얼음
    'e': (255, 255, 225), 'E': (255, 235, 60), 'G': (215, 150, 20),        # 전기
    'a': (255, 235, 190), 'A': (215, 160, 80), 'M': (140, 90, 45),         # 대지
}


def from_rows(rows):
    h, w = len(rows), len(rows[0])
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == 'o':
                img.putpixel((x, y), O + (255,))
            elif ch in PAL:
                img.putpixel((x, y), PAL[ch] + (255,))
    return img


def outline(img):
    w, h = img.size
    px = img.load()
    fill = [(x, y) for y in range(h) for x in range(w) if px[x, y][3] == 0 and any(
        0 <= x + dx < w and 0 <= y + dy < h and px[x + dx, y + dy][3] and px[x + dx, y + dy][:3] != O
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for x, y in fill:
        px[x, y] = O + (255,)
    return img


FLAMES = [
    ["...o....",
     "..oWo...",
     "..oYo...",
     ".oYWYo..",
     ".oYWYo..",
     "oRYWWYo.",
     "oRYWWYo.",
     "oRYWWYRo",
     "oRRYYRRo",
     ".oRRRRo.",
     "..oooo..",
     "........"],
    ["....o...",
     "...oWo..",
     "..oYYo..",
     "..oYWYo.",
     ".oYWWYo.",
     ".oYWWYRo",
     "oRYWWYRo",
     "oRYWWYRo",
     "oRRYYRRo",
     ".oRRRRo.",
     "..oooo..",
     "........"],
    ["........",
     "..o.....",
     ".oWo.o..",
     ".oYooWo.",
     ".oYWYYo.",
     "oRYWWYo.",
     "oRYWWYRo",
     "oRYWWYRo",
     "oRRYYRRo",
     ".oRRRRo.",
     "..oooo..",
     "........"],
]

BUBBLES = [
    ["........", "........", "........", "........", "........", "........", "........",
     "...ooo..", "..oPpPo.", "..oPPDo.", "...ooo..", "........"],
    ["........", "........", "........", "...ooo..", "..oPpPo.", ".oPpPPPo", ".oPPPPDo", "..oPPDo.", "...ooo..",
     "........", "........", "........"],
    ["..ooo...", ".oPpPo..", "oPpPPPo.", "oPPPPDo.", ".oPPDo..", "..ooo...", "........", "........", "........",
     "........", "........", "........"],
    [".o...o..", "oPo.oPo.", ".o...o..", "...o....", "..oPo...", "...o....", "........", "........", "........",
     "........", "........", "........"],
]

STAR = {
    'earth': [
        "...o...",
        "..oao..",
        "ooaAaoo",
        "oaAAAMo",
        ".oAMMo.",
        "oAMoMMo",
        "oo...oo"],
    'electric': [
        "...o...",
        "..oeo..",
        "ooeEeoo",
        "oeEEEGo",
        ".oEGGo.",
        "oEGoGGo",
        "oo...oo"],
}


def frost(level):
    w, h = 32, 12
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    px = img.load()
    rnd = random.Random(level * 7)
    count = {1: 3, 2: 5, 3: 7}[level]
    maxh = {1: 7, 2: 8, 3: 10}[level]
    xs = sorted(rnd.sample(range(3, w - 3, 2), count))
    for x0 in xs:
        hh = rnd.randint(maxh - 3, maxh)
        for dy in range(hh):
            y = h - 2 - dy
            half = 1 if dy < hh - 2 else 0
            for dx in range(-half, half + 1):
                c = PAL['i'] if dx == -half and dy > 1 else (PAL['I'] if dy < hh - 1 else PAL['i'])
                if dx == half and half:
                    c = PAL['B']
                px[x0 + dx, y] = c + (255,)
    for x in range(2, w - 2):  # 바닥 서리 띠
        if px[x, h - 2][3] == 0 and rnd.random() < 0.55 + 0.15 * level:
            px[x, h - 2] = PAL['I'] + (255,)
    return outline(img)


def line(px, x0, y0, x1, y1, c):
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err = dx + dy
    while True:
        px[x0, y0] = c
        if x0 == x1 and y0 == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


def zap(seed):
    # 지그재그 번개 줄기 2개: 4~6px마다 좌우로 꺾이는 꺾은선(1px 흰 심) + 오른쪽 노란 빛 + 외곽선
    w = h = 32
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    px = img.load()
    rnd = random.Random(seed)
    for k in range(2):
        x, y = rnd.randint(6, 25), rnd.randint(2, 5)
        d = rnd.choice((-1, 1))
        pts = [(x, y)]
        while y < h - 6:
            y += rnd.randint(4, 6)
            x = max(3, min(w - 4, x + d * rnd.randint(3, 5)))
            d = -d
            pts.append((x, y))
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            line(px, x0 + 1, y0, x1 + 1, y1, PAL['E'] + (255,))
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            line(px, x0, y0, x1, y1, PAL['e'] + (255,))
    return outline(img)


def main():
    os.makedirs(OUT, exist_ok=True)
    out = {}
    for i, rows in enumerate(FLAMES):
        out['status_flame_%d' % i] = from_rows(rows)
    for i, rows in enumerate(BUBBLES):
        out['status_bubble_%d' % i] = from_rows(rows)
    for lv in (1, 2, 3):
        out['status_frost_%d' % lv] = frost(lv)
    for i in range(3):
        out['status_zap_%d' % i] = zap(11 + i * 5)
    for k, rows in STAR.items():
        out['status_star_%s' % k] = from_rows(rows)
    for name, img in out.items():
        img.save(os.path.join(OUT, name + '.png'))
    # 미리보기
    sheet = Image.new('RGBA', (300, 60), (60, 70, 90, 255))
    x = 2
    for name, img in out.items():
        if x + img.width > 300:
            break
        sheet.alpha_composite(img, (x, 2 + (32 - img.height)))
        x += img.width + 3
    sheet.resize((sheet.width * 6, sheet.height * 6), Image.NEAREST).save('Art_Source/fx/preview_status_x6.png')
    print('ok', len(out))


if __name__ == '__main__':
    main()
