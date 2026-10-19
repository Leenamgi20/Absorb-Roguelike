export const meta = {
  name: 'feature-pipeline',
  description: 'Plan → Dev → QA → Doc 파이프라인 하나의 기능/TODO 항목을 명세→구현→검증→요약까지 자동 진행',
  whenToUse: 'docs/05_진행/TODO.md의 항목 하나(또는 새 기능 요청)를 args.spec으로 넘기면, 투두/QA시나리오 계획 → 구현 → 저비용 정적 QA(컴파일 확인+코드 리뷰만, Unity 반복 캡처 없음) → 위키형 문서화까지 진행. 실제 플레이 검증은 QA 시나리오 체크리스트로 사람에게 넘긴다(비용/시간 이유로 AI가 반복 플레이 검증을 하지 않기로 결정). 정적 QA 실패 시 최대 2회까지 Dev로 되돌리고, 그래도 실패하면 사람(마스터 세션) 확인이 필요하다고 보고하며 멈춘다.',
  phases: [
    { title: 'Plan', detail: '명세를 투두리스트 + QA 시나리오로 분해, 범위 밖 명시' },
    { title: 'Dev', detail: '투두리스트만 보고 구현, 완료마다 근거 남김' },
    { title: 'QA', detail: '저비용 정적 점검만 (컴파일 로그 1회 + 코드 리뷰) — 실제 플레이 검증은 사람 몫' },
    { title: 'Doc', detail: 'TODO.md 갱신(체크는 사용자 플레이 확인 후) + 경량 위키 MD 작성 + 구조 문서 동기화(AGENTS 12번)' },
  ],
}

const spec = args && args.spec
if (!spec) {
  throw new Error('args.spec 이 필요합니다 (예: { spec: "docs/05_진행/TODO.md의 보스 1기 구현 항목 마무리" })')
}
const rawMaxRetries = args && args.maxRetries
const MAX_RETRIES = typeof rawMaxRetries === 'number' ? rawMaxRetries : 2

const PLAN_SCHEMA = {
  type: 'object',
  properties: {
    tasks: {
      type: 'array',
      items: {
        type: 'object',
        properties: { id: { type: 'string' }, description: { type: 'string' } },
        required: ['id', 'description'],
      },
    },
    outOfScope: { type: 'array', items: { type: 'string' } },
    qaScenarios: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          description: { type: 'string' },
          expected: { type: 'string' },
        },
        required: ['id', 'description', 'expected'],
      },
    },
  },
  required: ['tasks', 'qaScenarios'],
}

const DEV_SCHEMA = {
  type: 'object',
  properties: {
    completed: {
      type: 'array',
      items: {
        type: 'object',
        properties: { id: { type: 'string' }, evidence: { type: 'string' } },
        required: ['id', 'evidence'],
      },
    },
    incomplete: {
      type: 'array',
      items: {
        type: 'object',
        properties: { id: { type: 'string' }, reason: { type: 'string' } },
        required: ['id', 'reason'],
      },
    },
  },
  required: ['completed', 'incomplete'],
}

const QA_SCHEMA = {
  type: 'object',
  properties: {
    compileClean: { type: 'boolean' },
    consoleIssues: { type: 'array', items: { type: 'string' } },
    codeReview: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          taskId: { type: 'string' },
          ok: { type: 'boolean' },
          note: { type: 'string' },
        },
        required: ['taskId', 'ok', 'note'],
      },
    },
    blockingIssuesFound: { type: 'boolean' },
  },
  required: ['compileClean', 'codeReview', 'blockingIssuesFound'],
}

// 결과 요약(토큰 절약 C, docs/06_작업방식/토큰_절약_전략.md): 명세·계획·전체 리뷰를 그대로 돌려주면 마스터 창 문맥이 불어난다 — 판단에 필요한 것만 반환.
function compact(doc) {
  const review = (qaResult && qaResult.codeReview) || []
  return {
    succeeded, attempt,
    tasks: plan.tasks.length, qaScenarios: plan.qaScenarios.length,
    incomplete: devResult ? devResult.incomplete : [],
    compileClean: qaResult ? qaResult.compileClean : null,
    blockingIssuesFound: qaResult ? qaResult.blockingIssuesFound : null,
    failedReview: review.filter(c => !c.ok),
    humanCheckNotes: review.filter(c => c.ok && /사람|확인 필요/.test(c.note)).map(c => `${c.taskId}: ${c.note}`),
    consoleIssues: qaResult ? qaResult.consoleIssues : [],
    doc,
  }
}
phase('Plan')
const plan = await agent(
  `다음 기능 명세를 Absorb_Roguelike 프로젝트(Unity, docs/05_진행/TODO.md 체크리스트 관행을 따름)의 실제 구현 투두리스트로 쪼개라.

명세: ${spec}

참고 문서(직접 읽어서 컨벤션/기존 결정과 맞춰라): docs/05_진행/TODO.md, docs/01_기획/Absorb_GDD_v01.md, docs/01_기획/기획_누락_추적.md

규칙:
- "구현해야 할 것"과 "절대 구현하지 말아야 할 범위(outOfScope)"를 명확히 분리해라.
- 각 태스크는 코드 한 곳(파일/시스템)에 대응하는 작은 단위로 쪼개라.
- QA 시나리오는 사람이 실제로 플레이해서 확인할 수 있는 구체적 행동+기대 결과로 써라 (예: "대검으로 가드 중 정면에서 피격 → 대미지 0").
- 이미 GDD/기획_누락_추적.md에서 확정된 내용과 모순되는 태스크를 만들지 마라.`,
  { schema: PLAN_SCHEMA, phase: 'Plan', model: 'opus', effort: 'medium' }
)
if (!plan) throw new Error('Plan 단계 실패')
log(`계획 완료: 태스크 ${plan.tasks.length}개, QA 시나리오 ${plan.qaScenarios.length}개`)

