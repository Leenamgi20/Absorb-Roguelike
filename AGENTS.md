# Absorb_Roguelike Repository Rules

> Rationale: [RULE_RATIONALE.md](RULE_RATIONALE.md). This file = current rules only.

## 0. 응답 언어 (최우선)
- 유저에게 보내는 모든 답변은 **반드시 한국어**로 작성한다. 긴 보고, 요약, 체크리스트, 작업 결과 설명 모두 예외 없음.
- 모델 전환이나 긴 작업 뒤에도 영어로 바뀌지 않도록 매 답변마다 확인한다.
- 파일 경로, 코드 식별자, 커밋 해시, 명령어처럼 원문 그대로 써야 하는 것만 영어 그대로 둔다.

## 1. Markdown file versioning
- Every `.md` file keeps a `## ⏳ 변경 이력 (History)` section at the bottom.
- Each edit appends: date (YYYY-MM-DD), version, one-line summary.
- Versioning: `1.00` on creation → `+0.01` per minor edit → major bump (`2.00`, `3.00`) for large rewrites.
- Keep content/structure accurate.
- Exception: `AGENTS.md` itself skips History — track via `git log -p AGENTS.md`.

## 2. Daily records
- Path: `docs/05_진행/daily_records/YYYY-MM-DD.md` (e.g. `docs/05_진행/daily_records/2026-09-15.md`).
- Header: `# YYYY-MM-DD 데일리 레코드`, then work summary + **AI Instruction Log** section (categorize user commands given that day). Follows rule 1 versioning.
- Write on explicit `/daily_record` trigger only — no live/incremental updates.
- One file per active day.

## 3. Git commands
- AI may run `git add`/`commit`/`push` directly (including push to origin/main), writing its own commit message — no per-action confirmation needed (personal repo, no system impact).
- Commit granularity = one `docs/05_진행/TODO.md` checklist item. Don't commit for every small tweak/fix within an item's work (e.g. a bug found while testing that item) — fold those into the same commit once the item is checked off, or when the user asks.
- No branch workflow by default — work directly on `main`. Solo repo, so branching's usual value (parallel work isolation, pre-review protection) doesn't apply, and commits already provide an undo safety net. Exception: a large, possibly-abandoned experiment (e.g. a from-scratch architecture rewrite) may use a throwaway branch.

