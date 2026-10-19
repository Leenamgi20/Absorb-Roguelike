# 도트 UI 키트(2026-10-06) — 테마 "봉인 석실": 청회색 돌 + 금 테 + 청록 룬(타이틀 배경·보스 방 장식과 같은 색).
# 모든 조각은 1픽셀 = 아트 1픽셀로 직접 찍는다(AI 변환 없이 격자 정확). Unity에서는 PPU 50 + 9-slice 테두리로 써서
# 1280×720 기준 캔버스에서 아트 1픽셀 = UI 2단위(게임 화면 640×360과 같은 픽셀 크기)가 된다.
# 출력: Assets/Art/UI/Kit/*.png, 테두리 값은 BORDERS(왼, 아래, 오른, 위 — Unity spriteBorder 순서)로 함께 출력.
# 사용: python -X utf8 Art_Source/ui/draw_ui_kit.py
import json
import os
from PIL import Image

OUT = 'Assets/Art/UI/Kit'
P = {
    'o': (22, 18, 26),
    's0': (14, 20, 32), 's1': (24, 34, 50), 's2': (34, 48, 68), 's3': (50, 68, 94), 's4': (74, 96, 126),
    'g0': (124, 82, 20), 'g1': (184, 128, 30), 'g2': (226, 172, 48), 'g3': (252, 222, 120),
    't0': (22, 84, 116), 't1': (36, 130, 172), 't2': (80, 206, 236), 't3': (178, 242, 252),
    'd0': (40, 42, 50), 'd1': (62, 64, 74), 'd2': (88, 90, 102), 'd3': (118, 120, 132),
    'w': (255, 255, 255), 'w1': (205, 205, 210),
}
BORDERS = {}


def canvas(w, h):
    return Image.new('RGBA', (w, h), (0, 0, 0, 0))


def put(img, x, y, key, a=255):
    img.putpixel((x, y), P[key] + (a,))


def rect(img, x0, y0, x1, y1, key, a=255):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(img, x, y, key, a)


def frame(img, x0, y0, x1, y1, key):
    for x in range(x0, x1 + 1):
        put(img, x, y0, key)
        put(img, x, y1, key)
    for y in range(y0, y1 + 1):
        put(img, x0, y, key)
        put(img, x1, y, key)


def save(img, name, border):
    img.save(os.path.join(OUT, name + '.png'))
    BORDERS[name] = border


def bevel_frame(img, x0, y0, x1, y1, hi, mid, lo):
    """1px 금 테: 위·왼쪽 밝게, 아래·오른쪽 어둡게."""
    for x in range(x0, x1 + 1):
        put(img, x, y0, hi)
        put(img, x, y1, lo)
    for y in range(y0 + 1, y1):
        put(img, x0, y, hi)
        put(img, x1, y, lo)
    put(img, x1, y0, mid)
    put(img, x0, y1, mid)


def corner_gem(img, cx, cy):
    """모서리 금 장식 5×5(다이아) + 가운데 청록 룬 보석."""
    shape = ["..o..", ".ogo.", "ogtgo", ".ogo.", "..o.."]
    for dy, row in enumerate(shape):
        for dx, ch in enumerate(row):
            if ch == '.':
                continue
            key = {'o': 'o', 'g': 'g2', 't': 't2'}[ch]
            put(img, cx - 2 + dx, cy - 2 + dy, key)
    put(img, cx - 1, cy - 1, 'g3')


def panel(name, fill='s1', alpha=246, gems=True):
    # 32×32, 테두리 10: 외곽선 → 금 테(입체) → 외곽선 → 돌 안쪽(윗줄 밝게, 아랫줄 어둡게)
    W = H = 32
    img = canvas(W, H)
    frame(img, 0, 0, W - 1, H - 1, 'o')
    bevel_frame(img, 1, 1, W - 2, H - 2, 'g3', 'g2', 'g0')
    frame(img, 2, 2, W - 3, H - 3, 'g1')
    frame(img, 3, 3, W - 4, H - 4, 'o')
    rect(img, 4, 4, W - 5, H - 5, fill, alpha)
    for x in range(4, W - 4):
        put(img, x, 4, 's3', alpha)
        put(img, x, H - 5, 's0', alpha)
    if gems:
        for cx, cy in ((3, 3), (W - 4, 3), (3, H - 4), (W - 4, H - 4)):
            corner_gem(img, cx, cy)
    save(img, name, (10, 10, 10, 10))


