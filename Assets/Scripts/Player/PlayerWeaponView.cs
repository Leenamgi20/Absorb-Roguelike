using System.Collections;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 대검 무기 그림·베기 이펙트 표시(리팩터 B안 단계 4a, PlayerController에서 분리). 판정과 무관한 연출 전용.
    /// 자체 Update류는 두지 않는다 — PlayerController가 기존 실행 순서 그대로 직접 부른다(FixedUpdate의 무기 방향, LateUpdate 원격 방향,
    /// 공격 루프·RpcAttackFx의 휘두르기). 공격 상태·흡수 스킬·스윙 수치는 PlayerController 읽기 전용 프로퍼티로 읽는다.
    /// </summary>
    public class PlayerWeaponView : MonoBehaviour
    {
        [Header("무기 표시 (대검)")]
        [SerializeField] private Transform weaponTransform;
        [SerializeField] private float weaponAnchorOffset = 0.2f; // 무기 그립(피벗) 위치 — 캐릭터 중심에서 이 거리만큼 떨어진 곳에 고정, 스프라이트는 여기서부터 바깥으로 뻗어나감
        [Tooltip("손잡이 위치 보정(칸). 슬라임 그림은 몸이 칸 아래쪽에 있어 몸 중심이 칸 중심보다 조금 낮다(2026-10-06 아트 적용).")]
        [SerializeField] private Vector2 weaponAnchorBias = new Vector2(0f, -0.08f);
        [Tooltip("이 값보다 위쪽을 볼 때는 무기를 몸 뒤로 그린다(위를 향해 들면 몸에 가려지는 게 자연스럽다).")]
        [SerializeField] private float weaponBehindFacingY = 0.35f;

        [Header("대검 베기 이펙트 (2026-10-07, 사용자 에셋 SwordAnimationaseprite)")]
        [Tooltip("휘두르는 동안 순서대로 보여 줄 프레임(칼 + 흰 궤적이 한 그림, 128×128, 오른쪽을 향해 위→아래로 베는 그림). 순서 = 칼을 든 3 → 0 → 1 → 2(끝). 피벗은 그림 속 손잡이(53,72px). 비어 있으면 예전처럼 무기만 돌린다.")]
        [SerializeField] private Sprite[] slashFrames;
        [SerializeField] private float slashScale = 1f;
        [SerializeField] private int slashSortingOrder = 12;

        private const float RemoteFacingSharpness = 25f; // 원격 무기 방향 따라가는 빠르기(클수록 빨리 붙음)

        private PlayerController _controller;
        private SpriteRenderer _weaponSpriteRenderer;
        private SpriteRenderer _slashRenderer; // 베기 이펙트(런타임 생성 자식)
        private Color _weaponDefaultColor;
        private Coroutine _remoteSwingCoroutine; // 비소유 오브젝트의 대검 스윙 연출(RPC 수신)
        private bool _remoteSwinging;

        /// <summary>무기 그림 트랜스폼(WeaponElementAura는 PlayerController.WeaponTransform 위임으로 읽는다).</summary>
        public Transform WeaponTransform => weaponTransform;

        public SpriteRenderer WeaponSpriteRenderer => _weaponSpriteRenderer;

        public Color WeaponDefaultColor => _weaponDefaultColor;

        /// <summary>관찰자 화면에서 부드럽게 따라가는 무기 방향(연출 전용).</summary>
        public Vector2 RemoteFacing { get; set; } = Vector2.down;

        public bool IsRemoteSwinging => _remoteSwinging;

        public bool HasSlashFx => slashFrames != null && slashFrames.Length > 0;

        private PlayerController Controller
        {
            get
            {
                if (_controller == null)
                {
                    _controller = GetComponent<PlayerController>();
                }

                return _controller;
            }
        }

        /// <summary>무기 렌더러·기본색 캐시. PlayerController.Awake의 기존 자리에서 부른다(컴포넌트 Awake 순서 비의존).</summary>
        public void CacheRenderers()
        {
            if (weaponTransform != null)
            {
                _weaponSpriteRenderer = weaponTransform.GetComponent<SpriteRenderer>();
                if (_weaponSpriteRenderer != null)
                {
                    _weaponDefaultColor = _weaponSpriteRenderer.color;
                }
            }
        }

        /// <summary>로컬 플레이어: FixedUpdate에서 마우스 방향 갱신 직후 불린다.</summary>
        public void UpdateWeaponFacing(Vector2 facing)
        {
            // 용 브레스는 휘두르기 연출이 없어 쏘는 동안에도 무기가 마우스를 따라 돈다(2026-10-07 — 틱 사이에 멈춰 뚝뚝 끊기던 문제).
            // 단계 5c: '용' 조건 = 현재 스킬의 WeaponFollowsFacingWhileAttacking(DragonBreathSkill).
            AbsorbSkillHost host = Controller.SkillHost;
            AbsorbSkillBase skill = host != null ? host.Current : null;
            if (Controller.IsAttacking && (skill == null || !skill.WeaponFollowsFacingWhileAttacking))
            {
                return; // 공격(스윙) 중에는 AttackRoutine이 무기 회전을 직접 제어한다.
            }

            ApplyWeaponFacing(facing);
        }

        /// <summary>원격 플레이어 오브젝트: 무기 방향은 0.05초마다 오는 값을 그대로 쓰면 뚝뚝 꺾이므로 받은 방향 쪽으로 부드럽게 돌린다(2026-10-06).</summary>
        public void TickRemoteFacing(Vector2 syncedFacing, float deltaTime)
        {
            float t = 1f - Mathf.Exp(-RemoteFacingSharpness * deltaTime);
            RemoteFacing = Vector3.Slerp(RemoteFacing, syncedFacing, t);
            ApplyWeaponFacing(RemoteFacing);
        }

        public Vector3 WeaponAnchor(Vector2 facing)
        {
            return (Vector3)(facing * weaponAnchorOffset + weaponAnchorBias);
        }

        // 위를 볼 때는 몸(정렬 10) 뒤(9), 그 외에는 앞(11).
        public void UpdateWeaponLayer(Vector2 facing)
        {
            if (_weaponSpriteRenderer != null)
            {
                _weaponSpriteRenderer.sortingOrder = facing.y > weaponBehindFacingY ? 9 : 11;
            }
        }

        // 베기 이펙트 한 프레임: 휘두르는 동안 들고 있던 무기 그림을 숨기고, 칼+궤적 프레임 전체를 바라보는 방향으로 돌려 보여 준다.
        // 그림은 오른쪽 기준이라 왼쪽을 볼 때는 위아래를 뒤집어(= 좌우 거울) 베는 방향이 항상 위→아래가 되게 한다.
        public void ShowSlashFrame(Vector2 facing, float t)
        {
            if (!HasSlashFx)
            {
                return;
            }

            if (_slashRenderer == null)
            {
                var go = new GameObject("SlashFx");
                go.transform.SetParent(transform, false);
                _slashRenderer = go.AddComponent<SpriteRenderer>();
                var body = GetComponent<SpriteRenderer>();
                if (body != null)
                {
                    _slashRenderer.sortingLayerID = body.sortingLayerID;
                }
            }

            int index = Mathf.Clamp((int)(t * slashFrames.Length), 0, slashFrames.Length - 1);
            _slashRenderer.sprite = slashFrames[index];
            _slashRenderer.sortingOrder = slashSortingOrder;
            _slashRenderer.flipY = facing.x < 0f;
            // 2026-10-08: 베기 그림의 피벗 = 그림 속 칼 손잡이(임포트 설정) → 들고 있던 무기와 같은 손 위치에 놓아 칼이 따로 떠 보이지 않게
            _slashRenderer.transform.localPosition = WeaponAnchor(facing);
            float spin = SpinsDuringSwing() ? t * 360f : 0f; // 뱀(원 전체 — AttackSpec.SpinWeapon): 휘두르는 동안 한 바퀴 돈다
            _slashRenderer.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg + (facing.x < 0f ? spin : -spin)); // 그림이 시계 방향으로 베므로 같은 방향으로 계속 돈다(왼쪽은 뒤집혀 반대)
            _slashRenderer.transform.localScale = Vector3.one * slashScale;
            _slashRenderer.enabled = true;
            if (_weaponSpriteRenderer != null)
            {
                _weaponSpriteRenderer.enabled = false;
            }
        }

        // 현재 흡수 스킬의 스윙 형태가 '한 바퀴 회전'인가(단계 5a — SkillHost.Current.ModifyAttack). 원격 화면도 동기화된 스킬로 장착한 Host 기준.
        private bool SpinsDuringSwing()
        {
            AbsorbSkillHost host = Controller.SkillHost;
            AbsorbSkillBase skill = host != null ? host.Current : null;
            if (skill == null)
            {
                return false;
            }

            var spec = new AttackSpec();
            skill.ModifyAttack(ref spec);
            return spec.SpinWeapon;
        }

        public void HideSlash()
        {
            if (_slashRenderer != null)
            {
                _slashRenderer.enabled = false;
            }

            if (_weaponSpriteRenderer != null)
            {
                _weaponSpriteRenderer.enabled = true;
            }
        }

        public void ApplyWeaponFacing(Vector2 facing)
        {
            // 스윙이 중간에 끊긴 경우(대시·가드로 코루틴 중단)에도 무기 그림이 돌아오게 — 스윙 밖에서만 불린다.
            if (_slashRenderer != null && _slashRenderer.enabled)
            {
                HideSlash();
            }

            if (weaponTransform == null || facing.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f;
            weaponTransform.rotation = Quaternion.Euler(0f, 0f, angle);
            weaponTransform.localPosition = WeaponAnchor(facing.normalized);
            UpdateWeaponLayer(facing);
        }

        // 비소유 오브젝트의 대검 휘두르기(PerformSingleSwing의 무기 회전·색 연출과 같은 모양). 도중에 새 스윙이 오면 처음부터 다시.
        // 호출부: PlayerController.RpcAttackFx(SwingCone·EarWhip).
        public void PlayRemoteSwing(Vector2 facing, bool slash)
        {
            if (weaponTransform == null || !isActiveAndEnabled)
            {
                return;
            }

            if (_remoteSwingCoroutine != null)
            {
                StopCoroutine(_remoteSwingCoroutine);
            }

            _remoteSwingCoroutine = StartCoroutine(RemoteSwingRoutine(facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector2.down, slash));
        }

        private IEnumerator RemoteSwingRoutine(Vector2 facing, bool slash)
        {
            _remoteSwinging = true;
            float swingDuration = Controller.AttackSwingDuration;
            float arcDegrees = Controller.AttackSwingArcDegrees;
            float baseAngle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f;
            float startAngle = baseAngle - arcDegrees * 0.5f;
            float endAngle = baseAngle + arcDegrees * 0.5f;
            weaponTransform.localPosition = WeaponAnchor(facing);
            UpdateWeaponLayer(facing);
            if (_weaponSpriteRenderer != null)
            {
                _weaponSpriteRenderer.color = Controller.AttackFlashColor;
            }

            float elapsed = 0f;
            while (elapsed < swingDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / swingDuration);
                weaponTransform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startAngle, endAngle, t));
                if (slash)
                {
                    ShowSlashFrame(facing, t);
                }

                yield return null;
            }

            HideSlash();
            if (_weaponSpriteRenderer != null)
            {
                _weaponSpriteRenderer.color = _weaponDefaultColor;
            }

            _remoteSwinging = false;
            _remoteSwingCoroutine = null;
        }
    }
}
