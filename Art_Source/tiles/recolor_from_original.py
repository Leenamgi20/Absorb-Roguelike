from PIL import Image
import numpy as np
from collections import Counter
o=np.array(Image.open('Assets/ThirdParty/EdgarExample1/Example1/example1_tileset.png').convert('RGBA')).astype(int)
CELLS=[(0,0),(0,1),(0,2),(0,3),(0,4),(0,5),(0,6),(0,7),(0,8),(0,9),(1,0),(1,1),(1,2),(1,4),(1,5),(1,6),(1,7),(1,8),(1,9),(2,0),(2,1),(2,2),(2,3),(2,4),(2,5),(2,7),(2,8),(3,0),(3,1),(3,2),(3,4),(3,5),(4,0),(4,1),(4,2),(4,3),(4,4),(4,5),(5,0),(5,1),(5,3),(5,4),(5,5),(7,0),(7,1),(7,2),(7,3)]
PAL=set()
from collections import Counter as _C
_c=_C()
for r,k in CELLS:
    for p in o[r*16:r*16+16,k*16:k*16+16].reshape(-1,4):
        if p[3]>0: _c[tuple(p[:3])]+=1
PAL=set(_c)
struct=np.zeros((160,160),bool)
for r in range(10):
    for k in range(10):
        b=o[r*16:r*16+16,k*16:k*16+16].reshape(-1,4); op=b[b[:,3]>0]
        if len(op)==0: continue
        frac=sum(tuple(p[:3]) in PAL for p in op)/len(op)
        if frac>=0.9 or (r,k) in CELLS: struct[r*16:r*16+16,k*16:k*16+16]=True
lum=lambda c:0.299*c[0]+0.587*c[1]+0.114*c[2]
def build(key):
    ai=np.array(Image.open(f'Art_Source/tiles/backup_v1/tileset_{key}_32.png').convert('RGBA')).astype(int)
    # ai at 16-grid: each orig pixel -> 2x2 block
    samples={}
    for y in range(160):
        for x in range(160):
            if not struct[y,x] or o[y,x,3]==0: continue
            c=tuple(o[y,x,:3]); blk=ai[2*y:2*y+2,2*x:2*x+2].reshape(-1,4)
            for p in blk:
                if p[3]>0: samples.setdefault(c,[]).append(tuple((p[:3]//8*8+4)))
    mode={c:np.array(Counter(lst).most_common(1)[0][0]) for c,lst in samples.items()}
    # 재질 그룹(원본 밝기 기준): 어둠(≤30) / 바닥(31~60) / 벽 윗면(>60). 그룹 대표색의 AI 색을 기준으로,
    # 같은 그룹의 다른 원본 색은 원본 밝기 비율만큼 밝기를 바꿔 원본의 줄눈·하이라이트 구조를 그대로 살린다.
    groups={'dark':[], 'floor':[], 'wall':[]}
    for c in samples:
        l=lum(c); groups['dark' if l<=30 else 'floor' if l<=60 else 'wall'].append(c)
    mapping={}
    for g,cs in groups.items():
        if not cs: continue
        base=max(cs,key=lambda c:len(samples[c]))
        bai=mode[base].astype(float); lb=lum(base)
        for c in cs:
            mapping[c]=np.clip(bai*(lum(c)/lb),0,255).astype(int)
    res=ai.copy()
    for y in range(160):
        for x in range(160):
            if not struct[y,x]: continue
            if o[y,x,3]==0: res[2*y:2*y+2,2*x:2*x+2]=0; continue
            res[2*y:2*y+2,2*x:2*x+2,:3]=mapping[tuple(o[y,x,:3])]; res[2*y:2*y+2,2*x:2*x+2,3]=255
    Image.fromarray(res.astype(np.uint8),'RGBA').save(f'Art_Source/tiles/tileset_{key}_32.png')
    return res
if __name__=='__main__':
    import sys
    outs=[build(k) for k in ['fire','ice','electric','poison','earth']]
    sheet=Image.new('RGBA',(5*330,330),(60,60,60,255))
    for i,r in enumerate(outs): sheet.alpha_composite(Image.fromarray(r.astype(np.uint8),'RGBA'),(i*330+5,5))
    sheet.convert('RGB').save('Art_Source/tiles/preview_recolor_v2.png'); sheet.convert('RGB').save('C:/tmp/rc.png')