def inset_box():
    # 16×16, 테두리 4: 패널 안 하위 칸(파인 홈). 위·왼쪽 안쪽 그늘, 아래·오른쪽 밝은 테.
    W = H = 16
    img = canvas(W, H)
    frame(img, 0, 0, W - 1, H - 1, 'o')
    rect(img, 1, 1, W - 2, H - 2, 's0', 235)
    for x in range(1, W - 1):
        put(img, x, 1, 'o', 200)
        put(img, x, H - 2, 's2')
    for y in range(1, H - 1):
        put(img, 1, y, 'o', 200)
        put(img, W - 2, y, 's2')
    save(img, 'box', (4, 4, 4, 4))


def button(name, face, face_hi, face_lo, rim_hi, rim, rim_lo, pressed=False, glow=False):
    # 32×18, 테두리 7: 외곽선 + 금 테 + 돌 버튼면(윗줄 하이라이트, 아래 2줄 두께). 눌림은 두께 없이 1px 내려앉음.
    W, H = 32, 18
    img = canvas(W, H)
    top = 1 if pressed else 0
    frame(img, 0, top, W - 1, H - 1, 'o')
    for x in (0, W - 1):
        for y in (top, H - 1):
            img.putpixel((x, y), (0, 0, 0, 0))   # 둥근 모서리
    bevel_frame(img, 1, top + 1, W - 2, H - 2, rim_hi, rim, rim_lo)
    depth = 0 if pressed else 2
    rect(img, 2, top + 2, W - 3, H - 3, face)
    for x in range(2, W - 2):
        put(img, x, top + 2, face_hi)
        for d in range(depth):
            put(img, x, H - 3 - d, face_lo)
    if glow:
        for x in range(3, W - 3):
            put(img, x, top + 3, 't1')
        put(img, 2, top + 2, 't3')
    save(img, name, (7, 7, 7, 7))


def slider_parts():
    # 트랙 16×8(테두리 3) — 파인 홈, 채움 8×4(테두리 2) — 청록 빛, 손잡이 10×16 — 금 쐐기
    img = canvas(16, 8)
    frame(img, 0, 0, 15, 7, 'o')
    rect(img, 1, 1, 14, 6, 's0')
    for x in range(1, 15):
        put(img, x, 1, 'o')
        put(img, x, 6, 's2')
    save(img, 'slider_track', (3, 3, 3, 3))

    img = canvas(8, 4)
    rect(img, 0, 0, 7, 3, 't1')
    for x in range(8):
        put(img, x, 0, 't3')
        put(img, x, 1, 't2')
        put(img, x, 3, 't0')
    save(img, 'slider_fill', (2, 2, 2, 2))

    rows = ["..oooooo..",
            ".oAAAABBo.",
            "oAABBBBBCo",
            "oABBBBBBCo",
            "oABBTTBBCo",
            "oABBTTBBCo",
            "oABBTTBBCo",
            "oABBTTBBCo",
            "oABBTTBBCo",
            "oABBTTBBCo",
            "oABBBBBBCo",
            "oBBBBBBBCo",
            ".oCCCCCDo.",
            "..oDDDDo..",
            "...oooo...",
            ".........."]
    img = canvas(10, 16)
    keys = {'o': 'o', 'A': 'g3', 'B': 'g2', 'C': 'g1', 'D': 'g0', 'T': 't2'}
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch in keys:
                put(img, x, y, keys[ch])
    save(img, 'slider_handle', (0, 0, 0, 0))


