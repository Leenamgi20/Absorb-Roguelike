"""밸런스 검토 보고서(HTML) 생성 — 값은 레벨 디자인 시트 에셋(2026-10-14 덤프 Tools/refactor_baseline/serialized_values_current.txt)에서 옮긴 것. 실행: python Tools/balance_report.py docs/01_기획/밸런스_검토_YYYY-MM-DD.html"""
import html
import sys

OUT = sys.argv[1]

BASE = 10        # PlayerCombat.attackDamage
CYCLE = 1.0      # attackSwingDuration 0.5 + attackRecoveryDelay 0.5
ELEM_BONUS = 3   # MainAbilityController.elementDamageBonus


def rnd(x):  # Mathf.RoundToInt = 은행원 반올림(.5는 짝수로)
    return int(round(x))


# ── 1. 동물 흡수 스킬 (단일 대상 초당 대미지, 속성 없음, 1층) ─────────────
# (이름, 현재 DPS, 추천 DPS, 현재 근거, 추천 근거, 성격)
skills = [
    ("흡수 없음(기본)", BASE / CYCLE, BASE / CYCLE, "10 ÷ 1초", "변경 없음", "기준"),
    ("호랑이", 3 * rnd((BASE + 8) * 0.4) / CYCLE, 3 * rnd((BASE + 3) * 0.4) / CYCLE,
     "패시브 공격 +8 → (18×0.4=7)×3타", "패시브 공격 +8 → +3: (13×0.4=5)×3타", "단일 딜"),
    ("뱀", (BASE + 3) / CYCLE, (BASE + 3) / CYCLE, "패시브 +3, 원 전체 + 둔화", "변경 없음", "광역·둔화"),
    ("소", rnd(BASE * 1.2) / 0.7, rnd(BASE * 1.2) / 0.7, "돌진 12, 0.2초+후딜 0.5초, 넉백×2·갑옷 40", "변경 없음", "돌진·갑옷 파괴"),
    ("토끼", 15 / CYCLE, 15 / CYCLE, "끝부분 15(경직 0.4초)·중간 10·근접 7", "변경 없음", "거리 유지"),
    ("쥐", BASE * 1.30 / CYCLE, BASE * 1.50 / CYCLE, "취약 최대 +30%(6%×5)", "스택당 6% → 10%(최대 +50%)", "파티 지원(취약)"),
    ("용", rnd(BASE * 0.25) / 0.15, rnd(BASE * 0.3) / 0.2, "틱 2(10×0.25=2.5→2)÷0.15초, 반경 4", "틱 배율 0.25 → 0.3(3), 간격 0.15 → 0.2초 — 사용자 결정: 조작감이 좋아 초당 15(호랑이·토끼와 같은 상위권)", "광역·정지"),
    ("원숭이", rnd(BASE * 0.7) / CYCLE, rnd(BASE * 1.1) / CYCLE, "바나나 7, 사거리 6", "배율 0.7 → 1.1(11) — 사용자 결정: 원거리지만 기본보다 조금 높게", "원거리"),
    ("말", BASE / CYCLE + BASE / 1.4, BASE / CYCLE + BASE / 1.4, "기본 + 대시 경로 10(2회, 쿨 0.8초)", "변경 없음", "기동"),
    ("닭", BASE / CYCLE + rnd(BASE * 0.3) / 0.8, BASE / CYCLE + rnd(BASE * 0.5) / 0.8, "기본 + 대시 3, 알 회복 6", "대시 0.3 → 0.5(5), 알 회복 6 → 10", "회복 지원"),
    ("돼지", BASE / CYCLE + rnd(BASE * 1.5) / 1.5 * 0.5, BASE / CYCLE + rnd(BASE * 1.5) / 1.0 * 0.5, "반격 15, 쿨 1.5초(막기 절반 가정)", "반격 쿨 1.5 → 1.0초", "방어·반격"),
    ("양", BASE / CYCLE + rnd(BASE * 1.5) / 6, BASE / CYCLE + rnd(BASE * 2.5) / 6, "털 3번 막으면 폭발 15 + 파티 보호막", "폭발 1.5 → 2.5배(25)", "방어·보호막"),
    ("개", BASE / CYCLE + rnd(BASE * 0.4) / 1.0, BASE / CYCLE + rnd(BASE * 0.6) / 1.0, "기본 + 동료 물기 4/초", "물기 0.4 → 0.6배(6)", "자동 추가 딜"),
]

