# 보스 방 바닥 봉인 원 96×96(바닥 3×3칸) 도트 — 2026-10-06.
# 참조: 바르코 GPT 3번(seal_gpt_src.png)의 구성(금 고리 2겹·룬 띠·12분할·5원소 보석·가운데 청록 별)과 색만 따르고,
# 32px 규격에 맞춰 1픽셀 단위로 새로 찍는다. 참조의 서양 별자리 그림(사자·저울 등)은 12지신 세계관과 안 맞아
# 단순한 룬 표식으로 바꿨다. 빛은 왼쪽 위, 외곽선 1px.
from PIL import Image
import math

S = 96
C = (S - 1) / 2
P = dict(
    out=(22, 18, 26), st0=(40, 40, 46), st1=(58, 58, 64), st2=(76, 76, 82),
    g0=(140, 92, 24), g1=(214, 160, 36), g2=(248, 206, 96),
    c0=(36, 120, 160), c1=(80, 206, 236), c2=(170, 240, 250),
)
GEMS = [((196, 40, 44), (255, 128, 104)),   # 화염(위)
        ((236, 196, 40), (255, 238, 140)),  # 전기
        ((150, 92, 44), (214, 152, 96)),    # 대지
        ((44, 156, 64), (140, 226, 120)),   # 독
        ((56, 164, 228), (170, 228, 250))]  # 냉기

img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
px = img.load()


def put(x, y, c):
    if 0 <= x < S and 0 <= y < S:
        px[x, y] = c + (255,)


def lit(x, y):
    """왼쪽 위 빛: -1(그늘)~1(빛)."""
    dx, dy = x - C, y - C
    r = math.hypot(dx, dy) or 1
    return (-dx - dy) / (r * 1.414)


# 기본 원판
for y in range(S):
    for x in range(S):
        r = math.hypot(x - C, y - C)
        if r > 47.6:
            continue
        L = lit(x, y)
        if r > 46.6:
            c = P['out']
        elif r > 43.6:  # 바깥 금 고리
            c = P['g2'] if (L > .45 and r > 45) else P['g0'] if (L < -.45 and r < 44.8) else P['g1']
        elif r > 42.6:
            c = P['out']
        elif r > 34.6:  # 룬 띠(돌)
            c = P['st1'] if abs(r - 38.6) < 3.2 else P['st0']
        elif r > 33.6:
            c = P['out']
        elif r > 30.6:  # 가운데 금 고리
            c = P['g2'] if (L > .45 and r > 32) else P['g0'] if (L < -.45 and r < 32) else P['g1']
        elif r > 29.6:
            c = P['out']
        elif r > 19.6:  # 12분할 띠
            c = P['st2'] if L > .55 and r > 27 else P['st1']
        elif r > 18.6:
            c = P['out']
        elif r > 16.6:  # 안쪽 금 고리
            c = P['g2'] if L > .3 else P['g0'] if L < -.3 else P['g1']
        elif r > 15.6:
            c = P['out']
        else:  # 가운데 원판
            c = P['st0']
        put(x, y, c)

# 12분할 선 + 칸마다 룬 표식
for k in range(12):
    a = math.radians(-90 + 15 + 30 * k)
    for t in range(20, 30):
        put(round(C + math.cos(a) * t), round(C + math.sin(a) * t), P['g0'])
    am = math.radians(-90 + 30 * k)
    mx, my = round(C + math.cos(am) * 25), round(C + math.sin(am) * 25)
    for dx, dy in ((0, -1), (0, 0), (0, 1), (-1, 0) if k % 2 else (1, 1)):
        put(mx + dx, my + dy, P['g1'])

# 바깥 룬 띠의 작은 각인 24개
for k in range(24):
    a = math.radians(-90 + 7.5 + 15 * k)
    mx, my = round(C + math.cos(a) * 38.6), round(C + math.sin(a) * 38.6)
    pattern = [((0, -1), (0, 0), (0, 1)), ((-1, 0), (0, 0), (1, 0), (0, 1)), ((-1, -1), (0, 0), (1, 1)),
               ((0, -1), (-1, 0), (1, 0), (0, 1))][k % 4]
    for dx, dy in pattern:
        put(mx + dx, my + dy, P['g1'])

# 5원소 보석(가운데 금 고리 위, 위에서 시계 방향 72°)
for i, (dark, light) in enumerate(GEMS):
    a = math.radians(-90 + 72 * i)
    gx, gy = C + math.cos(a) * 32.1, C + math.sin(a) * 32.1
    for y in range(int(gy) - 6, int(gy) + 7):
        for x in range(int(gx) - 6, int(gx) + 7):
            d = math.hypot(x - gx, y - gy)
            if d <= 4.6:
                put(x, y, P['out'] if d > 3.8 else P['g1'] if d > 2.9 else dark)
    put(round(gx) - 1, round(gy) - 1, light)
    put(round(gx), round(gy) - 1, light)
    put(round(gx) - 1, round(gy), light)

# 가운데 청록 별 — 96px는 중심이 47.5라 팔을 2px(47·48) 두께로 대칭으로 찍는다.
L0, L1 = 47, 48
for t in range(0, 13):  # 중심에서 바깥으로
    col = P['c2'] if t < 3 else P['c1'] if t < 9 else P['c0']
    for ys in (L0 - t, L1 + t):
        put(L0, ys, col); put(L1, ys, col)
        put(ys, L0, col); put(ys, L1, col)
    if t < 5:  # 가운데는 더 두껍게(마름모)
        w = 5 - t
        for k in range(1, w):
            for ys in (L0 - t, L1 + t):
                put(L0 - k, ys, P['c1']); put(L1 + k, ys, P['c1'])
                put(ys, L0 - k, P['c1']); put(ys, L1 + k, P['c1'])
for t in range(2, 7):  # 대각 짧은 팔
    col = P['c1'] if t < 4 else P['c0']
    put(L0 - t, L0 - t, col); put(L1 + t, L0 - t, col); put(L0 - t, L1 + t, col); put(L1 + t, L1 + t, col)
for x, y in ((L0, L0), (L1, L0), (L0, L1), (L1, L1)):
    put(x, y, P['c2'])
for x, y in ((38, 38), (57, 38), (38, 57), (57, 57)):  # 반짝이
    put(x, y, P['c1'])

img.save('Art_Source/boss/boss_seal_96.png')
print('ok')
