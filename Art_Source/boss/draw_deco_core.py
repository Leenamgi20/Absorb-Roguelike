# 보스 방 기본 구성 장식(벽 금 장식 4종 + 청록 화로 2프레임)을 32×32로 직접 찍는다(2026-10-06).
# make_deco32.py(참조 축소판)의 같은 칸을 이 결과로 덮어쓴다. 색은 봉인 원(draw_seal96.py)과 같은 팔레트.
from PIL import Image
import numpy as np

P = dict(o=(22, 18, 26), a=(40, 40, 46), b=(58, 58, 64), c=(82, 82, 90),
         G=(140, 92, 24), g=(214, 160, 36), y=(248, 206, 96),
         C=(36, 120, 160), k=(80, 206, 236), w=(170, 240, 250))


def blank():
    return np.zeros((32, 32, 4), np.uint8)


def put(t, x, y, ch):
    if 0 <= x < 32 and 0 <= y < 32:
        t[y, x] = (*P[ch], 255)


def rect(t, x0, y0, x1, y1, ch):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(t, x, y, ch)


def outline(t, open_lr=False):
    al = t[:, :, 3] > 0
    for y in range(32):
        for x in range(32):
            if not al[y, x]:
                continue
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if open_lr and not (0 <= nx < 32):
                    continue
                if not (0 <= ny < 32 and 0 <= nx < 32) or not al[ny, nx]:
                    t[y, x] = (*P['o'], 255)
                    break


def key_pattern(t, x0, x1, y0):
    """돌 띠 안의 금 번개무늬(4px 주기, 칸 경계에서 이어지도록 x 절대좌표 기준)."""
    for x in range(x0, x1 + 1):
        m = x % 8
        if m in (0, 1, 2, 3):
            put(t, x, y0, 'G')
        if m == 3:
            put(t, x, y0 + 1, 'G'); put(t, x, y0 + 2, 'G')
        if m in (3, 4, 5, 6, 7):
            put(t, x, y0 + 2, 'G') if m != 3 else None
        if m == 7:
            put(t, x, y0 + 1, 'G'); put(t, x, y0, 'G') if False else None


def trim(kind, crest=True):
    """벽 윗면에 얹는 금 장식 띠(칸 위쪽 13px). kind: left/mid/right, crest=False면 가운데 볏 없이."""
    t = blank()
    rect(t, 0, 3, 31, 4, 'y')       # 위 금테
    rect(t, 0, 5, 31, 5, 'g')
    rect(t, 0, 6, 31, 10, 'b')      # 돌 띠
    rect(t, 0, 6, 31, 6, 'c')
    key_pattern(t, 0, 31, 7)
    rect(t, 0, 11, 31, 11, 'G')     # 아래 금테
    rect(t, 0, 12, 31, 12, 'g')
    if kind == 'mid' and crest:     # 가운데 금 장식(연꽃 모양 볏)
        for x, y, ch in ((15, 0, 'y'), (16, 0, 'y'), (14, 1, 'g'), (15, 1, 'y'), (16, 1, 'y'), (17, 1, 'g'),
                         (12, 2, 'g'), (13, 2, 'y'), (14, 2, 'g'), (15, 2, 'y'), (16, 2, 'y'), (17, 2, 'g'),
                         (18, 2, 'y'), (19, 2, 'g'), (11, 1, 'G'), (20, 1, 'G')):
            put(t, x, y, ch)
    if kind in ('left', 'right'):   # 끝 마감 기둥
        xs = range(0, 4) if kind == 'left' else range(28, 32)
        for x in xs:
            rect(t, x, 1, x, 13, 'g')
        x_hi = 0 if kind == 'left' else 28
        rect(t, x_hi, 1, x_hi, 13, 'y')
        x_sh = 3 if kind == 'left' else 31
        rect(t, x_sh, 1, x_sh, 13, 'G')
        cx = 1 if kind == 'left' else 29
        rect(t, cx, 6, cx + 1, 8, 'b')
    outline(t, open_lr=True)
    if kind == 'left':  # 왼쪽 끝은 닫는다
        rect(t, 0, 1, 0, 13, 'o')
    if kind == 'right':
        rect(t, 31, 1, 31, 13, 'o')
    return t


