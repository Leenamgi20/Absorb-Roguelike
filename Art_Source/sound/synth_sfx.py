"""레트로 효과음 합성(2026-10-08) — 커비 시리즈의 통통 튀는 레트로 효과음 분위기를 '오마주'한 자체 합성음.
원본 소리를 복제하지 않고 네모파·삼각파·노이즈 + 음높이 변화로 새로 만든다.
출력: Art_Source/sound/synth/<사운드 파일 이름>.wav (44.1kHz 모노 16bit) — 이름은 docs/03_아트/사운드_제작_목록.md와 같다.
실행: python Art_Source/sound/synth_sfx.py  (프로젝트 루트)
"""
import os

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, lfilter

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'synth')
rng = np.random.default_rng(3)


# ── 기본 재료 ─────────────────────────────────────────
def t_of(dur):
    return np.arange(int(SR * dur)) / SR


def sweep(f0, f1, dur, curve=1.0):
    """f0→f1로 음높이가 변하는 주파수 곡선(curve>1이면 끝에서 급하게)."""
    x = np.linspace(0, 1, int(SR * dur)) ** curve
    return f0 * (f1 / f0) ** x


def osc(freq, kind='square', duty=0.5):
    phase = np.cumsum(freq) / SR % 1.0
    if kind == 'square':
        return np.where(phase < duty, 1.0, -1.0)
    if kind == 'tri':
        return 4 * np.abs(phase - 0.5) - 1
    if kind == 'sine':
        return np.sin(2 * np.pi * phase)
    raise ValueError(kind)


def noise(dur):
    return rng.uniform(-1, 1, int(SR * dur))


def env(n, a=0.005, d=0.0, s=1.0, r=0.05):
    """간단 ADSR(초 단위). 길이 n 샘플."""
    e = np.ones(n) * s
    ai, di, ri = int(a * SR), int(d * SR), int(r * SR)
    ai = min(ai, n)
    e[:ai] = np.linspace(0, 1, ai)
    if di:
        e[ai:ai + di] = np.linspace(1, s, len(e[ai:ai + di]))
    if ri:
        e[-ri:] *= np.linspace(1, 0, ri)
    return e


def lowpass(x, cutoff, order=2):
    b, a = butter(order, cutoff / (SR / 2), 'low')
    return lfilter(b, a, x)


def bandpass(x, lo, hi, order=2):
    b, a = butter(order, [lo / (SR / 2), hi / (SR / 2)], 'band')
    return lfilter(b, a, x)


def vibrato(freq, rate, depth):
    t = np.arange(len(freq)) / SR
    return freq * (1 + depth * np.sin(2 * np.pi * rate * t))


def seq(*parts, gap=0.0):
    out = []
    for p in parts:
        out.append(p)
        if gap:
            out.append(np.zeros(int(SR * gap)))
    return np.concatenate(out)


def mix(*layers):
    n = max(len(l) for l in layers)
    out = np.zeros(n)
    for l in layers:
        out[:len(l)] += l
    return out


def note(f, dur, kind='square', duty=0.5, vol=1.0, r=0.03):
    x = osc(np.full(int(SR * dur), f), kind, duty)
    return x * env(len(x), r=r) * vol


def declick(x, fade_in=0.001, fade_out=0.006):
    """시작·끝 클릭 방지(2026-10-14). 앞은 1ms만 — 앞 무음 없이 바로 시작."""
    x = np.array(x, dtype=float)
    i, o = min(int(SR * fade_in), len(x)), min(int(SR * fade_out), len(x))
    if i:
        x[:i] *= np.linspace(0, 1, i)
    if o:
        x[-o:] *= np.linspace(1, 0, o)
    return x


def save(name, x, peak_db=-1.0, loop=False):
    """정규화(-1dBFS) 후 저장. 반복용(loop)은 이음매가 깨지지 않게 앞뒤 페이드를 하지 않는다."""
    if not loop:
        x = declick(x)
    x = x / (np.max(np.abs(x)) + 1e-9) * 10 ** (peak_db / 20)
    os.makedirs(OUT, exist_ok=True)
    wavfile.write(os.path.join(OUT, name + '.wav'), SR, (x * 32767).astype(np.int16))


