# 층 출구 포탈 도트(2026-10-07) — 48×64(1.5×2칸), 똑바로 선 타원형 돌 고리 + 안쪽 소용돌이 4프레임.
# - portal_ring: 봉인 석실 돌(청회색) 고리, 1px 외곽선, 금 룬 점 8개 + 꼭대기 청록 보석(UI 키트·보스 방 장식과 같은 색)
# - portal_vortex_normal_0~3: 보라·청록 소용돌이(일반 층 — 다음 층으로)
# - portal_vortex_final_0~3: 금·흰 소용돌이(마지막 층 — 봉인 밖으로, 승리)
# 고리는 소용돌이 위에 그린다(코드가 순서를 둔다). 잠김(준비 안 됨)은 코드가 소용돌이를 어둡게 한다.
# 출력: Assets/Resources/Fx/portal_*.png, 미리보기 Art_Source/fx/preview_portal_x5.png
# 사용: python -X utf8 Art_Source/fx/draw_portal.py
import math
import os
from PIL import Image

OUT = 'Assets/Resources/Fx'
W, H = 48, 64
CX, CY = 23.5, 33.5
O = (22, 18, 26)
STONE = [(110, 132, 158), (78, 98, 124), (52, 68, 92), (34, 46, 66)]
GOLD = [(252, 222, 120), (226, 172, 48), (150, 100, 24)]
TEAL = [(178, 242, 252), (80, 206, 236)]
VORTEX = {
    'normal': [(250, 240, 255), (200, 160, 255), (150, 100, 235), (90, 60, 190), (60, 150, 200), (40, 40, 110)],
    'final': [(255, 255, 245), (255, 240, 170), (255, 215, 90), (230, 165, 40), (255, 250, 220), (170, 110, 30)],
}
IN_RX, IN_RY = 15.5, 23.5   # 소용돌이(고리 안쪽)
OUT_RX, OUT_RY = 21.5, 29.5  # 고리 바깥


def e(x, y, rx, ry):
    return ((x - CX) / rx) ** 2 + ((y - CY) / ry) ** 2


def ring():
    img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    px = img.load()
    for y in range(H):
        for x in range(W):
            inner = e(x, y, IN_RX + 0.5, IN_RY + 0.5)
            outer = e(x, y, OUT_RX, OUT_RY)
            if inner >= 1 and outer <= 1:
                # 왼쪽 위에서 빛: 바깥쪽 법선 방향으로 밝기
                nx, ny = (x - CX) / OUT_RX, (y - CY) / OUT_RY
                light = -(nx * 0.6 + ny * 0.8)
                t = (math.sqrt(inner) - 1) / (math.sqrt(1 / max(outer, 1e-6)) * math.sqrt(inner) - 1 + 1e-6)
                k = 0 if light > 0.45 else 1 if light > -0.1 else 2 if light > -0.55 else 3
                if 0.35 < t < 0.6:
                    k = min(3, k + 1)  # 고리 가운데 홈
                px[x, y] = STONE[k] + (255,)
    # 금 룬 점 8개(고리 중앙선)
    mid_rx, mid_ry = (IN_RX + OUT_RX) / 2 + 0.3, (IN_RY + OUT_RY) / 2 + 0.3
    for k in range(8):
        a = k * math.pi / 4 + math.pi / 8
        x, y = int(round(CX + math.cos(a) * mid_rx)), int(round(CY + math.sin(a) * mid_ry))
        px[x, y] = GOLD[0] + (255,)
        if 0 <= x + 1 < W:
            px[x + 1, y] = GOLD[1] + (255,)
        if 0 <= y + 1 < H:
            px[x, y + 1] = GOLD[2] + (255,)
    # 꼭대기 보석(다이아 5×5)
    gx, gy = int(CX), int(CY - mid_ry)
    for dy, row in enumerate(["..o..", ".oTo.", "oTtGo", ".oGo.", "..o.."]):
        for dx, ch in enumerate(row):
            c = {'o': O, 'T': TEAL[0], 't': TEAL[1], 'G': GOLD[1]}.get(ch)
            if c:
                px[gx - 2 + dx, gy - 2 + dy] = c + (255,)
    # 1px 외곽선(바깥·안쪽 모두)
    edge = [(x, y) for y in range(H) for x in range(W) if px[x, y][3] == 0 and any(
        0 <= x + dx < W and 0 <= y + dy < H and px[x + dx, y + dy][3] and px[x + dx, y + dy][:3] != O
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for x, y in edge:
        px[x, y] = O + (255,)
    return img


def vortex(pal, frame):
    img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    px = img.load()
    phase = frame * math.pi / 2
    for y in range(H):
        for x in range(W):
            d = e(x, y, IN_RX + 1, IN_RY + 1)
            if d > 1:
                continue
            r = math.sqrt(d)
            th = math.atan2((y - CY) / IN_RY, (x - CX) / IN_RX)
            v = math.sin(3 * th + 7 * r - phase)
            if r < 0.22:
                c = pal[0]
            elif v > 0.55:
                c = pal[1] if r < 0.6 else pal[2]
            elif v > -0.1:
                c = pal[2] if r < 0.7 else pal[3]
            else:
                c = pal[3] if r < 0.5 else pal[5]
            if (x * 7 + y * 13 + frame * 5) % 53 == 0 and r > 0.3:
                c = pal[4]  # 반짝이는 별 점
            px[x, y] = c + (255,)
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    out = {'portal_ring': ring()}
    for name, pal in VORTEX.items():
        for i in range(4):
            out['portal_vortex_%s_%d' % (name, i)] = vortex(pal, i)
    for name, img in out.items():
        img.save(os.path.join(OUT, name + '.png'))
    sheet = Image.new('RGBA', ((W + 4) * 4, (H + 4) * 2), (60, 70, 90, 255))
    for row, name in enumerate(('normal', 'final')):
        for i in range(4):
            frame = out['portal_vortex_%s_%d' % (name, i)].copy()
            frame.alpha_composite(out['portal_ring'])
            sheet.alpha_composite(frame, (i * (W + 4), row * (H + 4)))
    sheet.resize((sheet.width * 5, sheet.height * 5), Image.NEAREST).save('Art_Source/fx/preview_portal_x5.png')
    print('ok', len(out))


if __name__ == '__main__':
    main()
