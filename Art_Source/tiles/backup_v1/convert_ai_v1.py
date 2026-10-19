from PIL import Image
import numpy as np, sys
ref=np.array(Image.open('edgar_ref_160.png').convert('RGB')).astype(int)
refmask=(ref[:,:,0]>200)&(ref[:,:,1]<60)&(ref[:,:,2]>200)
alpha32=np.repeat(np.repeat(~refmask,2,0),2,1)  # 320x320 opaque mask
P={'fire':(0.995,2,4),'ice':(0.995,0,4),'electric':(0.985,2,-4),'poison':(0.95,6,12),'earth':(0.975,10,-4)}
def ismag(c): return c[...,0]>170 and c[...,1]<110 and c[...,2]>170
out={}
for k,(s,ox,oy) in P.items():
    a=np.array(Image.open(f'{k}_gpt_src.png').convert('RGB')).astype(int)
    mag=(a[:,:,0]>170)&(a[:,:,1]<110)&(a[:,:,2]>170)
    res=np.zeros((320,320,3),int); valid=np.zeros((320,320),bool)
    for y in range(320):
        cy=(y+0.5)*3.2*s+oy
        for x in range(320):
            cx=(x+0.5)*3.2*s+ox
            y0,y1=int(cy-1),int(cy+1)+1; x0,x1=int(cx-1),int(cx+1)+1
            blk=a[max(y0,0):y1,max(x0,0):x1].reshape(-1,3); mb=mag[max(y0,0):y1,max(x0,0):x1].reshape(-1)
            good=blk[~mb]
            if len(good): res[y,x]=np.median(good,0); valid[y,x]=True
    # fill opaque pixels that came out magenta with nearest valid neighbor
    need=alpha32&~valid
    for it in range(6):
        if not need.any(): break
        ys,xs=np.where(need)
        for y,x in zip(ys,xs):
            nb=[(y+dy,x+dx) for dy in(-1,0,1) for dx in(-1,0,1) if 0<=y+dy<320 and 0<=x+dx<320 and valid[y+dy,x+dx]]
            if nb: res[y,x]=res[nb[0]]; valid[y,x]=True
        need=alpha32&~valid
    img=Image.fromarray(res.astype(np.uint8),'RGB')
    q=np.array(img.quantize(colors=32,method=Image.MEDIANCUT,dither=Image.NONE).convert('RGB'))
    rgba=np.dstack([q,np.where(alpha32,255,0)]).astype(np.uint8)
    Image.fromarray(rgba,'RGBA').save(f'tileset_{k}_32.png'); out[k]=rgba
    print(k,'unfilled',int(need.sum()))
# preview
sheet=Image.new('RGBA',(5*330,330),(60,60,60,255))
for i,k in enumerate(P):
    sheet.alpha_composite(Image.fromarray(out[k],'RGBA'),(i*330+5,5))
sheet.convert('RGB').save('preview_5_tilesets.png'); sheet.convert('RGB').save('C:/tmp/p5.png')