def loop_crossfade(x, fade=0.08):
    """끝 fade초를 처음과 겹쳐 이음매 없는 반복으로."""
    f = int(SR * fade)
    head, body, tail = x[:f], x[f:-f], x[-f:]
    ramp = np.linspace(0, 1, f)
    return np.concatenate([tail * (1 - ramp) + head * ramp, body])


# ── 흡수 ─────────────────────────────────────────────
def absorb_hold():
    """빨아들이는 바람(반복 1초): 대역 노이즈 + 4Hz 울렁임 + 낮게 떨리는 네모파."""
    d = 1.08
    t = t_of(d)
    wind = bandpass(noise(d), 500, 2600) * (0.65 + 0.35 * np.sin(2 * np.pi * 4 * t))
    hiss = bandpass(noise(d), 2500, 6000) * 0.25 * (0.5 + 0.5 * np.sin(2 * np.pi * 8 * t + 1))
    hum = osc(vibrato(np.full(len(t), 110.0), 8, 0.06), 'square', 0.3)
    hum = lowpass(hum, 900) * 0.18
    return loop_crossfade(wind + hiss + hum)


def absorb_success():
    """꿀꺽: 아래로 떨어지는 통통한 삼각파 '굴' + 짧은 '꺽' 팝."""
    g1 = osc(sweep(520, 140, 0.16, 1.4), 'tri')
    g1 *= env(len(g1), a=0.003, r=0.04)
    g2 = osc(sweep(300, 90, 0.12, 1.2), 'square', 0.25)
    g2 = lowpass(g2, 1800) * env(len(g2), a=0.002, r=0.05) * 0.6
    pop = osc(sweep(900, 1500, 0.04), 'square', 0.5) * env(int(SR * 0.04), r=0.02) * 0.35
    return seq(g1, np.zeros(int(SR * 0.02)), g2, pop)


def trait_equip():
    """탈 쓰기 '퐁': 짧게 솟는 팝 + 위로 올라가는 반짝 아르페지오."""
    pop = osc(sweep(250, 900, 0.07, 0.6), 'square', 0.4) * env(int(SR * 0.07), r=0.03)
    arp = seq(*[note(f, 0.045, 'square', 0.25, 0.55, r=0.02) for f in (1046, 1318, 1568, 2093)])
    puff = lowpass(noise(0.12), 2500) * env(int(SR * 0.12), a=0.002, r=0.1) * 0.35
    return mix(seq(pop, arp), puff)


def trait_lost():
    """특성 잃음: 내려가는 아르페지오 + 김 빠지는 노이즈."""
    arp = seq(*[note(f, 0.06, 'square', 0.5, 0.6, r=0.025) for f in (1568, 1318, 1046, 784, 523)])
    deflate = bandpass(noise(0.35), 600, 3000) * env(int(SR * 0.35), a=0.01, r=0.3) * 0.4
    return mix(arp, np.concatenate([np.zeros(int(SR * 0.08)), deflate]))


def debris_grab():
    """파편 머금기: 짧은 '쭙' 슬러프 + 작은 블립."""
    slurp = bandpass(noise(0.12), 400, 2200)
    slurp *= np.linspace(1, 0.2, len(slurp)) * env(len(slurp), a=0.004, r=0.03)
    blip = osc(sweep(700, 350, 0.06), 'tri') * env(int(SR * 0.06), r=0.03) * 0.8
    return seq(slurp, blip)


def debris_spit():
    """파편 뱉기 '퉤': 아래로 꺾이는 네모파 + 휙 노이즈."""
    pt = osc(sweep(900, 220, 0.09, 0.7), 'square', 0.35) * env(int(SR * 0.09), a=0.001, r=0.03)
    whoosh = bandpass(noise(0.18), 800, 4000) * env(int(SR * 0.18), a=0.005, r=0.15) * 0.5
    return mix(pt, whoosh)


# ── 몇 가지 더(샘플) ─────────────────────────────────
def dash():
    """대시: 짧은 휙(밝은 노이즈 스윕)."""
    d = 0.22
    x = noise(d)
    lo = bandpass(x, 1200, 5000)
    return lo * env(len(lo), a=0.005, r=0.16) * np.linspace(1, 0.4, len(lo))


def player_hurt():
    """슬라임 피격: '뿌잉' — 떨림 있는 하강 네모파."""
    f = vibrato(sweep(700, 260, 0.22), 30, 0.08)
    return osc(f, 'square', 0.4) * env(len(f), a=0.002, r=0.08)