let devResult = null
let qaResult = null
let attempt = 0
let feedback = ''

while (attempt <= MAX_RETRIES) {
  attempt++
  devResult = await agent(
    `다음 투두리스트를 구현해라 (Unity C# 프로젝트, AGENTS.md 저장소 규칙 준수 — 특히 git 커밋은 하지 말고 파일만 수정해라, 커밋은 이후 별도로 처리한다).

태스크: ${JSON.stringify(plan.tasks)}
범위 밖(구현 금지): ${JSON.stringify(plan.outOfScope || [])}
${feedback ? `이전 시도 QA 실패/미완료 피드백 (이번엔 이걸 우선 해결해라): ${feedback}` : ''}

각 태스크를 완료하면 근거(수정한 파일:라인, 또는 Unity MCP로 확인한 콘솔 로그 요약)를 남겨라. 완료 못 한 태스크는 이유를 명시해라.

씬/프리팹/GameObject/컴포넌트 배치처럼 Unity 에디터 상태를 바꿔야 하는 작업은 .scene/.prefab YAML 파일을 직접 손으로 편집하지 말고, unity-mcp-skill(Unity MCP RunCommand 등, mcp__UnityMCP__* 또는 mcp__unity-mcp__* 툴을 ToolSearch로 로드)을 통해 에디터에서 직접 조작해라 — 손편집은 GUID 참조를 깨뜨릴 수 있다. 순수 C# 스크립트 로직은 일반 Edit로 해도 된다.`,
    { schema: DEV_SCHEMA, phase: 'Dev', agentType: 'general-purpose', model: 'opus', effort: 'medium' }
  )
  if (!devResult) { log('Dev 에이전트 실패 — 중단'); break }
  log(`Dev 시도 ${attempt}: 완료 ${devResult.completed.length}개 / 미완료 ${devResult.incomplete.length}개`)

  qaResult = await agent(
    `너는 이 작업과 독립적인 검수자(리뷰어) 역할이다. **어떤 이유로든 파일을 수정/생성/삭제하지 마라 (Edit, Write, 파일을 바꾸는 Bash 명령 전부 금지) — 이 세션은 오직 검증과 보고만 한다.** 버그나 미완성을 발견해도 절대 스스로 고치지 말고 실패로 보고해라. 코드를 수정하면 검증 결과 전체가 무효가 된다.

이번 QA는 "실제 플레이 검증"이 아니라 **저비용 정적 점검**이다 — Unity를 반복적으로 조작하거나 Play 모드 캡처를 여러 번 찍지 마라 (비용/시간이 너무 든다는 게 이 설계를 바꾼 이유다). 딱 두 가지만 해라:
1. mcp__UnityMCP__* 또는 mcp__unity-mcp__*의 GetConsoleLogs를 **딱 한 번만** 호출해서 컴파일 에러/경고 유무를 확인해라.
2. Dev가 수정/생성한 파일을 코드로 읽어서, 각 태스크 구현이 아래 QA 시나리오가 기대하는 동작과 논리적으로 맞는지 정적으로 판단해라(실행해서 확인하지 마라 — 실제 플레이 검증은 이후 사람이 한다). 명백한 로직 결함(널 참조 위험, 이벤트 미배선, 시나리오와 반대로 동작하는 코드 등)만 찾아라. 애매한 건 결함으로 치지 말고 note에 "사람 확인 필요"로만 남겨라.

태스크: ${JSON.stringify(plan.tasks)}
QA 시나리오(참고용 — 코드가 이 기대와 논리적으로 맞는지만 판단, 실행 금지): ${JSON.stringify(plan.qaScenarios)}
개발 완료 보고: ${JSON.stringify(devResult.completed)}
개발 미완료 보고: ${JSON.stringify(devResult.incomplete)}`,
    { schema: QA_SCHEMA, phase: 'QA', agentType: 'general-purpose', model: 'opus', effort: 'medium' }
  )
  if (!qaResult) { log('QA 에이전트 실패 — 중단'); break }
  log(`QA 시도 ${attempt}: 컴파일 ${qaResult.compileClean ? '클린' : '에러 있음'}, 결함 발견 ${qaResult.blockingIssuesFound ? '있음' : '없음'}`)

  if (!qaResult.blockingIssuesFound && qaResult.compileClean && devResult.incomplete.length === 0) {
    log(`정적 QA 통과 (시도 ${attempt}회 만에) — 실제 플레이 검증은 사람이 남은 QA 시나리오로 진행`)
    break
  }

  feedback = JSON.stringify({
    codeReviewIssues: qaResult.codeReview.filter(c => !c.ok),
    consoleIssues: qaResult.consoleIssues,
    incompleteDev: devResult.incomplete,
  })
  log(`정적 QA 실패/미완료 항목 있음 (시도 ${attempt}/${MAX_RETRIES + 1}) — Dev로 롤백`)
}

