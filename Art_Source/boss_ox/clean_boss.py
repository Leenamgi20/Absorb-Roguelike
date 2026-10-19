# 보스 64px 정리(2026-10-07): 변환 결과에서 몸과 떨어진 작은 조각(정령 불꽃이 외곽선색으로 뭉개진 점들)을 지운다.
# 가장 큰 덩어리(4방향 연결)만 남김. 사용: python -X utf8 Art_Source/boss_ox/clean_boss.py 입력 출력
import sys
from PIL import Image
im = Image.open(sys.argv[1]).convert('RGBA')
w, h = im.size
px = im.load()
seen = set()
comps = []
for y in range(h):
    for x in range(w):
        if px[x, y][3] and (x, y) not in seen:
            stack = [(x, y)]; seen.add((x, y)); comp = []
            while stack:
                cx, cy = stack.pop(); comp.append((cx, cy))
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < w and 0 <= ny < h and px[nx, ny][3] and (nx, ny) not in seen:
                        seen.add((nx, ny)); stack.append((nx, ny))
            comps.append(comp)
comps.sort(key=len, reverse=True)
for comp in comps[1:]:
    for x, y in comp:
        px[x, y] = (0, 0, 0, 0)
im.save(sys.argv[2])
print('components', [len(c) for c in comps[:6]])