# ── 2. 속성 (적 하나를 1초마다 1번 때릴 때 추가 초당 대미지, 시간에 따라) ─────
def dot_curve(per_stack, max_stacks, t):
    stacks = min(int(t // CYCLE) + 1, max_stacks) if t >= 0 else 0
    return per_stack * stacks


times = [i * 0.5 for i in range(0, 25)]  # 0~12초
fire_now = [dot_curve(4, 5, t) for t in times]
fire_rec = [dot_curve(2.5, 5, t) for t in times]
poison_now = [dot_curve(2, 10, t) for t in times]
poison_rec = [dot_curve(1.5, 10, t) for t in times]

elements = [
    ("화염", "스택당 4/초 × 최대 5 = 20/초(5초에 도달), 3초 지속", 20, "스택당 2.5/초 → 최대 12.5/초", 12.5, "빠르게 오르는 지속 피해"),
    ("독", "스택당 2/초 × 최대 10 = 20/초(10초에 도달), 5초 지속", 20, "스택당 1.5/초 → 최대 15/초(화염보다 높고 느림)", 15, "느리게 쌓이는 지속 피해"),
    ("전기", "3번 맞히면 방전 12 + 마비 1초 → 4/초 + 마비 33%", 4, "방전 12 → 15 → 5/초", 5, "주기적 마비"),
    ("대지", "맞힐 때 경직 0.3초(같은 대상 1초 쿨) → 정지 30%", 0, "변경 없음", 0, "경직(보스 패턴 타이머도 멈춤)"),
    ("냉기", "둔화 20%·40%·60%(3스택), 3초 지속", 0, "변경 없음", 0, "둔화"),
]

# ── 3. 몬스터 (1층, 혼자) ─────────────────────────────────────────────
# 이름, 체력, 공격력, 공격쿨, 예고, 후딜, 비고
mobs = [
    ("쥐", 18, 5, 0.6, 0.2, 0.15, "3번째 물기 ×3(15)"), ("닭", 20, 7, 1.5, 0.5, 0.4, "급강하"),
    ("토끼", 22, 6, 1.5, 0.5, 0.4, "끝부분 ×1.5"), ("호랑이", 24, 6, 1.5, 0.5, 0.4, "3연타"),
    ("원숭이", 24, 7, 1.5, 0.5, 0.4, "원거리"), ("뱀", 30, 8, 1.5, 0.5, 0.4, "명중 둔화"),
    ("개", 32, 9, 1.5, 0.5, 0.4, "돌진 후 후퇴"), ("말", 35, 10, 1.5, 0.6, 0.4, "2연속 돌진"),
    ("소", 45, 12, 1.5, 1.0, 0.4, "돌진·넉백 2.5"), ("양", 50, 8, 1.5, 0.5, 0.4, "털 폭발"),
    ("용", 50, 12, 1.5, 0.8, 0.4, "브레스 반경 3"), ("돼지", 60, 10, 1.5, 0.5, 0.4, "정면 대미지 ×0.2"),
]
COOL_S, WIND_S = 1.35, 1.25  # DifficultyConfig 전체 배율


def threat(dmg, cd, wind, rec, name):
    if name == "쥐":
        return (5 + 5 + 15) / (3 * (cd * COOL_S + wind * WIND_S + rec))
    if name == "호랑이":
        dmg = dmg * 3
    return dmg / (cd * COOL_S + wind * WIND_S + rec)


mob_rows = []
for n, hp, dmg, cd, wind, rec, note in mobs:
    eff = hp * 5 if n == "돼지" else hp  # 정면에서만 때릴 때(×0.2) 실질 체력
    mob_rows.append((n, hp, -(-hp // BASE), threat(dmg, cd, wind, rec, n), note, eff))

# ── 4. 배율이 쌓일 때 (정예 × 층 × 인원) ─────────────────────────────
floor_hp = [1.0, 1.15, 1.3, 1.45, 1.6]
scaling_rows = []
for f in range(5):
    scaling_rows.append((f + 1, 3 * floor_hp[f], 3 * floor_hp[f] * 1.5, 2.5 * floor_hp[f], 2.5 * floor_hp[f] * 1.5))

# ── 추천안 목록 ─────────────────────────────────────────────────────
recs = [
    ("U1", "동물", "용 브레스 틱 0.25 → 0.3배, 간격 0.15 → 0.2초(초당 13.3 → 15)", "사용자 결정: 처음엔 1등(18.8)으로 올렸다가, 이동만 막혀 있을 뿐 조작감이 좋아 초당 15로 조정 — 호랑이·토끼·쥐와 같은 상위권. 반올림 때문에 배율은 0.3(틱 3)으로 맞추고 간격으로 조정.", "AbsorbSkills/Dragon dragonBreathDamageScale·TickInterval"),
    ("A1", "동물", "호랑이 패시브 공격력 +8 → +3", "호랑이 3연타가 기본의 2.1배(21/초). 다른 동물 대부분이 기본의 1~1.5배라 혼자 튄다. +3이면 1.5배(15/초) — 뱀(+3)과 같은 크기, 이동속도 −10% 대가는 유지.", "패시브 특성 탭 — PassiveTraits/호랑이 attackDamageBonus"),
    ("A2", "동물", "쥐 취약 스택당 6% → 10%", "QA #43 '체감 안 됨'. 5스택 최대 +30% → +50%. 취약은 파티원 공격에도 붙어 협동 지원 동물이라는 성격이 살아난다.", "AbsorbSkills/Rat vulnerableDamagePerStack"),
    ("A3", "동물", "원숭이 바나나 0.7 → 1.1배(초당 11)", "원거리라 안전한 대신 7/초로 가장 약함. 추천은 0.8이었으나 사용자 결정으로 1.1 — 기본(10)보다 조금 높고 근접 스킬(13~19)보다 낮은 자리.", "AbsorbSkills/Monkey bananaDamageScale"),
    ("A4", "동물", "닭 대시 0.3 → 0.5배, 알 회복 6 → 10", "대시 3은 맞혀도 체감이 거의 없고, 알 6은 몬스터 한 대(6~12)보다 작다. 회복 지원 동물로 고르는 이유를 만든다.", "AbsorbSkills/Rooster roosterDashDamageScale·eggHealAmount"),
    ("A5", "동물", "돼지 반격 쿨 1.5 → 1.0초", "QA #16 '연속 공격에 모두 반격이 나가지 않음'. 잡몹 공격 간격(약 2초)에는 1.0초면 매번 반격, 연타 보스 층은 기존 0.4초 유지.", "AbsorbSkills/Pig pigCounterCooldown"),
    ("A6", "동물", "양 털 폭발 1.5 → 2.5배", "세 번 막아야 한 번 터지는데 15는 기본 공격 1.5번. 25면 '모아서 터뜨리는' 보상이 생긴다(파티 보호막은 그대로).", "AbsorbSkills/Sheep sheepBurstDamageScale"),
    ("A7", "동물", "개 동료 물기 0.4 → 0.6배", "자동 공격이 4/초로 거의 안 보인다(지난 피드백 '공격을 너무 안 함'의 남은 원인). 6/초.", "AbsorbSkills/Dog dogBiteDamageScale"),
    ("E1", "속성", "화염 스택당 4 → 2.5/초(최대 12.5/초)", "지금 화염·독 최대치 20/초는 플레이어 기본 공격(10/초)의 2배 — 속성 하나가 공격의 주인공이 된다. 화염은 빨리 오르고(5초) 낮은 최대치.", "StatusEffectConfig fireDpsPerStack"),
    ("E2", "속성", "독 스택당 2 → 1.5/초(최대 15/초)", "지금은 화염과 최대치가 같은데 오르는 속도가 절반이라 화염의 하위 호환. 최대치를 화염보다 높게 두어 '오래 싸우면 독' 선택지를 만든다.", "StatusEffectConfig poisonDpsPerStack"),
    ("E3", "속성", "전기 방전 12 → 15", "전기는 4/초 + 마비. 지속 피해를 낮춘 화염·독과 맞추려고 조금 올림(5/초).", "StatusEffectConfig electricDischargeDamage"),
    ("M1", "몬스터", "돼지 체력 60 → 45", "정면 대미지 ×0.2라 정면 실질 체력이 300. 정예(×3)·5층(×1.6)·2인(×1.5)이면 정면 2160 — 돌아가지 않으면 못 잡는다. 45면 기존 의도(옆·뒤로 돌기)는 유지되고 피로감이 준다.", "Monsters/Pig maxHealth"),
    ("M2", "몬스터", "쥐 3번째 물기 ×3 → ×2.5", "쥐 위협이 초당 약 6.9로 다른 잡몹(2~4)의 2배, 정예·5층이면 14/초로 7초에 플레이어가 쓰러진다. 붙으면 위험하다는 성격은 유지.", "Monsters/Rat burstMultiplier"),
    ("D1", "난이도", "정예 체력 ×3 → ×2.5", "정예 × 5층 × 2인이 7.2배까지 쌓인다(잡몹 30 → 216). ×2.5면 6배. 정예 공격 ×1.5는 유지.", "Monsters/EliteConfig healthMultiplier"),
    ("D2", "난이도", "보스 '재생' 초당 1% → 0.5%", "300 체력에 초당 3(5층 4.8) 회복은 혼자 스킬 공격(약 15/초)의 20~30%를 지운다. 0.5%면 '화염으로 끊어야 한다'는 공략은 남고 질질 끌림은 준다.", "Boss/Ogre_Stats regenPercentPerSecond"),
    ("D3", "난이도", "보스 포효(E) 45 → 40", "5층에서 63(체력의 63%). 예고 1.6초라 피할 수 있지만 2연속 실수 = 즉사. 40이면 56.", "Boss/Ogre_E_Roar roarDamage"),
]

keep = [
    "층 배율(체력 1.0→1.6, 공격 1.0→1.4)과 2인 체력 ×1.5 — 위 추천을 반영한 뒤 플레이로 다시 본다.",
    "전체 배율(이동속도 ×0.75, 공격 쿨 ×1.35, 예고 ×1.25) — QA #17 '때릴 틈이 없다' 이후 맞춘 값. 지금 플레이 피드백에 문제가 없어 유지.",
    "소·토끼·뱀·말·용 흡수 스킬 — 기본의 1.3~1.7배 범위로 각자 역할(돌진·거리·광역·기동·정지 광역)이 겹치지 않는다.",
    "용 브레스 틱 대미지는 10×0.25=2.5가 반올림 규칙(.5는 짝수로)에 따라 2가 된다. 속성 보너스가 붙으면 13×0.25=3.25→3. 지금은 의도대로 보이지만, 배율을 고칠 때 이 반올림을 감안해야 한다(예: 0.25→0.3이면 3).",
]

# ── SVG 도우미 ───────────────────────────────────────────────────────
def esc(s):
    return html.escape(str(s))


def bar_pairs(rows, width=760, label_w=130, row_h=34, max_v=None):
    """가로 막대 2개(현재·추천)씩. rows = (이름, 현재, 추천, 툴팁)."""
    max_v = max_v or max(max(r[1], r[2]) for r in rows) * 1.08
    plot_w = width - label_w - 60
    h = len(rows) * row_h + 34
    out = [f'<svg viewBox="0 0 {width} {h}" class="chart" role="img" aria-label="현재와 추천 비교 막대 그래프">']
    # 격자
    step = 5 if max_v <= 40 else 10
    v = 0
    while v <= max_v:
        x = label_w + plot_w * v / max_v
        out.append(f'<line x1="{x:.1f}" y1="6" x2="{x:.1f}" y2="{h - 22}" class="grid"/>')
        out.append(f'<text x="{x:.1f}" y="{h - 6}" class="axis" text-anchor="middle">{v}</text>')
        v += step
    for i, (name, now, rec, tip) in enumerate(rows):
        y = 8 + i * row_h
        changed = abs(now - rec) > 1e-6
        out.append(f'<text x="{label_w - 8}" y="{y + 17}" class="label" text-anchor="end">{esc(name)}</text>')
        for j, (val, cls) in enumerate(((now, "s1"), (rec, "s2"))):
            w = max(2, plot_w * val / max_v)
            yy = y + 3 + j * 13
            kind = "현재" if j == 0 else "추천"
            out.append(f'<g class="hit"><title>{esc(name)} — {kind} {val:.1f}/초&#10;{esc(tip)}</title>'
                       f'<rect x="{label_w}" y="{yy}" width="{w:.1f}" height="11" rx="3" class="{cls}"/>'
                       f'<rect x="{label_w}" y="{yy - 1}" width="{plot_w}" height="13" fill="transparent"/></g>')
            # 값 글자: 바뀐 줄은 두 막대 모두, 안 바뀐 줄은 위 막대에만
            if changed or j == 0:
                text = f"{val:.1f}" if changed else f"{val:.1f} (같음)"
                out.append(f'<text x="{label_w + w + 6:.1f}" y="{yy + 10}" class="val{"" if j == 1 or not changed else " muted"}">{text}</text>')
    out.append('</svg>')
    return "\n".join(out)


def line_chart(series, times, width=760, height=300, max_v=22):
    left, right, top, bottom = 44, 20, 14, 36
    pw, ph = width - left - right, height - top - bottom

    def X(t):
        return left + pw * t / times[-1]

    def Y(v):
        return top + ph * (1 - v / max_v)

    out = [f'<svg viewBox="0 0 {width} {height}" class="chart" role="img" aria-label="속성 지속 피해 시간 그래프">']
    for v in range(0, max_v + 1, 5):
        out.append(f'<line x1="{left}" y1="{Y(v):.1f}" x2="{width - right}" y2="{Y(v):.1f}" class="grid"/>')
        out.append(f'<text x="{left - 6}" y="{Y(v) + 4:.1f}" class="axis" text-anchor="end">{v}</text>')
    for t in range(0, int(times[-1]) + 1, 2):
        out.append(f'<text x="{X(t):.1f}" y="{height - 14}" class="axis" text-anchor="middle">{t}초</text>')
    for name, vals, cls, dash in series:
        pts = []
        for i, (t, v) in enumerate(zip(times, vals)):
            if i > 0:
                pts.append(f"{X(t):.1f},{Y(vals[i - 1]):.1f}")  # 계단
            pts.append(f"{X(t):.1f},{Y(v):.1f}")
        dash_attr = 'stroke-dasharray="6 5"' if dash else ""
        joined = " ".join(pts)
        out.append(f'<polyline points="{joined}" class="line {cls}" {dash_attr}/>')
    # 끝 글자: 값이 같으면 겹치므로 위에서부터 최소 15px 간격으로 밀어 낸다
    labels = sorted(((Y(vals[-1]) - 6, f"{name} {vals[-1]:g}") for name, vals, _, _ in series))
    last_y = -1e9
    for y, text in labels:
        y = max(y, last_y + 15)
        last_y = y
        out.append(f'<text x="{width - right - 4}" y="{y:.1f}" class="val" text-anchor="end">{esc(text)}</text>')
    # 호버용 점(0.5초마다)
    for name, vals, cls, dash in series:
        for t, v in zip(times, vals):
            out.append(f'<circle cx="{X(t):.1f}" cy="{Y(v):.1f}" r="7" fill="transparent" class="hit"><title>{esc(name)} — {t:g}초: {v:g}/초</title></circle>')
    out.append('</svg>')
    return "\n".join(out)


# ── HTML ───────────────────────────────────────────────────────────
skill_rows = [(n, now, rec, f"{a} → {b}") for n, now, rec, a, b, _ in skills]
elem_rows = [(n, now, rec, f"{a} → {b}") for n, a, now, b, rec, _ in elements]

def table(head, rows, cls=""):
    h = "".join(f"<th>{esc(x)}</th>" for x in head)
    body = "".join("<tr>" + "".join(f"<td>{c}</td>" for c in r) + "</tr>" for r in rows)
    return f'<div class="table-wrap"><table class="{cls}"><thead><tr>{h}</tr></thead><tbody>{body}</tbody></table></div>'


rec_table = table(["번호", "분야", "바꾼 것", "이유", "고칠 곳(레벨 디자인 시트)", "결정"],
                  [(f"<b>{a}</b>", b, f"<b>{esc(c)}</b>", esc(d), f"<code>{esc(e)}</code>", "반영") for a, b, c, d, e in recs], "recs")

skill_table = table(["동물", "성격", "현재 초당 대미지", "근거", "추천", "근거"],
                    [(esc(n), esc(k), f"{now:.1f}", esc(a), f"<b>{rec:.1f}</b>" if abs(now - rec) > 1e-6 else f"{rec:.1f}", esc(b)) for n, now, rec, a, b, k in skills])

elem_table = table(["속성", "성격", "현재", "추천"], [(esc(n), esc(k), esc(a), esc(b)) for n, a, _, b, _, k in elements])

mob_table = table(["몬스터", "체력", "기본 공격으로 처치(타)", "위협(초당 받는 대미지, 1층 혼자)", "특징"],
                  [(esc(n), f"{hp}" + (f" <span class='muted'>(정면 실질 {eff})</span>" if eff != hp else ""), f"{k}", f"{t:.1f}", esc(note)) for n, hp, k, t, note, eff in mob_rows])

scale_table = table(["층", "정예 체력 배율 혼자 (지금 ×3)", "정예 2인 (지금)", "정예 혼자 (추천 ×2.5)", "정예 2인 (추천)"],
                    [(f"{f}층", f"×{a:.2f}", f"×{b:.2f}", f"×{c:.2f}", f"×{d:.2f}") for f, a, b, c, d in scaling_rows])

page = f"""<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>밸런스 검토 보고서</title>
<style>
:root {{ --bg:#fcfcfb; --panel:#ffffff; --ink:#0b0b0b; --muted:#52514e; --line:#e2e0da; --grid:#ecebe7;
  --s1:#2a78d6; --s2:#eb6834; --s3:#1baf7a; --code:#f1f0ec; --accent:#2a78d6; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#1a1a19; --panel:#222220; --ink:#ffffff; --muted:#c3c2b7;
  --line:#3a3936; --grid:#2e2d2a; --s1:#3987e5; --s2:#d95926; --s3:#199e70; --code:#2c2b28; --accent:#3987e5; }} }}
:root[data-theme="dark"] {{ --bg:#1a1a19; --panel:#222220; --ink:#ffffff; --muted:#c3c2b7; --line:#3a3936; --grid:#2e2d2a;
  --s1:#3987e5; --s2:#d95926; --s3:#199e70; --code:#2c2b28; --accent:#3987e5; }}
* {{ box-sizing:border-box; }}
body {{ margin:0; background:var(--bg); color:var(--ink); font:15px/1.7 "Pretendard","Malgun Gothic",system-ui,sans-serif; }}
main {{ max-width:1000px; margin:0 auto; padding:28px 16px 80px; }}
h1 {{ font-size:26px; margin:0 0 6px; }} h2 {{ font-size:20px; margin:42px 0 10px; padding-bottom:6px; border-bottom:1px solid var(--line); }}
p {{ margin:8px 0; }} .muted {{ color:var(--muted); }}
code {{ font-family:Consolas,monospace; font-size:12.5px; background:var(--code); padding:1px 5px; border-radius:4px; }}
.note {{ border-left:3px solid var(--accent); background:var(--panel); padding:10px 14px; margin:12px 0; border-radius:0 8px 8px 0; }}
.table-wrap {{ overflow-x:auto; border:1px solid var(--line); border-radius:8px; background:var(--panel); margin:10px 0; }}
table {{ border-collapse:collapse; width:100%; font-size:14px; }} th,td {{ padding:7px 10px; border-bottom:1px solid var(--line); text-align:left; vertical-align:top; }}
th {{ background:var(--code); white-space:nowrap; font-weight:600; }} tr:last-child td {{ border-bottom:none; }}
table.recs td:first-child {{ white-space:nowrap; }}
.chart {{ width:100%; height:auto; background:var(--panel); border:1px solid var(--line); border-radius:8px; }}
.chart .grid {{ stroke:var(--grid); stroke-width:1; }} .chart .axis {{ fill:var(--muted); font-size:11px; }}
.chart .label {{ fill:var(--ink); font-size:12.5px; }} .chart .val {{ fill:var(--ink); font-size:11.5px; }} .chart .val.muted {{ fill:var(--muted); }}
.chart .s1 {{ fill:var(--s1); }} .chart .s2 {{ fill:var(--s2); }}
.chart .line {{ fill:none; stroke-width:2; }} .chart .line.s1 {{ stroke:var(--s2); }} .chart .line.s2 {{ stroke:var(--s3); }}
.chart .hit:hover rect:first-of-type {{ opacity:.8; }}
.legend {{ display:flex; gap:16px; flex-wrap:wrap; font-size:13px; color:var(--muted); margin:6px 0; }}
.legend span::before {{ content:""; display:inline-block; width:14px; height:10px; border-radius:2px; margin-right:6px; vertical-align:middle; background:var(--c); }}
.legend .dash::before {{ height:0; border-top:2px dashed var(--c); background:none; }}
.legend .solid::before {{ height:0; border-top:2px solid var(--c); background:none; }}
</style></head><body><main>
<h1>밸런스 검토 보고서</h1>
<p class="muted">2026-10-14 · 레벨 디자인 시트 수치 전체(잡몹 12종·정예·보스·상태이상·난이도·동물 스킬)를 코드의 실제 공식으로 계산해 비교했다. 2026-10-14 사용자 결정으로 전부 반영했다(원숭이·용은 사용자 조정).</p>
<div class="note">계산 기준: 플레이어 기본 공격 10, 공격 한 주기 1초(휘두르기 0.5 + 후딜 0.5), 1층, 혼자, 적 하나. 실제 체감은 맞히는 비율·이동·여러 적에 따라 달라지므로 <b>상대 비교용</b>이다. 막대·선에 마우스를 올리면 근거가 뜬다.</div>

<h2>1. 조정 목록 — 2026-10-14 전부 반영</h2>
<p>추천안 15개를 모두 반영했고, 사용자 결정으로 원숭이(A3)는 1.1배로 바꾸고 용(U1)을 추가했다(용은 1등으로 올렸다가 조작감을 보고 초당 15로 재조정). 아래 차트의 '추천'이 지금 게임에 들어간 값이다.</p>
{rec_table}

<h2>2. 동물 흡수 스킬 — 초당 대미지 (단일 대상)</h2>
<div class="legend"><span style="--c:var(--s1)">현재</span><span style="--c:var(--s2)">추천</span></div>
{bar_pairs(skill_rows)}
<p class="muted">호랑이만 기본의 2배를 넘는다. 원숭이·닭·개·양은 역할(원거리·회복·자동 공격·보호막)에 비해 대미지 보상이 작아 고를 이유가 약하다. 돼지·양·닭은 막기·회복 같은 비대미지 가치가 있어 대미지만으로는 낮게 보인다.</p>
{skill_table}

<h2>3. 속성 — 지속 피해가 쌓이는 모습</h2>
<p>적 하나를 1초마다 1번 맞힐 때, 속성이 추가로 주는 초당 대미지(공통 +3 공격력 보너스 제외).</p>
<div class="legend"><span class="solid" style="--c:var(--s2)">화염 현재</span><span class="dash" style="--c:var(--s2)">화염 추천</span><span class="solid" style="--c:var(--s3)">독 현재</span><span class="dash" style="--c:var(--s3)">독 추천</span></div>
{line_chart([("화염 현재", fire_now, "s1", False), ("화염 추천", fire_rec, "s1", True), ("독 현재", poison_now, "s2", False), ("독 추천", poison_rec, "s2", True)], times)}
<p class="muted">지금은 화염과 독의 최대치가 같은데(20/초) 독이 두 배 느리게 오른다 — 독을 고를 이유가 없다. 또 20/초는 플레이어 기본 공격(10/초)의 2배라 속성이 공격의 주인공이 된다. 추천안은 화염 = 빨리 오르고 낮은 최대치, 독 = 느리지만 더 높은 최대치로 나눈다.</p>
{elem_table}

<h2>4. 잡몹 — 체력과 위협</h2>
{mob_table}
<p class="muted">위협 = 공격력 ÷ (공격 쿨×1.35 + 예고×1.25 + 후딜), 이동은 빼고 계산. 쥐가 다른 잡몹의 두 배, 돼지는 정면 대미지 감소 때문에 실질 체력이 다른 잡몹의 5~10배다.</p>

<h2>5. 배율이 쌓일 때 — 정예 체력</h2>
<p>정예 배율 × 층 배율 × 인원 배율이 곱해진 최종 체력 배율(잡몹 기본 체력에 곱함). 예: 체력 30 잡몹의 정예는 5층 2인에서 지금 216, 추천 180.</p>
{scale_table}

<h2>6. 그대로 두는 것</h2>
<ul>{"".join(f"<li>{esc(k)}</li>" for k in keep)}</ul>

<h2>⏳ 변경 이력 (History)</h2>
<ul><li>2026-10-14, v1.02: 용 브레스를 초당 15로 다시 조정(사용자 결정), 패시브 특성은 시트 '패시브 특성' 탭에서 고침</li><li>2026-10-14, v1.01: 추천안 전부 반영, 원숭이 1.1배·용 1등(U1) 사용자 결정 반영</li><li>2026-10-14, v1.00: 최초 작성 — 동물 스킬·속성·잡몹·배율 검토와 추천안 15개(A1~A7, E1~E3, M1~M2, D1~D3)</li></ul>
</main>
<script>
// 테마는 OS 설정을 따른다. 체크용 data-theme가 없으면 아무것도 하지 않는다.
</script>
</body></html>"""

open(OUT, "w", encoding="utf-8").write(page)
print("written", OUT)