const succeeded = !!(qaResult && !qaResult.blockingIssuesFound && qaResult.compileClean && devResult && devResult.incomplete.length === 0)
if (!succeeded) {
  log(`${MAX_RETRIES + 1}회 시도 후에도 미해결 — 마스터 AI(사용자 세션) 확인이 필요합니다. 위키 문서화는 건너뜁니다.`)
  return compact(null)
}

phase('Doc')
const doc = await agent(
  `아래 작업 내용을 정리해라. 두 가지를 실제로 파일에 반영해라. 지금까지 통과한 건 "저비용 정적 검증"(컴파일 확인 + 코드 리뷰)뿐이고 **실제 플레이 검증은 아직 사람이 안 했다** — 이 사실을 정확히 반영해라, 다 끝난 것처럼 쓰지 마라.

1. docs/05_진행/TODO.md에서 이 작업(명세: "${spec}")에 해당하는 항목을 찾아, 구현이 끝났고 정적 검증(컴파일 클린 + 코드 리뷰)을 통과했다는 내용을 이어서 적어라. **체크박스는 아직 [ ]로 남겨두고**, AGENTS.md 8번 규칙에 맞춰 "(검증: AI 정적 확인 완료 — 컴파일 클린/코드 리뷰, 사용자 플레이 테스트 대기 중 — QA 시나리오 ${plan.qaScenarios.length}개)"라고 명시해라. 해당하는 기존 항목이 없으면 알맞은 단계 아래에 새 항목으로 추가해라. 사람이 실제로 플레이해서 확인한 뒤 [x]로 바꾸고 검증 태그를 갱신하는 건 이후 별도로 처리한다. 문서 맨 아래 "## ⏳ 변경 이력 (History)" 섹션에 AGENTS.md 1번 규칙대로 엔트리를 한 줄 추가해라(버전 +0.01).
2. docs/04_구현위키/ 폴더(없으면 새로 만들어라)에 이 기능을 설명하는 나무위키형 경량 Markdown 문서를 작성해라. 전체 코드를 나열하지 말고 구조/흐름/왜 이렇게 만들었는지 위주로 간결하게. QA 시나리오 목록을 "사람이 플레이로 확인해야 할 체크리스트" 섹션으로 그대로 포함해라. 파일명은 먼저 docs/04_구현위키/를 훑어보고 기존 파일과 안 겹치는 짧은 이름으로 정해라(예: docs/04_구현위키/보스1_구현.md). AGENTS.md 1번 규칙대로 "## ⏳ 변경 이력 (History)" 섹션을 포함해라(신규 파일이므로 v1.00).
3. AGENTS.md 12번(구조 문서 동기화) 표를 읽고, 이번 구현(git diff로 확인)이 그 표의 "바뀐 것"에 해당하면 해당 문서를 실제 코드에 맞게 고쳐라: docs/02_기술/코드_지도.md, docs/02_기술/핵심_클래스_다이어그램.md(고쳤으면 python Tools/sync_diagram_html.py 실행), docs/02_기술/핵심_동작_흐름.md. 각 문서 History에 한 줄 추가. 해당 없으면 "구조 문서 변경 없음"이라고 반환에 적어라.

명세: ${spec}
투두리스트: ${JSON.stringify(plan.tasks)}
개발 완료 근거: ${JSON.stringify(devResult.completed)}
정적 QA 결과: ${JSON.stringify(qaResult)}
사람이 플레이로 확인할 QA 시나리오: ${JSON.stringify(plan.qaScenarios)}

git 커밋은 하지 마라 — 이후 마스터 세션에서 diff를 검토하고 커밋한다. 수정/생성한 파일 경로 목록을 반환해라.`,
  { phase: 'Doc', model: 'sonnet', effort: 'medium' }
)

return compact(doc)
