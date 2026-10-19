# 보스 방 장식 12종을 32×32 칸으로 다시 찍는다(2026-10-06). 참조: 바르코 GPT 2번(deco_gpt_src.png).
# 1) 마젠타 배경에서 물체 덩어리를 찾고(가까운 조각은 합침) 4×3 배치 순서로 정렬
# 2) 덩어리마다 비율을 지키며 32칸 안에 맞춰, 출력 1픽셀 = 참조 블록의 최빈색(16단계 양자화)으로 샘플
# 3) 장식 전체를 같은 16색 팔레트로 줄이고, 바깥 가장자리에 1px 어두운 외곽선을 보강
# 결과: boss_deco_32.png(128×96, 칸 순서 = 아래 NAMES), 칸별 PNG는 deco/ 폴더.
from PIL import Image
import numpy as np
from collections import Counter
import os

NAMES = ['trim_left', 'trim_mid', 'trim_right', 'trim_corner',
         'brazier_0', 'brazier_1', 'pillar', 'banner',
         'statue', 'rune_stone', 'seal_stone', 'coins']
# 벽 장식은 칸 위쪽에 붙이고(벽 윗면에 얹음), 나머지는 칸 아래 가운데에 세운다.
TOP_ALIGN = {'trim_left', 'trim_mid', 'trim_right', 'trim_corner'}
FULL_WIDTH = {'trim_left', 'trim_mid', 'trim_right'}  # 이어 붙는 띠는 폭 32를 꽉 채운다

a = np.array(Image.open('Art_Source/boss/deco_gpt_src.png').convert('RGB')).astype(int)
H, W = a.shape[:2]
mag = (a[:, :, 0] > 190) & (a[:, :, 1] < 90) & (a[:, :, 2] > 190)
solid = ~mag

# 덩어리 찾기(작게 축소한 마스크에서 연결 → 근접 조각 합치기)
step = 8
small = solid[::step, ::step]
lab = np.zeros(small.shape, int)
n = 0
for y in range(small.shape[0]):
    for x in range(small.shape[1]):
        if small[y, x] and not lab[y, x]:
            n += 1
            st = [(y, x)]
            lab[y, x] = n
            while st:
                cy, cx = st.pop()
                for dy in (-2, -1, 0, 1, 2):
                    for dx in (-2, -1, 0, 1, 2):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < small.shape[0] and 0 <= nx < small.shape[1] and small[ny, nx] and not lab[ny, nx]:
                            lab[ny, nx] = n
                            st.append((ny, nx))
boxes = []
for i in range(1, n + 1):
    ys, xs = np.where(lab == i)
    if len(ys) < 20:
        continue
    boxes.append([xs.min() * step, ys.min() * step, (xs.max() + 1) * step, (ys.max() + 1) * step])
# 행으로 묶어 정렬
boxes.sort(key=lambda b: (b[1] + b[3]) / 2)
rows, cur = [], [boxes[0]]
for b in boxes[1:]:
    if abs((b[1] + b[3]) / 2 - (cur[-1][1] + cur[-1][3]) / 2) < 120:
        cur.append(b)
    else:
        rows.append(cur); cur = [b]
rows.append(cur)
ordered = [b for r in rows for b in sorted(r, key=lambda b: b[0])]
assert len(ordered) == 12, f'덩어리 {len(ordered)}개 — 배치 확인 필요'

q = a // 16 * 16 + 8


def sample(box):
    x0, y0, x1, y1 = box
    bw, bh = x1 - x0, y1 - y0
    return x0, y0, bw, bh


cells = []
for name, box in zip(NAMES, ordered):
    x0, y0, bw, bh = sample(box)
    if name in FULL_WIDTH:
        tw = 32; th = max(1, round(bh * 32 / bw))
    else:
        s = min(30 / bw, 30 / bh)
        tw, th = max(1, round(bw * s)), max(1, round(bh * s))
    out = np.zeros((32, 32, 4), np.uint8)
    ox = (32 - tw) // 2
    oy = 0 if name in TOP_ALIGN else 32 - th - 1
    for ty in range(th):
        for tx in range(tw):
            sx0 = x0 + int(tx * bw / tw); sx1 = x0 + max(int((tx + 1) * bw / tw), int(tx * bw / tw) + 1)
            sy0 = y0 + int(ty * bh / th); sy1 = y0 + max(int((ty + 1) * bh / th), int(ty * bh / th) + 1)
            m = solid[sy0:sy1, sx0:sx1]
            if m.mean() < 0.45:
                continue
            blk = q[sy0:sy1, sx0:sx1][m].reshape(-1, 3)
            c = Counter(map(tuple, blk)).most_common(1)[0][0]
            out[oy + ty, ox + tx] = (*c, 255)
    cells.append((name, out))

# 공통 16색 팔레트
allpx = np.concatenate([c[:, :, :3][c[:, :, 3] > 0] for _, c in cells])
palimg = Image.fromarray(allpx.reshape(1, -1, 3).astype(np.uint8)).quantize(colors=16, method=Image.MEDIANCUT, dither=Image.NONE)
pal = np.array(palimg.getpalette()[:48]).reshape(16, 3)
dark = pal[np.argmin(pal.sum(1))]

os.makedirs('Art_Source/boss/deco', exist_ok=True)
sheet = Image.new('RGBA', (128, 96), (0, 0, 0, 0))
for i, (name, c) in enumerate(cells):
    rgb = c[:, :, :3].astype(int); al = c[:, :, 3] > 0
    for y in range(32):
        for x in range(32):
            if al[y, x]:
                rgb[y, x] = pal[((pal - rgb[y, x]) ** 2).sum(1).argmin()]
    # 외곽선: 투명과 맞닿은 불투명 픽셀이 충분히 어둡지 않으면 가장 어두운 색으로
    edge = np.zeros_like(al)
    for y in range(32):
        for x in range(32):
            if al[y, x] and any(not (0 <= y + dy < 32 and 0 <= x + dx < 32) or not al[y + dy, x + dx]
                                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                edge[y, x] = True
    keep_open = name in FULL_WIDTH  # 이어 붙는 띠는 좌우 끝을 열어 둔다
    for y in range(32):
        for x in range(32):
            if edge[y, x] and rgb[y, x].sum() > 200:
                if keep_open and (x in (0, 31)):
                    continue
                rgb[y, x] = dark
    img = Image.fromarray(np.dstack([rgb, np.where(al, 255, 0)]).astype(np.uint8), 'RGBA')
    img.save(f'Art_Source/boss/deco/{name}.png')
    sheet.alpha_composite(img, ((i % 4) * 32, (i // 4) * 32))
sheet.save('Art_Source/boss/boss_deco_32.png')
print('ok', [b for b in ordered][:2])
