"""직렬화 값 비교 결과(diff.txt)에서 '사라짐'과 '새로'를 필드 이름·값으로 짝지어 본다.

필드를 프리팹 컴포넌트에서 데이터 에셋(ScriptableObject)으로 옮기면 비교 도구는 '사라짐 + 새로'로 보여 준다.
같은 이름·같은 값의 새 필드가 있으면 값이 제대로 옮겨진 것이다. Unity 메뉴
'Tools/Absorb/직렬화 값 - 기준과 비교'를 먼저 실행한 뒤 저장소 루트에서:
    python Tools/refactor_baseline/pair_check.py
"""
import collections
import sys
import pathlib

sys.stdout.reconfigure(encoding="utf-8")
DIFF = pathlib.Path(__file__).with_name("diff.txt")

gone, new, changed = {}, collections.defaultdict(list), []
for line in DIFF.read_text(encoding="utf-8").splitlines():
    kind, rest = line.split(" | ", 1)
    if kind == "바뀜":  # 형식이 '필드 : 이전 → 이후'라 ' = '로 나누지 않는다(2026-10-14 QA #48)
        changed.append(line)
        continue
    key, value = rest.rsplit(" = ", 1)
    field = key.split(" | ")[-1]
    if kind == "사라짐":
        gone[key] = (field, value)
    elif kind == "새로":
        new[field].append((key, value))
    else:
        changed.append(line)

unmatched = [k for k, (f, v) in gone.items() if not any(val == v for _, val in new.get(f, []))]
gone_fields = {f for f, _ in gone.values()}
extra = [key for f, items in new.items() if f not in gone_fields for key, _ in items]

print(f"바뀜 {len(changed)} / 사라짐 {len(gone)} / 짝 없음 {len(unmatched)} / 출처 없는 새 필드 {len(extra)}")
for line in changed:
    print("  바뀜:", line)
for key in unmatched:
    print("  짝 없음:", key, gone[key][1])
for key in extra:
    print("  새 필드(출처 없음 — 에셋 참조 연결 등 확인):", key)
