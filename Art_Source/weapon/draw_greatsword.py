# 낡은 대검 12×40 도트(2026-10-06). 마을 늙은 경비병이 물려준 이 빠진 대검(GDD 7A). 기존 임시 무기와 같은 크기·피벗
# (아래 가운데, PPU 32 → 길이 1.25칸)이라 공격 판정·연출 위치는 그대로다. 칼끝이 위(+y), 손잡이가 아래.
# 빛은 왼쪽 위, 외곽선 1px. 칼날·손잡이·폼멜은 12px 폭 가운데(5.5) 기준 대칭. 이 빠진 날·녹 점·감은 가죽 손잡이·청동 코등이.
from PIL import Image

W, H = 12, 40
P = {
    'o': (22, 18, 26),
    'S': (214, 222, 228), 's': (160, 170, 182), 'd': (102, 110, 124),   # 칼날 밝음/중간/그늘
    'r': (150, 96, 60),                                                 # 녹
    'B': (236, 184, 92), 'b': (184, 128, 52), 'n': (120, 78, 30),       # 청동 코등이·폼멜
    'L': (126, 76, 46), 'l': (86, 50, 30),                              # 가죽 손잡이
}
ROWS = [
    ".....oo.....",  # 0 칼끝
    "....oSso....",
    "....oSsdo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSs.do...",  # 이 빠짐(오른쪽 날)
    "...oSsodo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSrsdo...",  # 녹
    "...oSssdo...",
    "...oSssdo...",
    "....oSsdo...",  # 이 빠짐(왼쪽 날)
    "...oSssdo...",
    "...oSssdo...",
    "...oSsrdo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSsrdo...",
    "...oSssdo...",
    "...oSssdo...",
    "...oSssdo...",
    "oooooooooooo",  # 27 코등이
    "oBBBbbbbbbno",
    "obbbbbbbbnno",
    "oooooooooooo",
    "....oLlo....",  # 31 손잡이
    "....olLo....",
    "....oLlo....",
    "....olLo....",
    "....oLlo....",
    "...oooooo...",  # 36 폼멜
    "...oBbbno...",
    "...onnnno...",
    "....oooo....",
]
assert len(ROWS) == H and all(len(r) == W for r in ROWS)
img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
for y, row in enumerate(ROWS):
    for x, ch in enumerate(row):
        if ch != '.':
            img.putpixel((x, y), P[ch] + (255,))
img.save('Art_Source/weapon/greatsword_12x40.png')
print('ok')
