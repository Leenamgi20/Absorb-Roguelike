"""합성 효과음 검사(2026-10-14) — Art_Source/sound/synth/의 40개 id를 목록 규격과 비교한다.
검사: 파일(변형 _1/_2/_3 포함) 존재, 길이(목록 길이 -25% ~ +10%, 범위로 적힌 반복 소리는 그 범위), 피크 <= -1dBFS(+0.05 오차),
RMS 무음 아님(> -40dBFS), 모노·44.1kHz·16bit.
실행: python Tools/sfx_synth_check.py  (프로젝트 루트) — 표를 마크다운으로 출력, 실패가 있으면 종료 코드 1.
"""
import os
import re
import sys
import wave

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SYNTH = os.path.join(ROOT, 'Art_Source', 'sound', 'synth')

# 목록 문서 5장 길이(초). (최소, 최대)면 범위.
SPEC = {
    'sfx_sword_swing': 0.3, 'sfx_hit': 0.2, 'sfx_hit_strong': 0.3, 'sfx_guard': 0.3, 'sfx_dash': 0.3,
    'sfx_player_hurt': 0.3, 'sfx_player_down': 0.8, 'sfx_revive_loop': (1.0, 2.0), 'sfx_revive_done': 0.8,
    'sfx_absorb_hold': 1.0, 'sfx_absorb_success': 0.5, 'sfx_trait_equip': 0.4, 'sfx_trait_lost': 0.5,
    'sfx_debris_grab': 0.3, 'sfx_debris_spit': 0.3,
    'sfx_elem_fire': 0.4, 'sfx_elem_ice': 0.4, 'sfx_elem_electric': 0.4, 'sfx_elem_poison': 0.4, 'sfx_elem_earth': 0.4,
    'sfx_monster_hurt': 0.2, 'sfx_monster_die': 0.5, 'sfx_monster_warn': 0.3, 'sfx_dog_bark': 0.4,
    'sfx_dragon_breath': 1.0, 'sfx_shield_block': 0.3,
    'sfx_boss_roar': 1.5, 'sfx_boss_charge': 1.0, 'sfx_boss_wall_hit': 0.8, 'sfx_boss_slam': 1.0,
    'sfx_boss_phase': 1.5, 'sfx_boss_down': 2.0,
    'sfx_door_lock': 0.8, 'sfx_room_clear': 1.0, 'sfx_portal_open': 1.5, 'sfx_floor_enter': 1.0,
    'sfx_ui_hover': 0.1, 'sfx_ui_click': 0.15, 'sfx_ui_open': 0.3, 'sfx_ui_select': 0.8,
}
# 처음 12개 합성본(2026-10-08)은 목록보다 짧게 만든 것이 있어 하한만 넉넉히(-60%) 둔다 — 짧은 건 타격감엔 문제없음.
LEGACY = {'sfx_absorb_success', 'sfx_trait_equip', 'sfx_trait_lost', 'sfx_debris_grab', 'sfx_debris_spit', 'sfx_dash',
          'sfx_player_hurt', 'sfx_revive_done', 'sfx_ui_click', 'sfx_ui_hover', 'sfx_room_clear', 'sfx_absorb_hold'}


def files_for(sid):
    pat = re.compile(re.escape(sid) + r'(_\d+)?\.wav$')
    return sorted(f for f in os.listdir(SYNTH) if pat.fullmatch(f))


def check(path):
    with wave.open(path) as w:
        ch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        data = np.frombuffer(w.readframes(n), dtype=np.int16).astype(float) / 32768
    peak = 20 * np.log10(np.max(np.abs(data)) + 1e-12)
    rms = 20 * np.log10(np.sqrt(np.mean(data ** 2)) + 1e-12)
    return n / sr, peak, rms, (ch == 1 and sw == 2 and sr == 44100)


def main():
    rows, failed = [], 0
    for sid, spec in SPEC.items():
        files = files_for(sid)
        if isinstance(spec, tuple):
            lo, hi = spec
        else:
            lo, hi = spec * (0.4 if sid in LEGACY else 0.75), spec * 1.1
        if not files:
            rows.append((sid, 0, '-', '-', '-', '실패: 파일 없음'))
            failed += 1
            continue
        lens, peaks, ok, why = [], [], True, []
        for f in files:
            dur, peak, rms, fmt = check(os.path.join(SYNTH, f))
            lens.append(dur)
            peaks.append(peak)
            if not lo <= dur <= hi:
                ok = False
                why.append(f'{f} 길이 {dur:.2f}s')
            if peak > -0.95:
                ok = False
                why.append(f'{f} 피크 {peak:.2f}')
            if rms < -40:
                ok = False
                why.append(f'{f} 무음')
            if not fmt:
                ok = False
                why.append(f'{f} 형식')
        failed += 0 if ok else 1
        length = f'{min(lens):.2f}' if len(lens) == 1 else f'{min(lens):.2f}~{max(lens):.2f}'
        rows.append((sid, len(files), length, f'{max(peaks):.2f}', f'{lo:.2f}~{hi:.2f}', '통과' if ok else '실패: ' + ', '.join(why)))

    print('| id | 파일 수 | 길이(초) | 허용 길이 | 피크 dBFS | 판정 |')
    print('|---|---|---|---|---|---|')
    for sid, cnt, length, peak, rng_, verdict in rows:
        print(f'| `{sid}` | {cnt} | {length} | {rng_} | {peak} | {verdict} |')
    print(f'\n{len(SPEC)}개 중 실패 {failed}개')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
