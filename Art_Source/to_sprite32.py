# 바르코 생성 이미지(마젠타 배경, '32px 도트를 키운' 그림) → 32×32 스프라이트 변환(2026-10-06).
# 1) 마젠타가 아닌 물체 영역을 찾고, 가장자리 변화 주기로 원본 도트 한 칸 크기를 추정
# 2) 칸마다 가운데 영역의 중앙값 색을 1픽셀로(섞인 색이 생기지 않게)
# 3) 물체가 30px보다 크면 최빈색 샘플로 30px 안에 맞춤
# 4) 16색 이내로 줄이고, 마젠타 번짐 픽셀은 외곽선색으로, 바깥 가장자리에 1px 외곽선 보강
# 5) 32×32 캔버스 가운데·아래 2px 여백으로 배치(발이 바닥에 닿게), 사방 최소 1px 여백
# 사용: python to_sprite32.py 입력.png 출력.png [캔버스크기=32] [색수=16] [원본 도트 한 칸 px — 자동 감지가 빗나갈 때] [soft=1: 정령 불꽃처럼 연한 빛 가장자리는 외곽선을 넣지 않고, 마젠타 번짐은 투명 처리 — 보스용]  (보스 64px: ... 64 24, 2026-10-07)
from PIL import Image
import numpy as np
from collections import Counter
import sys

OUTLINE = (22, 18, 26)


def is_mag(a):
    return (a[..., 0] > 170) & (a[..., 1] < 110) & (a[..., 2] > 170)


def grid_size(a, box):
    x0, y0, x1, y1 = box
    sub = a[y0:y1, x0:x1].astype(int)
    best = None
    for axis in (1, 0):
        d = (np.abs(np.diff(sub, axis=axis)).sum(2) > 90).sum(0 if axis == 1 else 1)
        for cw in np.arange(6, 48, 0.05):
            ph = (np.arange(len(d)) % cw) / cw
            z = (d * np.exp(2j * np.pi * ph)).sum()
            v = abs(z) / max(d.sum(), 1)
            # 반 주기(2배 고조파)에 속지 않게 큰 칸 쪽을 살짝 우대
            score = v * (1 + cw / 200)
            if best is None or score > best[0]:
                best = (score, cw, (np.angle(z) / (2 * np.pi)) % 1 * cw, axis)
    return best[1]


def convert(src, dst, size=32, colors=16, cell=0, soft=0):
    a = np.array(Image.open(src).convert('RGB')).astype(int)
    mag = is_mag(a)
    ys, xs = np.where(~mag)
    box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
    cw = cell if cell else grid_size(a, box)
    x0, y0, x1, y1 = box
    nx = max(1, round((x1 - x0) / cw)); ny = max(1, round((y1 - y0) / cw))
    fit = size - 2
    scale = min(1.0, fit / max(nx, ny))
    tw, th = max(1, round(nx * scale)), max(1, round(ny * scale))
    out = np.zeros((th, tw, 4), np.uint8)
    for ty in range(th):
        for tx in range(tw):
            sx0 = x0 + (x1 - x0) * tx / tw; sx1 = x0 + (x1 - x0) * (tx + 1) / tw
            sy0 = y0 + (y1 - y0) * ty / th; sy1 = y0 + (y1 - y0) * (ty + 1) / th
            # 칸 가운데 60% 영역
            cx0 = int(sx0 + (sx1 - sx0) * .2); cx1 = max(cx0 + 1, int(sx1 - (sx1 - sx0) * .2))
            cy0 = int(sy0 + (sy1 - sy0) * .2); cy1 = max(cy0 + 1, int(sy1 - (sy1 - sy0) * .2))
            blk = a[cy0:cy1, cx0:cx1].reshape(-1, 3)
            m = is_mag(blk)
            if m.mean() > 0.5:
                continue
            good = blk[~m]
            if scale < 1.0:
                c = Counter(map(tuple, np.minimum(good // 12 * 12 + 6, 255))).most_common(1)[0][0]  # 255 넘침 방지(흰색이 2로 감기던 버그)
            else:
                c = tuple(np.median(good, 0).astype(int))
            out[ty, tx] = (*c, 255)
    al = out[:, :, 3] > 0
    if al.any():
        pal_img = Image.fromarray(out[:, :, :3][al].reshape(1, -1, 3)).quantize(colors=colors, method=Image.MEDIANCUT, dither=Image.NONE)
        used = [i for _, i in pal_img.getcolors()]  # 실제로 쓰인 팔레트 칸만(빈 칸의 임의 색 — 하늘색 등 — 이 섞이지 않게)
        full = np.array(pal_img.getpalette()).reshape(-1, 3)
        pal = full[used]
        for y, x in zip(*np.where(al)):
            c = out[y, x, :3].astype(int)
            if c[0] > 150 and c[1] < 90 and c[2] > 150:
                if soft:
                    out[y, x, 3] = 0  # 가장자리 마젠타 번짐 → 투명
                    al[y, x] = False
                else:
                    out[y, x, :3] = OUTLINE
            else:
                out[y, x, :3] = pal[((pal - c) ** 2).sum(1).argmin()]
    # 바깥 외곽선 보강
    h, w = al.shape
    for y in range(h):
        for x in range(w):
            if not al[y, x]:
                continue
            edge = any(not (0 <= y + dy < h and 0 <= x + dx < w) or not al[y + dy, x + dx]
                       for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            px3 = out[y, x, :3].astype(int)
            glow = soft and (px3.sum() > 560 or (px3[2] > 190 and px3[1] > 170 and px3[0] < 200))  # 연한 빛·흰청록 불꽃
            if edge and px3.sum() > 150 and not glow:
                out[y, x, :3] = OUTLINE
    canvas = np.zeros((size, size, 4), np.uint8)
    ox = (size - tw) // 2
    oy = max(1, size - 2 - th)
    canvas[oy:oy + th, ox:ox + tw] = out[:size - oy, :size - ox]
    Image.fromarray(canvas, 'RGBA').save(dst)
    return cw, (nx, ny), (tw, th)


if __name__ == '__main__':
    print(convert(sys.argv[1], sys.argv[2], *(int(v) for v in sys.argv[3:7])))