def corner():
    """왼쪽 위 모서리 장식: 위 띠 + 오른쪽 아래로 내려가는 세로 띠(L자)."""
    t = trim('mid')
    t[0:3] = 0  # 볏 제거
    for y in range(3, 32):
        rect(t, 22, y, 23, y, 'y'); rect(t, 24, y, 24, y, 'g')
        rect(t, 25, y, 29, y, 'b'); rect(t, 30, y, 30, y, 'G'); rect(t, 31, y, 31, y, 'g')
        if y % 8 in (0, 1, 2, 3):
            put(t, 26, y, 'G')
        if y % 8 == 3:
            put(t, 27, y, 'G'); put(t, 28, y, 'G')
    t[3:13, 24:32] = 0
    rect(t, 22, 3, 31, 4, 'y'); rect(t, 22, 5, 31, 12, 'b'); rect(t, 22, 11, 31, 11, 'G')
    for x, y in ((25, 7), (26, 7), (27, 8), (28, 9), (26, 9)):
        put(t, x, y, 'g')
    t[13:32, 0:22] = 0
    outline(t)
    return t


def brazier(frame):
    t = blank()
    # 받침
    rect(t, 9, 27, 22, 29, 'b'); rect(t, 9, 27, 22, 27, 'c'); rect(t, 9, 29, 22, 29, 'a')
    rect(t, 10, 26, 21, 26, 'g'); rect(t, 10, 26, 13, 26, 'y')
    # 기둥
    rect(t, 12, 20, 19, 25, 'b'); rect(t, 12, 20, 12, 25, 'c'); rect(t, 19, 20, 19, 25, 'a')
    rect(t, 12, 22, 19, 23, 'g'); rect(t, 12, 22, 14, 22, 'y'); rect(t, 12, 23, 19, 23, 'G')
    # 그릇(위가 넓은 사다리꼴)
    for i, y in enumerate(range(14, 20)):
        x0, x1 = 7 + i // 2, 24 - i // 2
        rect(t, x0, y, x1, y, 'b')
        put(t, x0, y, 'c'); put(t, x1, y, 'a')
    rect(t, 6, 13, 25, 14, 'g'); rect(t, 6, 13, 12, 13, 'y'); rect(t, 6, 14, 25, 14, 'G')
    rect(t, 8, 16, 23, 16, 'G'); put(t, 15, 16, 'y'); put(t, 16, 16, 'y')  # 띠 + 보석
    rect(t, 7, 12, 24, 12, 'a')  # 그릇 안쪽(어둠)
    # 청록 불꽃
    flame = [
        ["...........w....", "..........kw....", ".........kwk....", "........kwwk....", ".......kkwwkk...",
         "......Ckwwwkk...", "......Ckkwwkk...", ".....CCkkwkkC...", ".....CCkkkkkC...", "....CCCkkkkCC...",
         "....CCCCkkCCC..."],
        ["....w...........", "....kw......k...", ".....kk....kw...", ".....kwk..kwk...", "......kwkkwk....",
         "......Ckwwwk....", ".....CkkwwwkC...", ".....CCkwwkkC...", "....CCkkkkkCC...", "....CCCkkkkCC...",
         "....CCCCkkCCC..."],
    ][frame]
    for dy, row in enumerate(flame):
        for dx, ch in enumerate(row):
            if ch != '.':
                put(t, 8 + dx, 1 + dy, {'w': 'w', 'k': 'k', 'C': 'C'}[ch])
    for x, y in (((6, 3), (25, 6)) if frame == 0 else ((24, 2), (7, 7))):  # 불티
        put(t, x, y, 'k')
    outline(t)
    # 불꽃은 외곽선 대신 가장자리를 짙은 청록으로(빛나 보이게)
    for y in range(0, 13):
        for x in range(32):
            if tuple(t[y, x, :3]) == P['o'] and y < 12:
                t[y, x] = (*P['C'], 255) if any(
                    0 <= y + dy < 32 and 0 <= x + dx < 32 and tuple(t[y + dy, x + dx, :3]) in (P['k'], P['w'])
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1))) else t[y, x]
    return t