def revive_done():
    """부활 완료: 밝게 올라가는 아르페지오 + 마지막 음 길게."""
    notes = [note(f, 0.07, 'square', 0.25, 0.7, r=0.02) for f in (523, 659, 784, 1046)]
    last = osc(vibrato(np.full(int(SR * 0.3), 1318.0), 6, 0.01), 'square', 0.25) * env(int(SR * 0.3), r=0.25) * 0.7
    return seq(*notes, last)


def ui_click():
    # 2026-10-14: 목록 길이(0.15초)에 맞춰 작은 메아리 한 번을 붙임(앞 0.05초는 그대로)
    c = osc(sweep(1200, 1800, 0.05), 'square', 0.25) * env(int(SR * 0.05), a=0.001, r=0.03)
    return seq(c, np.zeros(int(SR * 0.02)), c * 0.3)


def ui_hover():
    # 2026-10-14: 목록 길이(0.1초)에 맞춰 0.03→0.06초, 끝을 부드럽게
    return osc(np.full(int(SR * 0.06), 2400.0), 'square', 0.25) * env(int(SR * 0.06), a=0.001, r=0.045) * 0.5


def room_clear():
    """방 클리어: 짧은 상승 징글."""
    return seq(*[note(f, d, 'square', 0.5, 0.7, r=0.03) for f, d in ((784, 0.08), (988, 0.08), (1175, 0.08), (1568, 0.25))])


SOUNDS = {
    'sfx_absorb_hold': absorb_hold,
    'sfx_absorb_success': absorb_success,
    'sfx_trait_equip': trait_equip,
    'sfx_trait_lost': trait_lost,
    'sfx_debris_grab': debris_grab,
    'sfx_debris_spit': debris_spit,
    'sfx_dash': dash,
    'sfx_player_hurt': player_hurt,
    'sfx_revive_done': revive_done,
    'sfx_ui_click': ui_click,
    'sfx_ui_hover': ui_hover,
    'sfx_room_clear': room_clear,
}

# ══ 나머지 28개(2026-10-14) ══════════════════════════════
# 밝고 가벼운 카툰·레트로 톤. 어두운 저역·진득한 소리는 쓰지 않는다(바닥음도 짧게, 대부분 150Hz 이상).
# 각 함수의 p = 음높이 배수(변형 _1/_2/_3에서 조금씩 다르게), 노이즈 시드는 변형마다 다시 정한다.
def decay(n, tau):
    """지수 감쇠 곡선(tau초에 1/e)."""
    return np.exp(-np.arange(n) / (SR * tau))


def chime(freqs, dur, tau=0.15, vol=1.0):
    """종·쇠 소리: 비정수배 사인 묶음 + 지수 감쇠."""
    n = int(SR * dur)
    t = np.arange(n) / SR
    x = sum(np.sin(2 * np.pi * f * t) / (i + 1) for i, f in enumerate(freqs))
    return x * decay(n, tau) * env(n, a=0.001, r=0.02) * vol


def at(x, start):
    """start초 뒤에 시작하도록 앞에 무음."""
    return np.concatenate([np.zeros(int(SR * start)), x])


def fit(x, dur):
    """정확히 dur초로 자르거나 무음으로 채운다."""
    n = int(SR * dur)
    return x[:n] if len(x) >= n else np.concatenate([x, np.zeros(n - len(x))])


def sparkle(dur, count=6, lo=2400, hi=4200, vol=0.3):
    """반짝이: 높은 짧은 사인 핑을 흩뿌린다."""
    out = np.zeros(int(SR * dur))
    for _ in range(count):
        s = rng.uniform(0, max(dur - 0.08, 0.01))
        ping = chime([rng.uniform(lo, hi)], 0.08, 0.03, vol)
        i = int(SR * s)
        out[i:i + len(ping)] += ping[:len(out) - i]
    return out


