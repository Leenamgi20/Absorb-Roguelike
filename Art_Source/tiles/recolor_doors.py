# 복도 문 스프라이트 칸(Door horizontal = Dungeon_Tileset_36 = 칸(3,6), Door vertical = Dungeon_Tileset_56 = 칸(5,6))을
# 원본 문 픽셀 배치 그대로 두고 색만 속성 색으로 바꾼다(AI판은 이 칸에 문이 아닌 격자·기둥을 그려 어색했음).
# 색: 속성 시트 벽 윗면 대표색 × (원본 문 밝기 범위를 35%~110%로 옮긴 값). 입력은 문 재색칠 전 시트(Assets 쪽 사본)를 다시 쓰지 않도록 Art_Source 시트에 한 번만 적용.
from PIL import Image
import numpy as np
from collections import Counter
o=np.array(Image.open('Assets/ThirdParty/EdgarExample1/Example1/example1_tileset.png').convert('RGBA')).astype(float)
lum=lambda c:0.299*c[0]+0.587*c[1]+0.114*c[2]
DOOR=[(3,6),(5,6)]
WALL_ORIG=np.array([110,74,72.])  # 원본 벽 윗면 대표색
_door=[tuple(o[y,x,:3]) for r,k in DOOR for y in range(r*16,r*16+16) for x in range(k*16,k*16+16) if o[y,x,3]>0]
LMIN=min(lum(c) for c in _door); LMAX=max(lum(c) for c in _door)  # 원본 문 밝기 범위 → 속성 벽색의 35%~110%
for key in ['fire','ice','electric','poison','earth']:
    p=f'Art_Source/tiles/tileset_{key}_32.png'
    t=np.array(Image.open(p).convert('RGBA')).astype(float)
    # 원본에서 벽 대표색이 있던 자리의 속성 색 = 테마 벽색
    ys,xs=np.where((o[:,:,0]==110)&(o[:,:,1]==74)&(o[:,:,2]==72))
    wall=np.array(Counter(tuple(t[2*y,2*x,:3]) for y,x in zip(ys,xs)).most_common(1)[0][0])
    for r,k in DOOR:
        for y in range(r*16,r*16+16):
            for x in range(k*16,k*16+16):
                c=o[y,x]
                if c[3]==0:
                    t[2*y:2*y+2,2*x:2*x+2]=0; continue
                col=np.clip(wall*(0.35+0.75*(lum(c[:3])-LMIN)/(LMAX-LMIN)),0,255)
                t[2*y:2*y+2,2*x:2*x+2,:3]=col; t[2*y:2*y+2,2*x:2*x+2,3]=255
    Image.fromarray(t.astype(np.uint8),'RGBA').save(p)
    print(key,'wall',wall)
