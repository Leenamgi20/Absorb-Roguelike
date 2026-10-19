# 무기 속성 오라 도트(2026-10-07, 사용자 결정 A — 속성에 따라 무기 색·오라).
# - weapon_sword_glow.png (13×46): 칼(weapon_sword.png 11×44) 실루엣을 1px 바깥으로 넓힌 흰 테두리. 코드가 속성 색으로 물들여 칼 뒤에 깐다.
#   칼과 같은 피벗을 쓰도록 사방 1px 여백을 둔다(피벗 y = (0.17×44 + 1) / 46).
# - weapon_sword_blade.png (11×44): 칼날 부분(코등이 위)만 흰색 — 코드가 반투명 속성 색으로 칼날에 덧칠.
# - mote_<속성>.png (5×5): 칼날에서 피어오르는 작은 조각(불씨·눈송이·스파크·거품·돌가루), 1px 외곽선.
# 출력: Assets/Resources/Fx/*.png, 미리보기 Art_Source/fx/preview_weapon_aura_x8.png
# 사용: python -X utf8 Art_Source/fx/draw_weapon_aura.py
import os
from PIL import Image

OUT = 'Assets/Resources/Fx'
SWORD = 'Assets/Art/Characters/Sword/weapon_sword.png'
O = (22, 18, 26)
GUARD_ROW = 27  # 위에서 27번째 줄부터 코등이(이 위가 칼날)


def glow():
    src = Image.open(SWORD).convert('RGBA')
    w, h = src.size
    img = Image.new('RGBA', (w + 2, h + 2), (0, 0, 0, 0))
    a = src.load()
    px = img.load()
    for y in range(h):
        for x in range(w):
            if a[x, y][3]:
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        if abs(dx) + abs(dy) <= 1:
                            px[x + 1 + dx, y + 1 + dy] = (255, 255, 255, 255)
    return img


def blade():
    src = Image.open(SWORD).convert('RGBA')
    w, h = src.size
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    a = src.load()
    for y in range(GUARD_ROW):
        for x in range(w):
            r, g, b, al = a[x, y]
            if al and (r + g + b) > 200:  # 외곽선 제외, 칼날 면만
                img.putpixel((x, y), (255, 255, 255, 255))
    return img


MOTES = {
    'fire': (["..o..", ".oYo.", "oYWYo", ".oRo.", "..o.."], {'W': (255, 245, 200), 'Y': (255, 170, 50), 'R': (220, 70, 30)}),
    'ice': (["o.o.o", ".oWo.", "oWIWo", ".oWo.", "o.o.o"], {'W': (240, 255, 255), 'I': (120, 210, 255)}),
    'electric': (["...o.", "..oWo", ".oYo.", "oWo..", ".o..."], {'W': (255, 255, 220), 'Y': (255, 230, 60)}),
    'poison': ([".ooo.", "oPpPo", "oPPPo", "oDPPo", ".ooo."], {'p': (240, 210, 255), 'P': (180, 100, 235), 'D': (110, 50, 170)}),
    'earth': ([".....", ".ooo.", "oAaAo", "oMAMo", ".ooo."], {'a': (240, 210, 160), 'A': (200, 145, 80), 'M': (130, 85, 45)}),
}


def mote(rows, pal):
    img = Image.new('RGBA', (5, 5), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == 'o':
                img.putpixel((x, y), O + (255,))
            elif ch in pal:
                img.putpixel((x, y), pal[ch] + (255,))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    out = {'weapon_sword_glow': glow(), 'weapon_sword_blade': blade()}
    for k, (rows, pal) in MOTES.items():
        out['mote_' + k] = mote(rows, pal)
    for name, img in out.items():
        img.save(os.path.join(OUT, name + '.png'))
    sheet = Image.new('RGBA', (80, 50), (60, 70, 90, 255))
    g = out['weapon_sword_glow'].copy()
    tint = Image.new('RGBA', g.size, (255, 150, 50, 255))
    g = Image.composite(tint, g, g)
    sheet.alpha_composite(g, (2, 2))
    sheet.alpha_composite(Image.open(SWORD).convert('RGBA'), (3, 3))
    x = 20
    for k in MOTES:
        sheet.alpha_composite(out['mote_' + k], (x, 20))
        x += 8
    sheet.resize((sheet.width * 8, sheet.height * 8), Image.NEAREST).save('Art_Source/fx/preview_weapon_aura_x8.png')
    print('ok', len(out))


if __name__ == '__main__':
    main()
