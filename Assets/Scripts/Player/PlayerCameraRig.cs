using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 로컬 카메라 추적·흔들림(리팩터 B안 단계 4a, PlayerController에서 분리).
    /// 카메라 추적(2026-09-22 신규) — 방/줌 전환 같은 기획은 아직 미확정이라([PLAYTEST]), 우선 로컬 플레이어를 부드럽게 따라가는
    /// 가장 단순한 형태. 이동/애니메이션이 다 반영된 뒤에 카메라를 옮겨야 하므로 LateUpdate 시점에 처리한다 — 자체 LateUpdate는 두지 않고
    /// PlayerController.LateUpdate 로컬 분기 마지막에서 LateTick을 부른다(원격 플레이어 오브젝트는 카메라를 건드리지 않는다).
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("카메라")]
        [SerializeField] private float cameraFollowSmoothTime = 0.15f; // [PLAYTEST] 카메라 추적 감쇠 시간 — 값이 작을수록 즉각 반응

        private PlayerHealth _health;
        private Vector3 _cameraFollowVelocity;
        private Vector3 _cameraShakeOffset; // 지난 프레임에 카메라에 더한 흔들림 — 추적 기준 위치에 누적되지 않게 매 프레임 빼고 다시 더한다

        // 로컬 카메라 흔들림(보스 D·E 충격파, BossBrain.RpcCameraShake). 로컬 연출이라 static 한 벌이면 된다.
        private static float s_shakeRemaining;
        private static float s_shakeDuration;
        private static float s_shakeAmplitude;

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
        }

        /// <summary>로컬 플레이어 전용 — PlayerController.LateUpdate 로컬 분기 마지막에 불린다.</summary>
        public void LateTick()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // 지난 프레임 흔들림을 빼서 추적 기준 위치로 되돌린 뒤 추적하고, 남은 흔들림을 새로 더한다.
            Vector3 basePosition = cam.transform.position - _cameraShakeOffset;
            Transform followTarget = CameraFollowTarget(); // 쓰러졌으면 살아 있는 팀원 관전, 부활하면 자기 캐릭터로 복귀
            Vector3 desiredPosition = new Vector3(followTarget.position.x, followTarget.position.y, basePosition.z);
            basePosition = Vector3.SmoothDamp(basePosition, desiredPosition, ref _cameraFollowVelocity, cameraFollowSmoothTime);
            _cameraShakeOffset = NextCameraShakeOffset(Time.deltaTime);
            cam.transform.position = basePosition + _cameraShakeOffset;
        }

        /// <summary>
        /// 관전 대상(2026-10-07 쓰러짐·부활): 로컬 플레이어가 쓰러졌으면 가장 가까운 살아 있는 팀원, 없거나 살아 있으면 자기 자신.
        /// </summary>
        private Transform CameraFollowTarget()
        {
            if (_health == null || !_health.IsDead)
            {
                return transform;
            }

            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (PlayerController other in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (other.gameObject == gameObject)
                {
                    continue;
                }

                var otherHealth = other.GetComponent<PlayerHealth>();
                if (otherHealth != null && otherHealth.IsDead)
                {
                    continue;
                }

                float d = Vector2.Distance(transform.position, other.transform.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = other.transform;
                }
            }

            return best != null ? best : transform;
        }

        /// <summary>
        /// 로컬 카메라 흔들림 요청(모든 클라이언트에서 각자 호출). 이미 흔들리는 중이면 더 센 쪽을 쓰고 남은 시간은 긴 쪽을 유지한다.
        /// 세기는 남은 시간 비율로 선형 감쇠한다. 호출부: BossBrain.RpcCameraShake(D 지진 강타·E 포효 대지분쇄),
        /// PlayerCombat.NotifyNearbyHit(히트스톱 흔들림).
        /// </summary>
        public static void RequestCameraShake(float duration, float amplitude)
        {
            if (duration <= 0f || amplitude <= 0f)
            {
                return;
            }

            if (s_shakeRemaining <= 0f || amplitude >= s_shakeAmplitude)
            {
                s_shakeAmplitude = amplitude;
                s_shakeDuration = Mathf.Max(duration, s_shakeRemaining);
                s_shakeRemaining = s_shakeDuration;
            }
            else
            {
                s_shakeRemaining = Mathf.Max(s_shakeRemaining, duration);
                s_shakeDuration = Mathf.Max(s_shakeDuration, s_shakeRemaining);
            }
        }

        private static Vector3 NextCameraShakeOffset(float dt)
        {
            if (s_shakeRemaining <= 0f)
            {
                return Vector3.zero;
            }

            s_shakeRemaining -= dt;
            float strength = s_shakeAmplitude * Mathf.Clamp01(s_shakeRemaining / Mathf.Max(0.0001f, s_shakeDuration));
            Vector2 offset = Random.insideUnitCircle * strength;
            return new Vector3(offset.x, offset.y, 0f);
        }
    }
}