def toggle_parts():
    img = canvas(14, 14)
    frame(img, 0, 0, 13, 13, 'o')
    bevel_frame(img, 1, 1, 12, 12, 'g3', 'g2', 'g0')
    rect(img, 2, 2, 11, 11, 's0')
    for x in range(2, 12):
        put(img, x, 2, 'o', 200)
    save(img, 'toggle_box', (0, 0, 0, 0))

    rows = ["..............",
            "..............",
            "..............",
            "..........oo..",
            ".........oTo..",
            "........oTto..",
            "..oo...oTto...",
            "..oTo.oTto....",
            "...oToTto.....",
            "....oTto......",
            ".....oo.......",
            "..............",
            "..............",
            ".............."]
    img = canvas(14, 14)
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == 'o':
                put(img, x, y, 'o')
            elif ch == 'T':
                put(img, x, y, 't3')
            elif ch == 't':
                put(img, x, y, 't2')
    save(img, 'toggle_check', (0, 0, 0, 0))


def bar_parts():
    # 체력바 틀 16×10(테두리 4): 외곽선 + 금 테 + 어두운 홈. 채움 4×6(테두리 1): 흰색(색은 코드 틴트) + 윗줄 하이라이트·아랫줄 그늘
    img = canvas(16, 10)
    frame(img, 0, 0, 15, 9, 'o')
    bevel_frame(img, 1, 1, 14, 8, 'g3', 'g2', 'g0')
    rect(img, 2, 2, 13, 7, 's0')
    for x in range(2, 14):
        put(img, x, 2, 'o')
    save(img, 'bar_frame', (4, 4, 4, 4))

    img = canvas(4, 6)
    rect(img, 0, 0, 3, 5, 'w')
    for x in range(4):
        put(img, x, 0, 'w')
        put(img, x, 1, 'w')
        put(img, x, 4, 'w1')
        put(img, x, 5, 'w1')
    for x in range(4):
        img.putpixel((x, 0), (255, 255, 255, 255))
    # 윗줄을 살짝 밝게 보이도록 2·3줄을 한 단계 낮춘다(틴트 후에도 입체감)
    for x in range(4):
        img.putpixel((x, 2), (232, 232, 236, 255))
        img.putpixel((x, 3), (232, 232, 236, 255))
    save(img, 'bar_fill', (1, 1, 1, 1))


def divider():
    # 24×5(좌우 테두리 6): 금 선 + 양 끝 마름모
    img = canvas(24, 5)
    for x in range(3, 21):
        put(img, x, 1, 'o')
        put(img, x, 2, 'g2')
        put(img, x, 3, 'o')
    for cx in (2, 21):
        for dx, dy, k in ((0, 0, 'o'), (-1, 1, 'o'), (1, 1, 'o'), (0, 1, 't2'), (-2, 2, 'o'), (2, 2, 'o'),
                          (-1, 2, 'g2'), (1, 2, 'g2'), (0, 2, 't3'), (-1, 3, 'o'), (1, 3, 'o'), (0, 3, 't1'), (0, 4, 'o')):
            if 0 <= cx + dx < 24:
                put(img, cx + dx, dy, k)
    save(img, 'divider', (6, 0, 6, 0))


def main():
    os.makedirs(OUT, exist_ok=True)
    panel('panel')
    panel('panel_plain', gems=False)
    inset_box()
    button('button', 's2', 's4', 's0', 'g3', 'g2', 'g0')
    button('button_hover', 's3', 't3', 's1', 'g3', 'g3', 'g1', glow=True)
    button('button_pressed', 's1', 's2', 's0', 'g2', 'g1', 'g0', pressed=True)
    button('button_disabled', 'd1', 'd2', 'd0', 'd3', 'd2', 'd0')
    slider_parts()
    toggle_parts()
    bar_parts()
    divider()
    with open(os.path.join(OUT, 'borders.json'), 'w', encoding='utf-8') as f:
        json.dump(BORDERS, f, ensure_ascii=False, indent=1)
    # 미리보기(×6)
    names = list(BORDERS)
    sheet = Image.new('RGBA', (sum(Image.open(os.path.join(OUT, n + '.png')).width + 4 for n in names), 34), (40, 40, 48, 255))
    x = 0
    for n in names:
        im = Image.open(os.path.join(OUT, n + '.png'))
        sheet.alpha_composite(im, (x, 0))
        x += im.width + 4
    sheet.resize((sheet.width * 6, sheet.height * 6), Image.NEAREST).save('Art_Source/ui/preview_kit_x6.png')
    print(BORDERS)


if __name__ == '__main__':
    main()