# ── 플레이어 ──
def sword_swing(p=1.0):
    """대검 휘두르기: 위로 갔다 내려오는 밝은 '휙' + 살짝 날 선 고음."""
    d = 0.28
    x = noise(d)
    n = len(x)
    lo = bandpass(x, 1500 * p, 3500 * p) * np.sin(np.linspace(0, np.pi, n)) ** 1.5
    hi = bandpass(noise(d), 4000 * p, 8000) * np.sin(np.linspace(0, np.pi, n)) ** 3 * 0.35
    tone = osc(sweep(900 * p, 1500 * p, d, 0.8), 'tri') * np.sin(np.linspace(0, np.pi, n)) ** 2 * 0.15
    return (lo + hi + tone) * env(n, a=0.002, r=0.05)


def hit(p=1.0):
    """명중: 통통한 '팍' 팝 + 짧은 노이즈 딱."""
    pop = osc(sweep(950 * p, 280 * p, 0.12, 0.6), 'square', 0.4) * env(int(SR * 0.12), a=0.001, r=0.05)
    click = bandpass(noise(0.04), 1500, 6000) * decay(int(SR * 0.04), 0.012) * 0.8
    return fit(mix(lowpass(pop, 4000), click), 0.18)


def hit_strong(p=1.0):
    """강타: 묵직하지 않은 '퍽' + 위에 반짝 띠링."""
    pow_ = osc(sweep(700 * p, 170 * p, 0.16, 0.5), 'tri') * env(int(SR * 0.16), a=0.001, r=0.06)
    burst = bandpass(noise(0.08), 900, 5000) * decay(int(SR * 0.08), 0.025) * 0.7
    twinkle = seq(note(2093 * p, 0.05, 'square', 0.25, 0.35, r=0.02), note(2637 * p, 0.09, 'square', 0.25, 0.3, r=0.07))
    return fit(mix(pow_, burst, at(twinkle, 0.1)), 0.3)


def guard(p=1.0):
    """가드: 맑은 쇠 '팅'."""
    ting = chime([1900 * p, 2870 * p, 4150 * p, 5300 * p], 0.3, 0.09)
    tick = bandpass(noise(0.015), 3000, 9000) * 0.6
    return mix(ting, tick)


def player_down(p=1.0):
    """쓰러짐: 내려가는 슬라이드 휘슬 + 작은 '퉁'."""
    whistle = osc(vibrato(sweep(1700 * p, 380 * p, 0.62, 0.8), 7, 0.015), 'sine') * env(int(SR * 0.62), a=0.004, r=0.06) * 0.8
    thump = osc(sweep(260, 140, 0.14), 'tri') * decay(int(SR * 0.14), 0.05)
    puff = lowpass(noise(0.12), 2200) * decay(int(SR * 0.12), 0.04) * 0.3
    return fit(mix(whistle, at(mix(thump, puff), 0.62)), 0.8)


def revive_loop(p=1.0):
    """부활 진행 중(반복 1.5초): 부드럽게 반짝이는 마법 소리."""
    d = 1.58
    t = t_of(d)
    pad = sum(osc(vibrato(np.full(len(t), f), 5, 0.004), 'tri') for f in (784, 988, 1175)) / 3
    pad = pad * (0.75 + 0.25 * np.sin(2 * np.pi * t / d * 2)) * 0.35
    air = bandpass(noise(d), 4000, 9000) * 0.06
    return loop_crossfade(pad + air + sparkle(d, 10, 2600, 4600, 0.35))


# ── 속성 ──
def elem_fire(p=1.0):
    """화염: 솟는 '화륵' + 타닥 불꽃."""
    d = 0.4
    n = int(SR * d)
    fw = bandpass(noise(d), 700, 4500) * env(n, a=0.03, r=0.25) * np.linspace(1, 0.5, n)
    crack = np.zeros(n)
    for _ in range(9):
        i = int(rng.uniform(0.02, 0.33) * SR)
        c = bandpass(noise(0.01), 2500, 8000) * 0.8
        crack[i:i + len(c)] += c
    rise = osc(sweep(300, 700, d, 0.6), 'tri') * env(n, a=0.02, r=0.2) * 0.15
    return fw + crack + rise


def elem_ice(p=1.0):
    """냉기: '찡' 얼어붙는 소리 + 차르르 얼음 종."""
    freeze = bandpass(noise(0.12), 5000, 10000) * decay(int(SR * 0.12), 0.04) * 0.5
    bells = seq(*[chime([f, f * 2.76], 0.07, 0.05, 0.6) for f in (2637, 2093, 3136, 2637)])
    return fit(mix(freeze, at(bells, 0.02), at(chime([1568, 3920], 0.25, 0.08, 0.4), 0.14)), 0.4)


