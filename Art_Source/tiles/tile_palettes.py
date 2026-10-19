# 속성별 32px 타일 색표(2026-10-06). make_floor32.py / make_wall32.py가 같이 쓴다.
# 바닥 기본색 = 백업 AI판(backup_v1) 바닥 최빈색, 벽 기본색 = 원본 배치 재색칠판의 벽 윗면 색(전기는 바닥과 대비가 약해 조금 어둡게).
# 장식 색 이름은 대지 기준(자갈 peb / 새싹 g / 호박 amber / 이끼 moss)이지만 속성마다 뜻이 다르다:
#   화염 = 불씨 돌·작은 불꽃·용암 균열 / 냉기 = 얼음 조각·서리 결정 / 전기 = 노란 결정·스파크·번개 균열 / 독 = 버섯·풀·독 균열.
# 흰색·거의 흰색은 쓰지 않는다(플레이어 흰 외곽선 규칙). 냉기 하이라이트도 연한 하늘색까지만.


def _mul(c, f, cap=245):
    return tuple(int(max(0, min(cap, v * f))) for v in c)


def _floor(base, peb, pebhi, pebsh, gdark, g, glight, fcrack=None):
    return dict(base=base, mid=_mul(base, .94), dark=_mul(base, .86), light=_mul(base, 1.05), hi=_mul(base, 1.14),
                shadow=_mul(base, .55), peb=peb, pebhi=pebhi, pebsh=pebsh, gdark=gdark, g=g, glight=glight,
                fcrack=fcrack or _mul(base, .55))


def _wall(base, amber, amberhi, ambersh, moss, mosslt):
    return dict(base=base, hi=_mul(base, 1.13), sh=_mul(base, .72), speck=_mul(base, .9), speck2=_mul(base, 1.06),
                crack=_mul(base, .5), root=_mul(base, .45), rootlt=_mul(base, .62),
                amber=amber, amberhi=amberhi, ambersh=ambersh, moss=moss, mosslt=mosslt, band=_mul(base, 1.1, 235))


FLOOR = {
    # 대지는 처음 만든 값 그대로(손으로 고른 색)
    'earth': dict(base=(205, 140, 74), mid=(195, 127, 62), dark=(181, 115, 53), light=(213, 148, 77),
                  hi=(223, 177, 76), shadow=(118, 67, 32), peb=(196, 136, 67), pebhi=(223, 177, 76),
                  pebsh=(118, 85, 36), gdark=(96, 110, 38), g=(138, 150, 50), glight=(186, 190, 78),
                  fcrack=(118, 67, 32)),
    'fire': _floor((238, 146, 76), peb=(150, 60, 40), pebhi=(255, 170, 60), pebsh=(90, 35, 25),
                   gdark=(200, 60, 30), g=(250, 120, 40), glight=(255, 200, 80), fcrack=(214, 70, 34)),
    # 냉기: 2026-10-06 유저 요청으로 밝은 서리빛 회청색(채도 낮게 — 채도 높은 하늘색 슬라임과 구분, 흰색 아님)
    'ice': _floor((180, 204, 222), peb=(96, 176, 226), pebhi=(150, 214, 240), pebsh=(110, 140, 170),
                  gdark=(60, 140, 200), g=(100, 180, 230), glight=(150, 214, 240), fcrack=(100, 132, 166)),
    'electric': _floor((170, 146, 206), peb=(110, 90, 160), pebhi=(255, 230, 90), pebsh=(70, 55, 110),
                       gdark=(220, 170, 40), g=(255, 215, 60), glight=(255, 240, 140), fcrack=(250, 210, 60)),
    'poison': _floor((114, 132, 62), peb=(170, 80, 170), pebhi=(230, 140, 220), pebsh=(90, 40, 90),
                     gdark=(60, 90, 30), g=(110, 160, 50), glight=(170, 210, 80), fcrack=(150, 220, 60)),
}

WALL = {
    'earth': dict(base=(200, 128, 62), hi=(226, 160, 84), sh=(150, 88, 39), speck=(184, 112, 50),
                  speck2=(212, 142, 70), crack=(110, 62, 30), root=(96, 58, 28), rootlt=(132, 84, 40),
                  amber=(240, 170, 52), amberhi=(255, 214, 110), ambersh=(176, 104, 30),
                  moss=(126, 146, 48), mosslt=(168, 182, 70), band=(226, 160, 92)),
    'fire': _wall((226, 96, 64), amber=(255, 140, 40), amberhi=(255, 210, 90), ambersh=(170, 60, 30),
                  moss=(180, 60, 40), mosslt=(255, 150, 60)),
    'ice': _wall((72, 150, 204), amber=(130, 220, 250), amberhi=(186, 236, 250), ambersh=(50, 130, 200),
                 moss=(150, 220, 250), mosslt=(180, 236, 250)),
    'electric': _wall((148, 124, 190), amber=(255, 215, 60), amberhi=(255, 245, 150), ambersh=(180, 130, 30),
                      moss=(255, 215, 60), mosslt=(255, 240, 140)),
    'poison': _wall((160, 206, 92), amber=(180, 90, 190), amberhi=(230, 150, 230), ambersh=(100, 50, 110),
                    moss=(90, 150, 40), mosslt=(140, 200, 60)),
}
