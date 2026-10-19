"""VARCO Sound 참조 이미지 생성(2026-10-08).

docs/03_아트/사운드_제작_목록.md의 사운드마다 게임 실제 도트 그림을 조합한 참조 이미지를 만든다.
출력: Art_Source/sound/ref/<사운드 파일 이름>.png (배경음악 768x432, 효과음 512x512).
실행: python Art_Source/sound/make_sound_refs.py  (프로젝트 루트에서)
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageEnhance

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Art_Source', 'sound', 'ref')
A = lambda *p: os.path.join(ROOT, 'Assets', *p)

random.seed(7)
_cache = {}


def load(path):
    if path not in _cache:
        _cache[path] = Image.open(path).convert('RGBA')
    return _cache[path].copy()


def char(name):
    return load(A('Art', 'Characters', name + '.png'))


def mon(name):
    return load(A('Art', 'Monsters', 'monster_' + name + '.png'))


def boss(name):
    return load(A('Resources', 'Bosses', 'boss_' + name + '.png'))


def fx(name):
    return load(A('Resources', 'Fx', name + '.png'))


def tal(name):
    return load(A('Art', 'Characters', 'Tal', 'tal_' + name + '.png'))


SLIME = lambda: char('player_slime')
SWORD = lambda: load(A('Art', 'Characters', 'Sword', 'weapon_sword.png'))
SLASH = lambda i=2: load(A('Art', 'Characters', 'Sword', 'sword_slash_%d.png' % i))


def kit(name):
    return load(A('Art', 'UI', 'Kit', name + '.png'))


def canvas(size, color):
    return Image.new('RGBA', size, color + (255,))


def tiled(size, theme, dark=0.45):
    """층 속성 타일셋 일부를 3배로 깔아 바닥처럼."""
    src = load(A('Art', 'Tiles', 'tileset_%s_32.png' % theme)).crop((0, 0, 160, 160))
    src = src.resize((480, 480), Image.NEAREST)
    img = Image.new('RGBA', size, (0, 0, 0, 255))
    for x in range(0, size[0], 480):
        for y in range(0, size[1], 480):
            img.alpha_composite(src, (x, y))
    return ImageEnhance.Brightness(img).enhance(dark)


def put(img, sprite, center, scale, flip=False, rotate=0, alpha=1.0, tint=None, dark=None):
    s = sprite
    if flip:
        s = s.transpose(Image.FLIP_LEFT_RIGHT)
    s = s.resize((max(1, int(s.width * scale)), max(1, int(s.height * scale))), Image.NEAREST)
    if rotate:
        s = s.rotate(rotate, resample=Image.NEAREST, expand=True)
    if tint:
        solid = Image.new('RGBA', s.size, tint + (255,))
        solid.putalpha(s.getchannel('A'))
        s = Image.blend(s, solid, 0.55)
    if dark is not None:
        s = ImageEnhance.Brightness(s).enhance(dark)
    if alpha < 1.0:
        a = s.getchannel('A').point(lambda v: int(v * alpha))
        s.putalpha(a)
    img.alpha_composite(s, (int(center[0] - s.width / 2), int(center[1] - s.height / 2)))


def rings(img, center, radii, color, width=6):
    d = ImageDraw.Draw(img)
    for r in radii:
        d.ellipse((center[0] - r, center[1] - r, center[0] + r, center[1] + r), outline=color + (255,), width=width)


def streaks(img, center, direction, count, length, color, spread=1.2):
    """spread: 진행 방향에 수직으로 퍼지는 폭(100px 단위)."""
    d = ImageDraw.Draw(img)
    dx, dy = direction
    for _ in range(count):
        off = random.uniform(-1, 1) * spread * 100
        ox, oy = off * -dy, off * dx
        sx, sy = center[0] + ox - dx * random.uniform(40, 90), center[1] + oy - dy * random.uniform(40, 90)
        d.line((sx, sy, sx - dx * length, sy - dy * length), fill=color + (255,), width=6)


def mark(img, center, text_color=(255, 70, 60)):
    """느낌표 경고 표시(도트풍 사각형)."""
    d = ImageDraw.Draw(img)
    x, y = center
    d.rectangle((x - 12, y - 60, x + 12, y), fill=text_color + (255,))
    d.rectangle((x - 12, y + 14, x + 12, y + 38), fill=text_color + (255,))


def rock(img, center, r):
    """뱉을 수 있는 돌 파편(도트풍 다각형)."""
    d = ImageDraw.Draw(img)
    x, y = center
    pts = [(x - r, y), (x - r * 0.5, y - r * 0.8), (x + r * 0.6, y - r * 0.7), (x + r, y + r * 0.1), (x + r * 0.4, y + r * 0.8), (x - r * 0.6, y + r * 0.7)]
    d.polygon(pts, fill=(140, 120, 100, 255), outline=(40, 30, 25, 255), width=6)
    d.polygon([(x - r * 0.4, y - r * 0.5), (x + r * 0.3, y - r * 0.45), (x, y)], fill=(180, 160, 135, 255))


def burst(img, center, r0, r1, count, color, width=8):
    """충격선: 중심에서 바깥으로 뻗는 굵은 선(맞는 순간을 분명히, 2026-10-14)."""
    d = ImageDraw.Draw(img)
    for i in range(count):
        a = math.radians(i * 360 / count + random.uniform(-8, 8))
        d.line((center[0] + math.cos(a) * r0, center[1] + math.sin(a) * r0,
                center[0] + math.cos(a) * r1, center[1] + math.sin(a) * r1), fill=color + (255,), width=width)


def swing_arc(img, center, radius, start, end, color, width=26):
    """대검 휘두르기 궤적: 굵은 초승달 + 안쪽 밝은 띠."""
    d = ImageDraw.Draw(img)
    box = (center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius)
    d.arc(box, start, end, fill=color + (255,), width=width)
    inner = (box[0] + width * 0.6, box[1] + width * 0.6, box[2] - width * 0.6, box[3] - width * 0.6)
    d.arc(inner, start + 10, end - 4, fill=(255, 255, 255, 255), width=max(4, width // 3))


def cursor(img, tip, scale=6):
    """도트 마우스 화살표(끝이 tip)."""
    pts = [(0, 0), (0, 12), (3, 9), (5, 14), (7, 13), (5, 8), (9, 8)]
    d = ImageDraw.Draw(img)
    poly = [(tip[0] + x * scale, tip[1] + y * scale) for x, y in pts]
    d.polygon(poly, fill=(255, 255, 255, 255), outline=(20, 20, 30, 255), width=4)


def cracks(img, center, count, length, color=(40, 28, 20)):
    """땅 갈라짐(지그재그 선)."""
    d = ImageDraw.Draw(img)
    for i in range(count):
        a = math.radians(i * 360 / count + random.uniform(-15, 15))
        x, y = center
        pts = [(x, y)]
        for _ in range(4):
            a += math.radians(random.uniform(-30, 30))
            x += math.cos(a) * length / 4
            y += math.sin(a) * length / 4 * 0.55
            pts.append((x, y))
        d.line(pts, fill=color + (255,), width=7)


def squash(sprite, sx, sy):
    """납작하게(쓰러짐)."""
    return sprite.resize((max(1, int(sprite.width * sx)), max(1, int(sprite.height * sy))), Image.NEAREST)


def sparkle(img, area, count, name='emit_star', scale=4):
    x0, y0, x1, y1 = area
    for _ in range(count):
        put(img, fx(name), (random.uniform(x0, x1), random.uniform(y0, y1)), scale * random.uniform(0.7, 1.2))


BGM = (768, 432)
SFX = (512, 512)
C = (256, 256)

refs = {}


def ref(name, size=SFX):
    def deco(fn):
        refs[name] = (fn, size)
        return fn
    return deco


# ── 배경음악 ─────────────────────────────────────────
@ref('bgm_title', BGM)
def _(img):
    bg = load(A('Art', 'UI', 'title_bg_pixel.png')).resize(BGM, Image.NEAREST)
    img.alpha_composite(bg)
    put(img, SLIME(), (384, 300), 5)


@ref('bgm_dungeon', BGM)
def _(img):
    img.alpha_composite(tiled(BGM, 'earth', 0.75))
    put(img, SLIME(), (330, 250), 5)
    put(img, SWORD(), (395, 230), 4, rotate=-35)
    put(img, mon('rat'), (560, 200), 4, flip=True)
    put(img, mon('rooster'), (620, 320), 4, flip=True)
    put(img, mon('dog'), (150, 330), 4)
    put(img, fx('hit_base_2'), (540, 200), 5)


@ref('bgm_boss', BGM)
def _(img):
    img.alpha_composite(tiled(BGM, 'fire', 0.45))
    put(img, boss('tiger'), (470, 200), 3.6, flip=True)
    put(img, SLIME(), (200, 320), 4)
    put(img, SWORD(), (255, 300), 3.5, rotate=-35)
    put(img, fx('hit_gold_2'), (420, 230), 6)


@ref('bgm_boss_final', BGM)
def _(img):
    img.alpha_composite(tiled(BGM, 'electric', 0.3))
    put(img, fx('portal_vortex_final_1'), (384, 190), 4, alpha=0.5)
    put(img, boss('dragon'), (384, 190), 4.2)
    put(img, SLIME(), (170, 360), 3.5)
    put(img, tal('tiger'), (600, 360), 3.5, flip=True)
    sparkle(img, (60, 30, 708, 400), 10, 'mote_electric', 5)


@ref('jingle_victory', BGM)
def _(img):
    img.alpha_composite(canvas(BGM, (250, 205, 110)))
    put(img, fx('portal_vortex_final_2'), (384, 200), 5)
    put(img, SLIME(), (384, 300), 5)
    sparkle(img, (100, 40, 668, 400), 18)


@ref('jingle_defeat', BGM)
def _(img):
    # 2026-10-14 다시 만듦: 거의 검은 화면 → 어두운 던전 + 위에서 내리는 희미한 빛 + 납작하게 쓰러진 슬라임·떨어진 대검·어지러운 별
    img.alpha_composite(tiled(BGM, 'ice', 0.28))
    light = Image.new('RGBA', BGM, (0, 0, 0, 0))
    ImageDraw.Draw(light).polygon([(330, 0), (438, 0), (560, 432), (210, 432)], fill=(170, 190, 230, 45))
    img.alpha_composite(light)
    put(img, squash(SLIME(), 1.35, 0.55), (384, 330), 7, dark=0.65)
    put(img, SWORD(), (560, 360), 5, rotate=95, dark=0.6)
    for a in range(0, 360, 72):
        put(img, fx('emit_star'), (384 + math.cos(math.radians(a)) * 80, 250 + math.sin(math.radians(a)) * 22), 4, alpha=0.8)


# ── 플레이어 ─────────────────────────────────────────
@ref('sfx_sword_swing')
def _(img):
    # 2026-10-14 다시 만듦: 칼이 작고 궤적이 없어 '서 있는' 장면이었다 → 크게 기울인 대검 + 굵은 휘두르기 궤적 + 바람선(맞는 대상 없음)
    img.alpha_composite(tiled(SFX, 'earth', 0.5))
    swing_arc(img, (200, 300), 210, 240, 350, (220, 235, 255), 30)
    streaks(img, (380, 210), (0.6, 0.8), 5, 90, (230, 240, 255), 0.6)
    put(img, SLIME(), (170, 330), 7)
    put(img, SWORD(), (330, 200), 7, rotate=-60)


def hit_scene(img, monster, impact, ring_color, monster_scale=8):
    """대검이 몬스터에 닿는 순간: 왼쪽 슬라임+대검, 오른쪽 하얗게 번쩍인 몬스터, 맞닿은 지점에 충격(몬스터 몸 위가 아님)."""
    put(img, SLIME(), (110, 330), 6)
    put(img, SWORD(), (215, 285), 6, rotate=-80)
    put(img, mon(monster), (405, 300), monster_scale, flip=True, rotate=-10)
    contact = (290, 270)
    burst(img, contact, 34, 105, 10, ring_color, 9)
    put(img, fx(impact), contact, 8)


@ref('sfx_hit')
def _(img):
    # 2026-10-14 다시 만듦: 불꽃이 몸 무늬처럼 보여 '맞는 순간'이 안 읽혔다
    img.alpha_composite(tiled(SFX, 'earth', 0.5))
    hit_scene(img, 'rat', 'hit_base_1', (255, 245, 210))


@ref('sfx_hit_strong')
def _(img):
    # 2026-10-14 다시 만듦: 강타 = 더 큰 금빛 충격 + 화면 흔들림 잔상
    img.alpha_composite(tiled(SFX, 'earth', 0.45))
    put(img, mon('ox'), (430, 300), 8, flip=True, alpha=0.3)  # 흔들림 잔상
    hit_scene(img, 'ox', 'hit_gold_1', (255, 210, 90))
    burst(img, (285, 275), 130, 200, 14, (255, 200, 80), 6)


@ref('sfx_guard')
def _(img):
    img.alpha_composite(tiled(SFX, 'ice', 0.45))
    put(img, SLIME(), (170, 300), 6)
    put(img, SWORD(), (250, 260), 5)
    put(img, mon('dog'), (390, 280), 6, flip=True)
    put(img, fx('hit_base_1'), (270, 230), 8)


@ref('sfx_dash')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.5))
    for i, a in enumerate((0.15, 0.3, 0.5)):
        put(img, SLIME(), (120 + i * 70, 280), 6, alpha=a)
    streaks(img, (330, 280), (1, 0), 6, 140, (230, 240, 255), 0.5)
    put(img, SLIME(), (350, 280), 6)


@ref('sfx_player_hurt')
def _(img):
    img.alpha_composite(tiled(SFX, 'fire', 0.45))
    put(img, SLIME(), (240, 280), 8, tint=(255, 80, 80))
    put(img, fx('hit_base_2'), (300, 230), 7)


@ref('sfx_player_down')
def _(img):
    # 2026-10-14 다시 만듦: 어두운 슬라임만 있어 동작이 안 보였다 → 납작하게 퍼진 슬라임 + 머리 위 어지러운 별 + 먼지 + 떨어진 대검
    img.alpha_composite(tiled(SFX, 'earth', 0.45))
    put(img, squash(SLIME(), 1.5, 0.5), (256, 340), 8, dark=0.8)
    for a in range(0, 360, 60):
        put(img, fx('emit_star'), (256 + math.cos(math.radians(a)) * 95, 230 + math.sin(math.radians(a)) * 28), 5)
    put(img, fx('absorb_puff_2'), (140, 370), 5, alpha=0.6)
    put(img, fx('absorb_puff_2'), (372, 370), 5, alpha=0.6)
    put(img, SWORD(), (400, 420), 5, rotate=100)


@ref('sfx_revive_loop')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.35))
    put(img, SLIME(), (256, 320), 7, dark=0.45)
    rings(img, (256, 320), (150, 120), (140, 255, 170), 5)
    sparkle(img, (130, 120, 380, 380), 8, 'emit_star', 4)


@ref('sfx_revive_done')
def _(img):
    img.alpha_composite(canvas(SFX, (60, 120, 80)))
    rings(img, (256, 280), (200, 160), (220, 255, 200), 8)
    put(img, SLIME(), (256, 280), 8)
    sparkle(img, (60, 60, 450, 450), 16, 'emit_star', 5)


# ── 흡수 ─────────────────────────────────────────────
@ref('sfx_absorb_hold')
def _(img):
    img.alpha_composite(tiled(SFX, 'poison', 0.4))
    put(img, SLIME(), (150, 280), 7)
    streaks(img, (330, 280), (-1, 0), 8, 120, (200, 240, 255), 0.5)
    for i in range(6):
        put(img, fx('absorb_pellet_%d' % (i % 2)), (230 + i * 35, 280 + random.randint(-40, 40)), 6)
    put(img, mon('rabbit'), (420, 280), 6, flip=True, alpha=0.7)


@ref('sfx_absorb_success')
def _(img):
    img.alpha_composite(tiled(SFX, 'poison', 0.4))
    put(img, SLIME(), C, 9)
    put(img, fx('absorb_puff_1'), (256, 150), 6, alpha=0.5)


@ref('sfx_trait_equip')
def _(img):
    img.alpha_composite(canvas(SFX, (70, 60, 110)))
    put(img, tal('dog'), C, 9)
    put(img, fx('absorb_puff_2'), (256, 150), 7, alpha=0.5)


@ref('sfx_trait_lost')
def _(img):
    img.alpha_composite(tiled(SFX, 'fire', 0.4))
    put(img, SLIME(), (230, 320), 7)
    put(img, tal('tiger'), (330, 150), 5, rotate=25, alpha=0.8)
    sparkle(img, (200, 80, 450, 260), 6)


@ref('sfx_debris_grab')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.45))
    put(img, SLIME(), (220, 280), 8)
    rock(img, (345, 270), 50)
    streaks(img, (380, 270), (-1, 0), 4, 60, (230, 230, 230), 0.3)


@ref('sfx_debris_spit')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.45))
    put(img, SLIME(), (150, 280), 8)
    rock(img, (400, 270), 50)
    streaks(img, (340, 270), (1, 0), 5, 110, (255, 240, 200), 0.3)


# ── 속성 ─────────────────────────────────────────────
for elem, monster, status, bgc in [
    ('fire', 'rooster', 'status_flame_1', (90, 30, 20)),
    ('ice', 'sheep', 'status_frost_2', (30, 60, 100)),
    ('electric', 'rat', 'status_zap_1', (70, 70, 20)),
    ('poison', 'snake', 'status_bubble_2', (40, 70, 30)),
    ('earth', 'ox', 'status_star_earth', (80, 55, 30)),
]:
    def make(img, elem=elem, monster=monster, status=status, bgc=bgc):
        img.alpha_composite(tiled(SFX, elem, 0.4))
        put(img, mon(monster), (256, 290), 8)
        put(img, fx(status), (256, 290), 8, alpha=0.9)
        put(img, fx('hit_%s_2' % elem), (256, 250), 10)
        sparkle(img, (90, 90, 420, 420), 6, 'mote_' + elem, 6)
    refs['sfx_elem_' + elem] = (make, SFX)


@ref('sfx_elem_earth')
def _(img):
    # 2026-10-14 다시 만듦: 효과 표시가 작아 흐렸다 → 발밑 땅이 갈라지고 돌이 튀어 오르며, 머리 위 큰 경직 별
    img.alpha_composite(tiled(SFX, 'earth', 0.45))
    cracks(img, (256, 420), 7, 230)
    put(img, mon('ox'), (256, 300), 8)
    for x, y, r in [(90, 360, 26), (425, 350, 30), (110, 230, 18), (410, 215, 20)]:
        rock(img, (x, y), r)
    put(img, fx('hit_earth_2'), (256, 425), 9)
    put(img, fx('status_star_earth'), (256, 140), 13)


# ── 몬스터 ───────────────────────────────────────────
@ref('sfx_monster_hurt')
def _(img):
    # 2026-10-14 다시 만듦: 표시가 작아 흐렸다 → 맞아서 뒤로 튕겨 나가는 토끼(기울어짐·뒤로 밀리는 선·통증 별), 대검은 빠짐(맞은 쪽 반응이 주인공)
    img.alpha_composite(tiled(SFX, 'earth', 0.5))
    streaks(img, (200, 280), (-1, 0), 6, 110, (240, 240, 240), 0.8)
    put(img, mon('rabbit'), (290, 290), 9, rotate=-18, tint=(255, 200, 200))
    put(img, fx('hit_base_1'), (200, 260), 8)
    for x, y in [(330, 140), (380, 175), (285, 125)]:
        put(img, fx('emit_star'), (x, y), 4)


@ref('sfx_monster_die')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.45))
    put(img, mon('sheep'), C, 9, alpha=0.4)
    put(img, fx('absorb_puff_3'), (256, 200), 7, alpha=0.5)


@ref('sfx_monster_warn')
def _(img):
    img.alpha_composite(tiled(SFX, 'fire', 0.4))
    put(img, mon('tiger'), (256, 310), 8)
    mark(img, (256, 130))


@ref('sfx_dog_bark')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.5))
    put(img, mon('dog'), (220, 300), 9)
    rings(img, (340, 220), (40, 70, 100), (255, 255, 255), 6)


@ref('sfx_dragon_breath')
def _(img):
    img.alpha_composite(tiled(SFX, 'fire', 0.4))
    put(img, mon('dragon'), (130, 260), 7)
    for i in range(14):
        t = i / 13
        put(img, fx('hit_fire_%d' % (i % 4)), (220 + t * 250, 260 + random.uniform(-1, 1) * t * 120), 4 + t * 4)


@ref('sfx_shield_block')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.45))
    put(img, mon('pig'), (330, 280), 8, flip=True)
    put(img, SWORD(), (190, 250), 5, rotate=-50)
    put(img, fx('hit_base_2'), (250, 250), 8)


# ── 보스 ─────────────────────────────────────────────
@ref('sfx_boss_roar')
def _(img):
    img.alpha_composite(tiled(SFX, 'fire', 0.35))
    rings(img, (256, 240), (190, 230), (255, 220, 160), 7)
    put(img, boss('tiger'), (256, 260), 4.4)


@ref('sfx_boss_charge')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.4))
    streaks(img, (300, 260), (1, 0), 10, 160, (240, 220, 190), 0.9)
    put(img, boss('ox'), (330, 260), 4)


@ref('sfx_boss_wall_hit')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.4))
    d = ImageDraw.Draw(img)
    d.rectangle((420, 0, 512, 512), fill=(70, 60, 55, 255))
    put(img, boss('ox'), (290, 270), 4)
    for i in range(6):
        put(img, fx('hit_earth_%d' % (i % 4)), (420, 160 + i * 40), 6)
    put(img, fx('status_star_earth'), (290, 120), 6)


@ref('sfx_boss_slam')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.35))
    rings(img, (256, 330), (120, 180, 240), (230, 190, 120), 8)
    put(img, boss('monkey'), (256, 240), 4)
    for a in range(0, 360, 45):
        r = 200
        put(img, fx('hit_earth_2'), (256 + math.cos(math.radians(a)) * r, 330 + math.sin(math.radians(a)) * r * 0.5), 6)


@ref('sfx_boss_phase')
def _(img):
    img.alpha_composite(canvas(SFX, (60, 10, 15)))
    rings(img, C, (210, 170), (255, 60, 60), 8)
    put(img, boss('dragon'), C, 4.4, tint=(255, 60, 60))
    sparkle(img, (40, 40, 470, 470), 10, 'mote_fire', 6)


@ref('sfx_boss_down')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.3))
    put(img, boss('horse'), (256, 300), 4.2, rotate=90, dark=0.5)
    put(img, fx('absorb_puff_2'), (256, 170), 7, alpha=0.45)


# ── 던전 ─────────────────────────────────────────────
@ref('sfx_door_lock')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.5))
    d = ImageDraw.Draw(img)
    d.rectangle((150, 80, 362, 470), fill=(80, 70, 65, 255), outline=(30, 25, 25, 255), width=10)
    for y in range(130, 470, 70):
        d.line((150, y, 362, y), fill=(50, 45, 40, 255), width=6)
    rings(img, (256, 270), (70,), (200, 120, 255), 10)
    put(img, fx('hit_base_2'), (256, 270), 6)


@ref('sfx_room_clear')
def _(img):
    img.alpha_composite(tiled(SFX, 'earth', 0.6))
    put(img, SLIME(), (256, 300), 7)
    sparkle(img, (60, 60, 450, 260), 18)


@ref('sfx_portal_open')
def _(img):
    img.alpha_composite(tiled(SFX, 'electric', 0.35))
    put(img, fx('portal_vortex_normal_1'), (256, 250), 6.5)
    put(img, fx('portal_ring'), (256, 250), 6.5)


@ref('sfx_floor_enter')
def _(img):
    img.alpha_composite(canvas(SFX, (10, 8, 20)))
    put(img, fx('portal_vortex_normal_2'), (256, 230), 6.5)
    put(img, SLIME(), (256, 300), 5, alpha=0.6)
    streaks(img, (256, 260), (0, 1), 8, 120, (200, 170, 255), 1.3)


# ── UI ───────────────────────────────────────────────
def button(img, sprite, center, w, h):
    s = kit(sprite)
    put(img, s.resize((w // 4, h // 4), Image.NEAREST), center, 4)


@ref('sfx_ui_hover')
def _(img):
    # 2026-10-14 다시 만듦: 빈 버튼 틀뿐이었다 → 마우스 화살표가 올라간 버튼만 밝게 빛남(위아래 버튼은 어둡게)
    img.alpha_composite(canvas(SFX, (25, 22, 40)))
    button(img, 'button', (256, 120), 320, 80)
    glow = Image.new('RGBA', SFX, (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle((70, 210, 442, 302), 18, fill=(255, 230, 140, 70))
    img.alpha_composite(glow)
    button(img, 'button_hover', (256, 256), 320, 80)
    button(img, 'button', (256, 392), 320, 80)
    put(img, fx('emit_star'), (110, 230), 4)
    put(img, fx('emit_star'), (404, 282), 4)
    cursor(img, (300, 262), 6)


@ref('sfx_ui_click')
def _(img):
    # 2026-10-14 다시 만듦: 빈 버튼뿐이었다 → 화살표가 누른 버튼이 눌려 들어가고, 누른 자리에서 동그란 물결이 퍼짐
    img.alpha_composite(canvas(SFX, (25, 22, 40)))
    button(img, 'button_pressed', (256, 270), 320, 96)
    rings(img, (300, 268), (36, 64, 92), (255, 240, 190), 5)
    sparkle(img, (150, 190, 380, 340), 4)
    cursor(img, (300, 268), 6)


@ref('sfx_ui_open')
def _(img):
    img.alpha_composite(canvas(SFX, (25, 22, 40)))
    put(img, kit('panel'), C, 11)
    streaks(img, (256, 256), (0, 1), 6, 80, (200, 190, 230), 1.2)


@ref('sfx_ui_select')
def _(img):
    img.alpha_composite(canvas(SFX, (40, 30, 60)))
    put(img, kit('panel'), C, 11)
    put(img, fx('hit_fire_2'), (190, 256), 8)
    put(img, fx('hit_ice_2'), (322, 256), 8)
    sparkle(img, (100, 100, 410, 410), 10)


def main():
    import sys
    os.makedirs(OUT, exist_ok=True)
    only = set(sys.argv[1:])  # 이름을 주면 그것만 다시 만든다(예: python make_sound_refs.py sfx_hit sfx_ui_click)
    for name, (fn, size) in refs.items():
        if only and name not in only:
            continue
        img = canvas(size, (0, 0, 0))
        fn(img)
        img.convert('RGB').save(os.path.join(OUT, name + '.png'))
    print(len(only) if only else len(refs), 'images ->', OUT)


if __name__ == '__main__':
    main()
