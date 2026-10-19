# 흡수·방출 이펙트 도트(2026-10-07) — 1px 진한 외곽선 + 색 3단(명중·속성 이펙트와 같은 규칙).
# - absorb_pellet_0/1 (7×7 / 5×5): 대상에서 플레이어로 빨려 드는 정령 빛 알갱이(흰·청록)
# - absorb_puff_0~3 (32×32): 탈이 바뀌는 순간 "퐁" 연기 4프레임(작은 덩어리 → 퍼지는 뭉게 → 흩어짐 → 점)
# - emit_star (9×9): 특성을 잃을 때 몸에서 튀어나오는 금빛 별
# - absorb_streak (10×4): 흡수 홀드 중 앞쪽에서 몸으로 빨려 드는 바람 줄기(오른쪽 끝이 머리 — 코드가 이동 방향으로 돌림)
# 출력: Assets/Resources/Fx/*.png, 미리보기 Art_Source/fx/preview_absorb_x6.png
# 사용: python -X utf8 Art_Source/fx/draw_absorb_fx.py
import math
import os
from PIL import Image

OUT = 'Assets/Resources/Fx'
O = (22, 18, 26)
SPIRIT = ((245, 255, 255), (150, 235, 250), (60, 170, 210))
CLOUD = ((255, 255, 255), (222, 230, 242), (172, 182, 204))
GOLD = ((255, 255, 225), (255, 215, 70), (210, 140, 20))


def outline(img):
    w, h = img.size
    px = img.load()
    edge = [(x, y) for y in range(h) for x in range(w) if px[x, y][3] == 0 and any(
        0 <= x + dx < w and 0 <= y + dy < h and px[x + dx, y + dy][3] and px[x + dx, y + dy][:3] != O
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for x, y in edge:
        px[x, y] = O + (255,)
    return img


def blob(px, size, cx, cy, r, pal):
    """둥근 덩어리: 왼쪽 위 밝음, 오른쪽 아래 어두움."""
    for y in range(size):
        for x in range(size):
            d2 = (x - cx) ** 2 + (y - cy) ** 2
            if d2 <= r * r:
                shade = (x - cx) * 0.6 + (y - cy) * 0.8
                c = pal[0] if shade < -r * 0.35 else (pal[2] if shade > r * 0.45 else pal[1])
                px[x, y] = c + (255,)


def pellet(size, r):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    c = (size - 1) / 2
    blob(img.load(), size, c, c, r, SPIRIT)
    return outline(img)


def puff(frame):
    n = 32
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    px = img.load()
    c = 15.5
    if frame == 0:
        blob(px, n, c, c + 1, 5, CLOUD)
    elif frame == 1:
        for k in range(5):
            a = k * 2 * math.pi / 5 + 0.3
            blob(px, n, c + math.cos(a) * 6, c + 1 + math.sin(a) * 5, 4.6, CLOUD)
        blob(px, n, c, c + 1, 4, CLOUD)
    elif frame == 2:
        for k in range(6):
            a = k * 2 * math.pi / 6 + 0.1
            blob(px, n, c + math.cos(a) * 10, c + 1 + math.sin(a) * 8, 3.4, CLOUD)
    else:
        for k in range(6):
            a = k * 2 * math.pi / 6 + 0.4
            blob(px, n, c + math.cos(a) * 12, c + 1 + math.sin(a) * 10, 1.6, CLOUD)
    return outline(img)


def star():
    rows = ["....o....",
            "...oWo...",
            "...oYo...",
            "ooooYoooo",
            "oWYYWYYGo",
            ".oGYYYGo.",
            "..oYGYo..",
            ".oYGoGYo.",
            ".ooo.ooo."]
    pal = {'W': GOLD[0], 'Y': GOLD[1], 'G': GOLD[2]}
    img = Image.new('RGBA', (9, 9), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == 'o':
                img.putpixel((x, y), O + (255,))
            elif ch in pal:
                img.putpixel((x, y), pal[ch] + (255,))
    return img


def streak():
    rows = ["..oooooo..",
            ".oBIIiiWo.",
            "..oooooWo.",
            "......oo.."]
    pal = {'W': SPIRIT[0], 'i': SPIRIT[0], 'I': SPIRIT[1], 'B': SPIRIT[2]}
    img = Image.new('RGBA', (10, 4), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == 'o':
                img.putpixel((x, y), O + (255,))
            elif ch in pal:
                img.putpixel((x, y), pal[ch] + (255,))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    out = {'absorb_pellet_0': pellet(7, 2.4), 'absorb_pellet_1': pellet(5, 1.5)}
    for i in range(4):
        out['absorb_puff_%d' % i] = puff(i)
    out['emit_star'] = star()
    out['absorb_streak'] = streak()
    for name, img in out.items():
        img.save(os.path.join(OUT, name + '.png'))
    sheet = Image.new('RGBA', (200, 36), (60, 70, 90, 255))
    x = 2
    for img in out.values():
        sheet.alpha_composite(img, (x, 2 + (32 - img.height) // 2))
        x += img.width + 3
    sheet.resize((sheet.width * 6, sheet.height * 6), Image.NEAREST).save('Art_Source/fx/preview_absorb_x6.png')
    print('ok', len(out))


if __name__ == '__main__':
    main()
