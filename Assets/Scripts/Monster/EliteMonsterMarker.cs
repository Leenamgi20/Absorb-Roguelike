using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 정예몹 스탯/표식 적용(결정 대기 체크리스트 D, 2026-09-17 확정 수치: 체력 3배 / 공격력 1.5배).
    /// GDD 원칙대로 정예몹은 별도 원형을 만들지 않고 잡몹 원형(MonsterStub + MonsterBehaviorController)을
    /// 그대로 재사용하되, 이 컴포넌트를 같이 붙이고 MonsterStub의 "정예몹(Elite)" 체크박스를 켜면
    /// 스탯 배율과 시각 표식(주황 색상 틴트 + 셰이더 1px 외곽선)이 적용된다. 크기 확대는 하지 않는다 — 비정수 배율은
    /// 도트가 뭉개지므로 금지(docs/03_아트/아트_UI_규격.md).
    /// 외곽선(2026-10-15, 비주얼 3단계 V8): 예전 사본 4장(1px 밀기 + 단색 실루엣) 대신 몸 렌더러 재질을 `Fx/SpriteOutline`으로
    /// 바꾼다(VisualFx.ApplyOutline(Elite) — 색은 VisualFxConfig.eliteOutlineColor, 몸 틴트와 무관, 비주얼 효과 토글과 무관하게 항상).
    /// 시체가 되면(MonsterStub.IsDefeated) 외곽선 없는 원래 재질로 되돌려 살아있는 정예몹과 구분한다. 각 화면 로컬.
    /// </summary>
    [RequireComponent(typeof(MonsterStub))]
    public class EliteMonsterMarker : NetworkBehaviour
    {
        [Tooltip("정예 배율 에셋(리팩터 C4 — Assets/Data/Monsters/EliteConfig.asset, 12종 공용).")]
        [SerializeField] private EliteConfig eliteConfig;
        [SerializeField] private Color eliteTintColor = new Color(1f, 0.55f, 0.1f); // 임시 표식: 주황 톤

        private SpriteRenderer _body;
        private MonsterStub _stub;
        private bool _outlineRemoved;

        public override void OnStartServer()
        {
            MonsterStub stub = GetComponent<MonsterStub>();
            if (eliteConfig == null)
            {
                // 리팩터 C4: 코드 기본값으로 대신 돌리지 않는다(AbsorbSkillHost 5c와 같은 방침) — 배율 없이 스폰된다.
                Debug.LogError($"[EliteMonsterMarker] {name}: eliteConfig 참조가 비어 있다 — 프리팹의 EliteMonsterMarker에 Assets/Data/Monsters/EliteConfig 에셋을 연결해야 한다.", this);
                stub.ApplyDifficultyHealthScale(); // 층·인원 배율은 정예 설정과 무관하게 적용
                return;
            }

            // 순서: 정예 배율 → 층 × 인원 배율(MonsterStub.OnStartServer는 마커가 있으면 건너뛴다 — 컴포넌트 순서와 무관).
            stub.ApplyEliteHealthMultiplier(eliteConfig.healthMultiplier);
            stub.ApplyDifficultyHealthScale();

            MonsterBehaviorController behavior = GetComponent<MonsterBehaviorController>();
            if (behavior != null)
            {
                behavior.ApplyEliteDamageMultiplier(eliteConfig.attackDamageMultiplier);
            }
        }

        private void Start()
        {
            // 시각 표식은 모든 접속자 화면에 보여야 하므로 서버 체크 없이 각 클라이언트에서 로컬로 적용한다.
            _stub = GetComponent<MonsterStub>();
            _body = GetComponentInChildren<SpriteRenderer>();
            if (_body == null)
            {
                return;
            }

            _body.color = eliteTintColor;
            if (_stub != null && _stub.IsDefeated)
            {
                _outlineRemoved = true; // 이미 시체로 스폰(늦은 참가) — 외곽선 없이
                return;
            }

            AbsorbRoguelike.UI.VisualFx.ApplyOutline(_body, AbsorbRoguelike.UI.OutlineKind.Elite);
        }

        // 시체가 되면 외곽선 없는 원래 재질로 되돌려 살아있는 정예몹과 구분(한 번만).
        private void LateUpdate()
        {
            if (_outlineRemoved || _body == null || _stub == null || !_stub.IsDefeated)
            {
                return;
            }

            _outlineRemoved = true;
            AbsorbRoguelike.UI.VisualFx.RemoveOutline(_body);
        }
    }
}