def elem_electric(p=1.0):
    """전기: 지직 '찌릿' — 빠르게 튀는 네모파 + 버즈."""
    d = 0.38
    n = int(SR * d)
    jump = np.repeat(rng.uniform(600, 2200, n // 400 + 1), 400)[:n]
    zap = osc(jump, 'square', 0.3) * 0.45
    buzz = osc(np.full(n, 120.0), 'square', 0.1) * 0.15
    buzz = bandpass(buzz, 800, 5000) * 2
    return (zap + buzz) * env(n, a=0.002, r=0.12) * (0.7 + 0.3 * (rng.uniform(0, 1, n) > 0.3))


def elem_poison(p=1.0):
    """독: 뽀글뽀글 거품(위로 튀는 짧은 사인)."""
    out = np.zeros(int(SR * 0.4))
    for k, s in enumerate((0.0, 0.07, 0.13, 0.21, 0.27)):
        f0 = rng.uniform(320, 520)
        b = osc(sweep(f0, f0 * 2.4, 0.06, 1.5), 'sine') * env(int(SR * 0.06), a=0.002, r=0.02) * (1 - k * 0.12)
        i = int(SR * s)
        out[i:i + len(b)] += b
    return out


def elem_earth(p=1.0):
    """대지: 돌 '똑딱 쿵' — 짧은 바위 부딪힘 + 작은 우르릉."""
    bonk = osc(sweep(520, 170, 0.12, 0.5), 'tri') * env(int(SR * 0.12), a=0.001, r=0.05)
    knock = bandpass(noise(0.05), 800, 3500) * decay(int(SR * 0.05), 0.015) * 0.8
    rumble = lowpass(noise(0.3), 500) * env(int(SR * 0.3), a=0.01, r=0.2) * 0.5
    pebble = at(bandpass(noise(0.03), 1500, 4000) * 0.4, 0.16)
    return fit(mix(bonk, knock, at(rumble, 0.05), pebble), 0.4)


# ── 몬스터 ──
def monster_hurt(p=1.0):
    """잡몹 피격: 작은 '끽' 찍찍."""
    f = np.concatenate([sweep(1100 * p, 1900 * p, 0.06), sweep(1900 * p, 1200 * p, 0.1)])
    return fit(osc(f, 'square', 0.3) * env(len(f), a=0.001, r=0.04) * 0.8, 0.18)


def monster_die(p=1.0):
    """잡몹 사망: '펑' 연기 + 올라가는 작은 블립."""
    poof = lowpass(noise(0.4), 2500) * env(int(SR * 0.4), a=0.003, r=0.3) * decay(int(SR * 0.4), 0.15)
    blip = osc(sweep(600, 1500, 0.1), 'square', 0.25) * env(int(SR * 0.1), r=0.03) * 0.45
    return fit(mix(poof, at(blip, 0.04), at(sparkle(0.3, 3, 2500, 3800, 0.25), 0.15)), 0.5)


def monster_warn(p=1.0):
    """공격 예고: '띵띵!' 느낌표 블립 두 번."""
    return fit(seq(note(1568, 0.06, 'square', 0.25, 0.7, r=0.02), np.zeros(int(SR * 0.025)),
                   note(2093, 0.12, 'square', 0.25, 0.75, r=0.06)), 0.3)


def dog_bark(p=1.0):
    """개 짖기: 높은 강아지 '왕!' (모음 필터 + 하강 음)."""
    f = sweep(820, 560, 0.16, 0.7)
    body = osc(vibrato(f, 40, 0.03), 'square', 0.35)
    body = bandpass(body, 600, 3200) * env(len(f), a=0.004, r=0.06) * 1.4
    breath = bandpass(noise(0.05), 1500, 5000) * decay(int(SR * 0.05), 0.02) * 0.4
    yip = osc(sweep(900, 700, 0.06), 'square', 0.35)
    yip = bandpass(yip, 600, 3200) * env(len(yip), a=0.002, r=0.04) * 0.5
    return fit(mix(body, breath, at(yip, 0.2)), 0.38)


def dragon_breath(p=1.0):
    """용 브레스: 1초 '화아악' — 밝은 불길이 부풀었다 잦아듦."""
    d = 1.0
    n = int(SR * d)
    swell = np.minimum(1, np.linspace(0, 6, n)) * np.linspace(1, 0.35, n) ** 0.7
    roar = bandpass(noise(d), 600, 4800) * swell
    sweep_ = bandpass(noise(d), 1500, 6000) * np.sin(np.linspace(0, np.pi, n)) * 0.3
    crack = np.zeros(n)
    for _ in range(14):
        i = int(rng.uniform(0.05, 0.85) * SR)
        c = bandpass(noise(0.008), 3000, 9000) * 0.6
        crack[i:i + len(c)] += c
    puff = osc(sweep(260, 520, 0.15, 0.6), 'tri') * env(int(SR * 0.15), a=0.002, r=0.08) * 0.35
    return (mix(roar, sweep_, crack, puff)) * env(n, a=0.004, r=0.2)


def shield_block(p=1.0):
    """방패에 막힘: 나무 '통' + '뿅' 튕김."""
    bonk = osc(sweep(620, 300, 0.07), 'tri') * env(int(SR * 0.07), a=0.001, r=0.03)
    wood = bandpass(noise(0.04), 600, 2500) * decay(int(SR * 0.04), 0.012) * 0.8
    boing = osc(vibrato(sweep(330, 700, 0.2, 0.7), 18, 0.05), 'sine') * env(int(SR * 0.2), a=0.004, r=0.08) * 0.6
    return fit(mix(bonk, wood, at(boing, 0.06)), 0.3)


# ── 보스 ──
def boss_roar(p=1.0):
    """보스 포효: 크지만 만화 같은 '크아아' — 중음 네모파 그르렁 + 숨."""
    d = 1.45
    n = int(SR * d)
    f = vibrato(np.concatenate([sweep(240, 330, 0.25), sweep(330, 260, d - 0.25)])[:n], 11, 0.05)
    growl = (1 + 0.5 * np.sin(2 * np.pi * 32 * t_of(d)[:n]))
    body = osc(f, 'square', 0.4) + 0.6 * osc(f * 1.5, 'square', 0.3)
    body = bandpass(body * growl, 350, 3600) * 0.6
    breath = bandpass(noise(d), 900, 4500)[:n] * 0.35
    return (body + breath) * env(n, a=0.04, r=0.45)


def boss_charge(p=1.0):
    """보스 돌진: 통통 튀는 묵직한(?) 발소리가 빨라짐 + 휙 바람."""
    d = 1.0
    out = np.zeros(int(SR * d))
    s = 0.0
    gap = 0.2
    while s < d - 0.1:
        step = osc(sweep(330, 150, 0.09, 0.6), 'tri') * env(int(SR * 0.09), a=0.001, r=0.04)
        step = mix(step, bandpass(noise(0.03), 800, 3000) * decay(int(SR * 0.03), 0.01) * 0.4)
        i = int(SR * s)
        out[i:i + len(step)] += step[:len(out) - i]
        s += gap
        gap = max(0.1, gap * 0.85)
    wind = bandpass(noise(d), 1200, 5000) * np.linspace(0.05, 0.4, int(SR * d))
    return (out + wind) * env(len(out), a=0.002, r=0.12)


def boss_wall_hit(p=1.0):
    """벽 충돌 경직: '쾅' + 빙글빙글 어지러운 워블."""
    crash = bandpass(noise(0.25), 500, 5000) * decay(int(SR * 0.25), 0.07)
    bonk = osc(sweep(420, 160, 0.16, 0.5), 'tri') * env(int(SR * 0.16), a=0.001, r=0.06)
    wob = osc(vibrato(np.full(int(SR * 0.55), 900.0), 9, 0.08), 'sine') * env(int(SR * 0.55), a=0.02, r=0.25) * 0.5
    wob2 = osc(vibrato(np.full(int(SR * 0.55), 1350.0), 9, 0.08), 'sine') * env(int(SR * 0.55), a=0.02, r=0.25) * 0.25
    return fit(mix(crash, bonk, at(wob + wob2, 0.22)), 0.8)


def boss_slam(p=1.0):
    """지진 강타: 통 튀는 '쿵' + 짧은 우르릉."""
    boom = osc(sweep(320, 110, 0.25, 0.5), 'tri') * env(int(SR * 0.25), a=0.001, r=0.1)
    hit_ = bandpass(noise(0.1), 600, 4000) * decay(int(SR * 0.1), 0.03) * 0.8
    rumble = lowpass(noise(0.8), 700) * env(int(SR * 0.8), a=0.02, r=0.5) * decay(int(SR * 0.8), 0.35) * 0.7
    bounce = osc(sweep(260, 420, 0.1), 'tri') * env(int(SR * 0.1), r=0.04) * 0.3
    debris = sparkle(0.6, 5, 1500, 3000, 0.15)
    return fit(mix(boom, hit_, at(rumble, 0.03), at(bounce, 0.28), at(debris, 0.2)), 1.0)


def boss_phase(p=1.0):
    """페이즈 전환: 위로 솟는 파워업 '슈우웅' + 밝은 징 한 번."""
    rise_d = 0.8
    n = int(SR * rise_d)
    up = osc(vibrato(sweep(220, 1200, rise_d, 1.6), 14, 0.02), 'square', 0.3) * np.linspace(0.2, 0.7, n)
    up = lowpass(up, 5000)
    whoosh = bandpass(noise(rise_d), 1000, 6000) * np.linspace(0, 0.6, n)
    gong = chime([520, 1210, 1740, 2470, 3300], 0.7, 0.3, 1.1)
    return fit(mix(up + whoosh, at(gong, rise_d - 0.02), at(sparkle(0.6, 6), rise_d)), 1.5)


def boss_down(p=1.0):
    """보스 쓰러짐: 길게 내려가는 휘슬 + 통 튀는 쿵 + 끝에 반짝."""
    wd = 1.1
    whistle = osc(vibrato(sweep(1800, 280, wd, 0.9), 6, 0.02), 'sine') * env(int(SR * wd), a=0.01, r=0.05) * 0.7
    thud = mix(osc(sweep(300, 130, 0.2), 'tri') * env(int(SR * 0.2), a=0.001, r=0.08),
               lowpass(noise(0.25), 1500) * decay(int(SR * 0.25), 0.08) * 0.6)
    bounce = osc(sweep(260, 150, 0.1), 'tri') * env(int(SR * 0.1), r=0.05) * 0.4
    shine = seq(*[note(f, 0.06, 'square', 0.25, 0.4, r=0.02) for f in (1568, 2093, 2637)],
                chime([3136, 4700], 0.3, 0.1, 0.5))
    return fit(mix(whistle, at(thud, wd), at(bounce, wd + 0.25), at(shine, wd + 0.42)), 2.0)


# ── 던전 ──
def door_lock(p=1.0):
    """문 잠김: '쾅' 문 닫힘 + 밝은 마법 '찰칵'."""
    slam = bandpass(noise(0.18), 400, 3500) * decay(int(SR * 0.18), 0.05)
    body = osc(sweep(360, 170, 0.15), 'tri') * env(int(SR * 0.15), a=0.001, r=0.06)
    click = seq(bandpass(noise(0.012), 3000, 9000), np.zeros(int(SR * 0.04)), bandpass(noise(0.012), 2500, 8000))
    lock = chime([1760, 2640, 3520], 0.4, 0.12, 0.6)
    return fit(mix(slam, body, at(click * 0.8, 0.2), at(lock, 0.26), at(sparkle(0.35, 4), 0.3)), 0.8)


def portal_open(p=1.0):
    """포탈 열림: 소용돌이치며 솟는 반짝 '슈와아'."""
    d = 1.5
    n = int(SR * d)
    swirl = osc(vibrato(sweep(330, 1320, d, 0.8), 7, 0.06), 'tri') * np.sin(np.linspace(0, np.pi, n)) ** 0.6 * 0.5
    swirl2 = osc(vibrato(sweep(495, 1980, d, 0.8), 7.5, 0.05), 'tri') * np.sin(np.linspace(0, np.pi, n)) ** 0.6 * 0.3
    t = t_of(d)[:n]
    whoosh = bandpass(noise(d), 1200, 6000)[:n] * (0.5 + 0.5 * np.sin(2 * np.pi * 3 * t)) * np.linspace(0.1, 0.4, n)
    return (swirl + swirl2 + whoosh + sparkle(d, 14, 2500, 5000, 0.3)) * env(n, a=0.02, r=0.35)


def floor_enter(p=1.0):
    """다음 층 이동: 빙글 도는 워프 '뿌우웅'."""
    d = 1.0
    n = int(SR * d)
    spin = osc(vibrato(sweep(400, 1600, 0.7, 1.2), 16, 0.1), 'square', 0.3)
    spin = lowpass(fit(spin, d), 5000) * env(n, a=0.005, r=0.35) * 0.6
    whoosh = bandpass(noise(d), 1000, 6000) * np.sin(np.linspace(0, np.pi, n)) ** 2 * 0.5
    return fit(mix(spin, whoosh, at(sparkle(0.3, 3), 0.65)), d)


# ── UI ──
def ui_open(p=1.0):
    """창 열기·닫기: 가벼운 '슉' + 작은 '퐁'."""
    sw = bandpass(noise(0.2), 1800, 7000) * np.sin(np.linspace(0, np.pi, int(SR * 0.2))) ** 2 * 0.6
    pop = osc(sweep(500, 1100, 0.06, 0.6), 'square', 0.3) * env(int(SR * 0.06), a=0.001, r=0.03) * 0.6
    return fit(mix(sw, at(pop, 0.15)), 0.3)


def ui_select():
    """보스 속성 선택 확정: 밝은 확정 차임 + 반짝."""
    arp = seq(*[note(f, 0.07, 'square', 0.25, 0.6, r=0.02) for f in (1046, 1318, 1568)])
    last = chime([2093, 2093 * 2.0, 3136], 0.5, 0.18, 0.9)
    return fit(mix(arp, at(last, 0.21), at(sparkle(0.45, 6), 0.3)), 0.8)


# 반복 소리(앞뒤 페이드 없이 이음매 처리)
LOOPS = {'sfx_absorb_hold', 'sfx_revive_loop'}

NEW_SOUNDS = {
    'sfx_player_down': player_down,
    'sfx_revive_loop': revive_loop,
    'sfx_elem_fire': elem_fire,
    'sfx_elem_ice': elem_ice,
    'sfx_elem_electric': elem_electric,
    'sfx_elem_poison': elem_poison,
    'sfx_elem_earth': elem_earth,
    'sfx_monster_die': monster_die,
    'sfx_monster_warn': monster_warn,
    'sfx_dog_bark': dog_bark,
    'sfx_dragon_breath': dragon_breath,
    'sfx_shield_block': shield_block,
    'sfx_boss_roar': boss_roar,
    'sfx_boss_charge': boss_charge,
    'sfx_boss_wall_hit': boss_wall_hit,
    'sfx_boss_slam': boss_slam,
    'sfx_boss_phase': boss_phase,
    'sfx_boss_down': boss_down,
    'sfx_door_lock': door_lock,
    'sfx_portal_open': portal_open,
    'sfx_floor_enter': floor_enter,
    'sfx_ui_open': ui_open,
    'sfx_ui_select': ui_select,
}

# 자주 나는 소리: 음높이 배수·노이즈 시드를 달리한 변형(_1, _2, _3)
VARIANTS = {
    'sfx_sword_swing': (sword_swing, (1.0, 1.08, 0.93)),
    'sfx_hit': (hit, (1.0, 1.1, 0.92)),
    'sfx_hit_strong': (hit_strong, (1.0, 1.07)),
    'sfx_guard': (guard, (1.0, 1.06)),
    'sfx_monster_hurt': (monster_hurt, (1.0, 1.12, 0.9)),
}


def all_ids():
    return list(SOUNDS) + list(NEW_SOUNDS) + list(VARIANTS)


if __name__ == '__main__':
    for name, fn in SOUNDS.items():
        save(name, fn(), loop=name in LOOPS)
    for k, (name, fn) in enumerate(NEW_SOUNDS.items()):
        rng = np.random.default_rng(100 + k)
        save(name, fn(), loop=name in LOOPS)
    count = len(SOUNDS) + len(NEW_SOUNDS)
    for k, (name, (fn, pitches)) in enumerate(VARIANTS.items()):
        for v, pitch in enumerate(pitches, 1):
            rng = np.random.default_rng(1000 + k * 10 + v)
            save(f'{name}_{v}', fn(pitch))
            count += 1
    print(len(all_ids()), 'ids /', count, 'files ->', OUT)