## 4. Doc file format
- Default: `.md` for all new docs (including `docs/`) unless user names a format.
- User-specified format wins — save as a real file, not just an Artifact preview.
- Diagrams: Mermaid fences (` ```mermaid `) inside Markdown.
- Existing `.html` docs: keep, don't delete. Updates go into a same-name `.md` conversion (note original filename at top) instead.
- HTML docs follow rule 1 versioning too.

## 5. Commit message format
- Title: `<type>: <한글 요약>` — type만 영어 소문자, 요약은 한국어 명령형(...함/...수정 등 X, "~수정", "~추가"처럼 간결한 서술)으로 쓰고 마침표는 안 붙인다.
- Types: `feat` (new gameplay/system), `fix` (bug fix), `docs` (docs-only), `asset` (Unity scene/prefab/import/package changes with no script logic), `chore` (tooling/config, e.g. `.gitignore`/`.mcp.json`), `refactor` (no behavior change).
- Body: `-` 불릿 리스트, 한국어로 한 줄에 하나씩, 중요한 것부터. 파일 경로·식별자·함수명 등 원문 그대로 써야 하는 것은 영어 그대로 유지.
- One logical unit of work per commit — split unrelated changes (e.g. docs vs Unity project) into separate commits rather than one mixed commit.
- Footer: keep the `Co-Authored-By` line as given in the session's system reminder.

## 6. Development workflow stage tracking
- At session start, read `docs/05_진행/현재_상태.md` first (one-page current state); before touching code, also skim `docs/02_기술/핵심_클래스_다이어그램.md` and the relevant flow in `docs/02_기술/핵심_동작_흐름.md`. Then check current stage via `docs/05_진행/개발_전체_흐름.md`'s "현재 프로젝트 위치" + `docs/05_진행/TODO.md` checklist state.
- If the requested work belongs to a different stage than the current one, flag it and confirm before proceeding (don't block — just surface the mismatch).
- Stage transitions follow `docs/05_진행/개발_전체_흐름.md`'s "전환 조건" table. Irreversible transitions (e.g. launch) always need explicit user approval.
- When a stage transition happens, update "현재 프로젝트 위치" in `docs/05_진행/개발_전체_흐름.md` to match.
- While in stage 1(기획)/2(프리프로덕션), also check `docs/01_기획/기획_누락_추적.md` for open planning gaps relevant to the current work.

## 7. Planning-gap tracking (`docs/01_기획/기획_누락_추적.md`)
- GDD/기획 문서에 `[OPEN QUESTION]`/`[Assumption]`/`[PLAYTEST]` 태그가 추가되거나 해소될 때마다 `docs/01_기획/기획_누락_추적.md`를 재스캔해 갱신한다 (스캔 명령은 그 문서 상단 참고).
- 태그가 이미 다른 문서(ADR, 결정 대기 체크리스트 등)에서 해소됐는데 원문에 안 지워진 경우, 발견 즉시 원문을 정정한다 (사소한 사실 정정은 유저 확인 없이 진행 가능).

## 8. TODO verification standard
- `docs/05_진행/TODO.md` 항목을 `[x]`로 체크할 때, 어떻게 검증했는지 괄호로 명시한다. 예: `(검증: 호스트 단독 플레이)`, `(검증: 2인 협동 테스트)`, `(검증: PlayMode 테스트 <이름>)`.
- 검증 방법이 "호스트 단독 플레이"뿐인 항목은 QA 단계(4단계) 진입 전 더 강한 검증(2인 테스트 또는 자동 테스트)이 필요하다는 뜻으로 취급한다.

## 9. 작업 방식 선택 & 유저 관여 묶기
- **작업 크기로 방식을 고른다**:
  - 작은 항목(파일 2~3개 수준: 버튼 추가, 수치 조정, 버그 한 건, 문서 정리) → 마스터 세션이 직접 구현하고 Unity MCP로 상태값까지 확인.
  - 중간 이상(여러 시스템에 걸친 기능) → `.claude/workflows/feature-pipeline.js`를 백그라운드로 실행(`scriptPath`로 이 파일을 지정). 단계별 모델: 기획·구현·검수 Opus 5.5 중간, 문서 Sonnet 5.5 중간. 결과는 요약만 돌려받는다. 그동안 마스터 세션은 다른 작업/논의를 진행.
  - 빌드·릴리즈 → 에이전트 워크플로우를 쓰지 않는다. 마스터가 Unity MCP로 빌드(사용자 Play 중이면 기다림)하고, zip·업로드는 백그라운드 셸 명령으로.
  - 이유·실측·절차: `docs/06_작업방식/토큰_절약_전략.md` (토큰 절약 전략 A·B·C·D).
  - Unity 에디터는 하나뿐이다 — 파이프라인이 에디터를 쓰는 동안 마스터 세션은 Play/씬 조작을 하지 않고, 파이프라인을 동시에 두 개 돌리지 않는다. 사용자가 Play 중일 때는 누구도 컴파일·Play 중지를 하지 않는다.
- **유저가 관여할 일은 몰아서 한 번에 가져온다**. 작업마다 끊어서 묻지 않는다.
- 가져올 때는 **"플레이로 확인할 것"**과 **"결정할 것(선택)"**을 섹션을 나눠 명확히 구분한다. 결정할 것은 선택지와 추천을 함께 준다.
- 진행을 막는 결정(이게 없으면 다음 작업을 못 함)만 예외로 바로 묻는다.

## 10. QA 이슈·워크플로우 실행 기록
- 플레이 확인·테스트에서 나온 문제는 `docs/05_진행/QA_이슈_기록.md`에 한 줄씩 남긴다: 증상 → 원인 → 해결 → 상태(해결/확인 대기/미해결/비코드)·커밋. 고치기 전이라도 증상부터 적는다. 설정·환경 문제도 '비코드'로 남긴다.
- 백그라운드 워크플로우(기능 파이프라인, 빌드·릴리즈 등)는 끝날 때마다(실패 포함) `docs/05_진행/워크플로우_실행_기록.md`에 날짜·이름·목적·결과·산출물을 한 줄 남긴다. 실행 원본은 AI 세션 폴더에만 있어 사라지기 때문.

## 11. 문서 위치 (docs 분류)
- `docs/README.md`가 문서 지도다. 새 문서는 그 표의 분류(01_기획·02_기술·03_아트·04_구현위키·05_진행·06_작업방식)에 맞는 폴더에 두고, 만든 즉시 `docs/README.md` 전체 목록에 한 줄 추가한다.
- 끝난 작업의 경위·지난 스냅샷은 현행 문서에 쌓지 않고 `docs/99_아카이브/`로 보낸다. 현황은 `현재_상태.md`·`TODO.md`에서 끝난 줄을 지우거나 체크해 짧게 유지한다.
- 수치·규칙은 한 곳(GDD 또는 구현 위키)에만 쓰고 다른 문서는 링크한다.

## 12. 구조 문서 동기화 (코드와 같은 커밋)
- 코드 구조가 바뀌면 **그 코드 변경과 같은 커밋**에서 아래 문서를 같이 고친다. 커밋 전에 "구조가 바뀌었나?"를 확인한다.

| 바뀐 것 | 고칠 문서 |
|---|---|
| `Assets/Scripts` 파일 추가·삭제·이동, 파일 역할 변경 | `docs/02_기술/코드_지도.md` |
| 핵심 클래스(다이어그램에 있는 것) 추가·삭제, 클래스 간 참조·상속·컴포넌트 관계 변경, 노드에 적힌 멤버 이름 변경 | `docs/02_기술/핵심_클래스_다이어그램.md` → `python Tools/sync_diagram_html.py`로 `.html` 반영 |
| 공격·흡수·보스 선택·피격/부활·층 이동의 호출 순서, 클라/서버 경계(`Cmd`/`SyncVar`/`Rpc`) 변경 | `docs/02_기술/핵심_동작_흐름.md` |
- 수치 조정·연출·버그 수정처럼 구조가 그대로면 고치지 않는다.
- 문서와 코드가 다른 것을 발견하면 코드가 맞는 것으로 보고 즉시 문서를 고친다(유저 확인 불필요).
- 정확한 줄 수처럼 금방 낡는 값은 적지 않는다.