if __name__ == '__main__':
    import os
    os.makedirs('Art_Source/boss/deco', exist_ok=True)
    parts = {'trim_left': trim('left'), 'trim_mid': trim('mid'), 'trim_right': trim('right'),
             'trim_corner': corner(), 'brazier_0': brazier(0), 'brazier_1': brazier(1)}
    order = ['trim_left', 'trim_mid', 'trim_right', 'trim_corner', 'brazier_0', 'brazier_1']
    sheet = Image.open('Art_Source/boss/boss_deco_32.png').convert('RGBA')
    for name, arr in parts.items():
        img = Image.fromarray(arr, 'RGBA')
        img.save(f'Art_Source/boss/deco/{name}.png')
        i = order.index(name)
        x, y = (i % 4) * 32, (i // 4) * 32
        sheet.paste(Image.new('RGBA', (32, 32), (0, 0, 0, 0)), (x, y))
        sheet.alpha_composite(img, (x, y))
    sheet.save('Art_Source/boss/boss_deco_32.png')
    print('ok')


# ---- 4면 금 장식(2026-10-06 추가): 위 띠를 기준으로 아래·왼쪽·오른쪽 띠와 바깥 모서리 4종을 만든다.
# 띠는 모두 바닥에서 먼 쪽(칸의 바깥 가장자리)에 붙는다. 빛은 왼쪽 위 기준으로 하이라이트(y)·그림자(G)를 다시 맞춘다.
def _swap_hi_sh(t):
    y, G = np.array((*P['y'], 255)), np.array((*P['G'], 255))
    m_y = (t == y).all(2)
    m_G = (t == G).all(2)
    t[m_y] = G
    t[m_G] = y
    return t


def trim_side(side, kind='mid'):
    """side: top/bottom/left/right, kind: left/mid/right(띠의 시작·가운데·끝).
    3/4 시점 벽의 '윗면'(원본 타일의 밝은 벽돌 자리)에 얹히도록 칸 안 위치를 맞춘다(2026-10-06 수정 — 처음엔 뒤집기·돌리기만 해서
    아래·옆 띠가 벽 앞면 그늘 위에 놓였음). 원본 벽 칸에서 밝은 벽돌 위치:
    - 위 벽(0행)·아래 벽(4행): 칸 위쪽(16px 기준 1~5행) → 띠를 칸 위쪽 3~12행에(위 띠와 같은 모양, 아래는 볏 없음)
    - 왼쪽 벽(0열): 칸 오른쪽(10~14열 → 32px 20~29열) → 세로 띠를 오른쪽 20~29열에
    - 오른쪽 벽(5열): 칸 왼쪽(1~5열 → 32px 2~11열) → 세로 띠를 왼쪽 3~12열에"""
    if side == 'top':
        return trim(kind)
    base = trim(kind, crest=False)
    if side == 'bottom':
        return base
    t = base.transpose(1, 0, 2).copy()  # 위 띠 → 세로 띠(왼쪽 3~12열, 하이라이트가 왼쪽)
    if side == 'right':
        return t
    shifted = np.zeros_like(t)          # 왼쪽 벽: 오른쪽 20~29열로 옮김(17열 이동)
    shifted[:, 17:32] = t[:, 0:15]
    return shifted


def corner_cap(pos):
    """바깥 모서리 마감(금 테 사각 + 청록 보석, 14×14). 가로 띠(칸 위쪽 3~12행)와 세로 띠가 만나는 자리.
    pos: tl/tr/bl/br — 왼쪽 모서리 칸은 세로 띠가 오른쪽(20~29열), 오른쪽 모서리 칸은 왼쪽(3~12열)이라 x만 다르다."""
    t = blank()
    x0 = 17 if pos in ('tl', 'bl') else 1
    y0 = 1
    rect(t, x0 + 1, y0 + 1, x0 + 12, y0 + 12, 'g')
    rect(t, x0 + 1, y0 + 1, x0 + 12, y0 + 1, 'y'); rect(t, x0 + 1, y0 + 1, x0 + 1, y0 + 12, 'y')
    rect(t, x0 + 1, y0 + 12, x0 + 12, y0 + 12, 'G'); rect(t, x0 + 12, y0 + 1, x0 + 12, y0 + 12, 'G')
    rect(t, x0 + 4, y0 + 4, x0 + 9, y0 + 9, 'b')
    rect(t, x0 + 5, y0 + 5, x0 + 8, y0 + 8, 'C'); rect(t, x0 + 5, y0 + 5, x0 + 6, y0 + 6, 'k'); put(t, x0 + 5, y0 + 5, 'w')
    outline(t)
    return t


def save_sides():
    import os
    os.makedirs('Art_Source/boss/deco', exist_ok=True)
    out = {}
    for side in ('bottom', 'left', 'right'):
        for kind in ('left', 'mid', 'right'):
            out[f'trim_{side}_{kind}'] = trim_side(side, kind)
    for pos in ('tl', 'tr', 'bl', 'br'):
        out[f'cap_{pos}'] = corner_cap(pos)
    for name, arr in out.items():
        Image.fromarray(arr, 'RGBA').save(f'Art_Source/boss/deco/{name}.png')
    return out


# ---- 모서리 이음(2026-10-06): 파란 보석 마감 대신 가로 띠와 세로 띠가 꺾여 이어지는 L자 조각.
# 띠 단면: 가로 = 칸 위쪽 3~12행, 세로 = 왼쪽 벽 칸 20~29열 / 오른쪽 벽 칸 3~12열.
# 겹치는 정사각형(10×10)은 금테 + 돌 + 금 마름모로 채워 꺾인 마디처럼 보이게 한다.
def corner_join(pos):
    band_h = trim('mid', crest=False)            # 가로 띠(칸 위쪽 3~12행)
    band_v = band_h.transpose(1, 0, 2).copy()     # 세로 띠(왼쪽 3~12열)
    left_side = pos in ('tl', 'bl')
    vx0 = 20 if left_side else 3                  # 세로 띠 x 범위
    t = blank()
    # 세로 띠: 위 모서리면 마디(3행)부터 아래로, 아래 모서리면 위에서 마디(12행)까지
    vy0, vy1 = (3, 31) if pos in ('tl', 'tr') else (0, 12)
    src_x0 = 3                                    # band_v에서 띠가 있는 열(3~12)
    for y in range(vy0, vy1 + 1):
        for i in range(10):
            t[y, vx0 + i] = band_v[y, src_x0 + i]
    # 가로 띠: 왼쪽 모서리면 마디부터 오른쪽 끝까지, 오른쪽 모서리면 왼쪽 끝부터 마디까지
    hx0, hx1 = (vx0, 31) if left_side else (0, vx0 + 9)
    for x in range(hx0, hx1 + 1):
        for y in range(3, 13):
            t[y, x] = band_h[y, x]
    # 마디(겹침 사각형) 장식
    x0, y0 = vx0, 3
    rect(t, x0, y0, x0 + 9, y0 + 9, 'g')
    rect(t, x0, y0, x0 + 9, y0, 'y'); rect(t, x0, y0, x0, y0 + 9, 'y')
    rect(t, x0, y0 + 9, x0 + 9, y0 + 9, 'G'); rect(t, x0 + 9, y0, x0 + 9, y0 + 9, 'G')
    rect(t, x0 + 2, y0 + 2, x0 + 7, y0 + 7, 'b')
    for dx, dy in ((4, 3), (5, 3), (3, 4), (6, 4), (3, 5), (6, 5), (4, 6), (5, 6)):
        put(t, x0 + dx, y0 + dy, 'g')
    put(t, x0 + 4, y0 + 4, 'y'); put(t, x0 + 5, y0 + 4, 'g'); put(t, x0 + 4, y0 + 5, 'g'); put(t, x0 + 5, y0 + 5, 'G')
    # 외곽선(띠가 칸 밖으로 이어지는 쪽은 연다)
    al = t[:, :, 3] > 0
    for y in range(32):
        for x in range(32):
            if not al[y, x]:
                continue
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if not (0 <= ny < 32 and 0 <= nx < 32):
                    continue  # 칸 밖 = 이웃 띠로 이어짐
                if not al[ny, nx]:
                    t[y, x] = (*P['o'], 255)
                    break
    return t


def save_joins():
    out = {}
    for pos in ('tl', 'tr', 'bl', 'br'):
        out[f'join_{pos}'] = corner_join(pos)
        Image.fromarray(out[f'join_{pos}'], 'RGBA').save(f'Art_Source/boss/deco/join_{pos}.png')
    return out
