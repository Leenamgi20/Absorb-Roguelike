using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Monster;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 디졸브 잔상(2026-10-15, 비주얼 퀄리티 3단계 V9). 사라지는 몸 그림을 복사한 로컬 GameObject를 씬 루트에 만들고
    /// `Resources/Fx/SpriteDissolve` 재질로 VisualFxConfig.dissolveDuration 동안 _Progress 0→1(도트 블록 단위로 부서짐 + 빛나는 가장자리)을
    /// 재생한 뒤 스스로 지운다. 네트워크 컴포넌트 없음 — 각 화면이 자기 훅에서 따로 만든다(원격 2P 화면도 같은 조건).
    /// 훅: MonsterStub.OnStopClient(잡몹 처치·잡몹 흡수·정예 시체 전원 흡수 → 모두 NetworkServer.Destroy), BossStub 시체 감추기(흡수한 사람 화면).
    /// 서버 파괴 시점·순서는 그대로 — 잔상은 파괴 직전 그림의 복사본일 뿐이다.
    /// 만들지 않는 경우: 효과 꺼짐(VisualFx.Enabled), 그림 없음/꺼짐, 앱 종료·씬 로딩·클라이언트 정지, 소스 씬 언로드 중,
    /// 층 이동 중(<see cref="SuppressUntil"/> 억제창 — FloorProgression.AdvanceFloor가 일괄 파괴 전에 켠다), 카메라 화면 밖.
    /// 원격 클라이언트는 층 이동 일괄 파괴(ObjectDestroyMessage)가 층 번호 SyncVar보다 먼저 같은 프레임에 도착하므로,
    /// 만든 프레임의 LateUpdate에서 층 번호가 바뀌었으면(=층 이동 일괄 파괴) 그려지기 전에 지운다.
    /// </summary>
    public class DissolveGhost : MonoBehaviour
    {
        private static Shader _shader;
        private static bool _shaderLoaded;
        private static float _suppressUntil = -1f; // Time.unscaledTime 기준
        private static bool _quitting;

        private SpriteRenderer _renderer;
        private Material _material;
        private float _duration;
        private float _elapsed;
        private int _floorAtSpawn;
        private bool _checked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _suppressUntil = -1f;
            _quitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => _quitting = true;

        /// <summary>이 시각(Time.unscaledTime)까지 잔상을 만들지 않는다(층 이동 일괄 파괴 가드). 더 늦은 값만 반영.</summary>
        public static void SuppressUntil(float unscaledTime)
        {
            _suppressUntil = Mathf.Max(_suppressUntil, unscaledTime);
        }

        /// <summary>지금부터 seconds초 동안 잔상 억제.</summary>
        public static void SuppressFor(float seconds) => SuppressUntil(Time.unscaledTime + seconds);

        private static bool Suppressed => Time.unscaledTime < _suppressUntil;

        private static Shader DissolveShader
        {
            get
            {
                if (!_shaderLoaded)
                {
                    _shaderLoaded = true;
                    _shader = Resources.Load<Shader>("Fx/SpriteDissolve");
                    if (_shader == null)
                    {
                        Debug.LogError("[DissolveGhost] Resources/Fx/SpriteDissolve 셰이더를 불러오지 못했다 — 디졸브 잔상 없이 바로 사라진다.");
                    }
                }

                return _shader;
            }
        }

        private static int CurrentFloor => FloorProgression.Instance != null ? FloorProgression.Instance.Floor : 0;

        // 층·세션 상태 때문에 잔상을 만들면 안 되는지(생성 때).
        private static bool SessionBlocked =>
            _quitting || !Application.isPlaying || !NetworkClient.active
            || NetworkClient.isLoadingScene || NetworkServer.isLoadingScene || Suppressed;

        /// <summary>
        /// source 그림(스프라이트·월드 위치·회전·lossyScale·뒤집기·color·정렬)을 복사한 잔상을 만들어 디졸브를 재생한다.
        /// source의 소유 오브젝트는 곧 지워져도 된다(잔상은 씬 루트의 독립 오브젝트). 스킵 조건이면 아무것도 안 한다.
        /// </summary>
        public static void Spawn(SpriteRenderer source, Color edgeColor)
        {
            if (!VisualFx.Enabled || SessionBlocked || source == null || source.sprite == null || !source.enabled)
            {
                return;
            }

            // forceRenderingOff: 피격 흔들림(HitWhiteFlash) 중이면 흔들림 복사본 대신 원래 위치의 몸 그림을 쓴다. 대체 그림이 없으면 건너뛴다.
            if (source.forceRenderingOff && source.GetComponent<HitWhiteFlash>() == null)
            {
                return;
            }

            if (!source.gameObject.scene.isLoaded || DissolveShader == null || !OnScreen(source.bounds))
            {
                return;
            }

            VisualFxConfig fx = VisualFx.Settings;
            var go = new GameObject("DissolveGhost");
            Transform from = source.transform;
            go.transform.SetPositionAndRotation(from.position, from.rotation);
            go.transform.localScale = from.lossyScale;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.flipX = source.flipX;
            renderer.flipY = source.flipY;
            renderer.color = source.color; // 시체 어둡게·정예 틴트가 그대로 이어진다
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            renderer.drawMode = source.drawMode;
            if (source.drawMode != SpriteDrawMode.Simple)
            {
                renderer.size = source.size;
            }

            var material = new Material(DissolveShader) { name = "SpriteDissolve_Ghost" };
            material.SetFloat("_Progress", 0f);
            material.SetFloat("_EdgeWidth", fx.dissolveEdgeWidth);
            material.SetColor("_EdgeColor", edgeColor);
            material.SetFloat("_EdgeIntensity", fx.dissolveEdgeGlow);
            material.SetFloat("_PixelsPerUnit", source.sprite.pixelsPerUnit);
            material.SetFloat("_Seed", Random.Range(0f, 1000f));
            renderer.sharedMaterial = material;

            var ghost = go.AddComponent<DissolveGhost>();
            ghost._renderer = renderer;
            ghost._material = material;
            ghost._duration = Mathf.Max(0.05f, fx.dissolveDuration);
            ghost._floorAtSpawn = CurrentFloor;
        }

        /// <summary>
        /// 잔상 가장자리 색: 대상에 보이는 상태이상이 있으면 그 속성 색, 없으면 이 층 속성 색, 둘 다 없으면 dissolveDefaultEdgeColor(흰빛).
        /// 몬스터 데이터·판정은 읽기만 한다.
        /// </summary>
        public static Color EdgeColorFor(GameObject target)
        {
            ElementType element = ElementType.None;
            var status = target != null ? target.GetComponent<StatusEffectReceiver>() : null;
            if (status != null)
            {
                element = status.VisibleStatus;
            }

            if (element == ElementType.None && FloorProgression.Instance != null)
            {
                element = FloorProgression.Instance.CurrentFloorElement;
            }

            if (element == ElementType.None)
            {
                return VisualFx.Settings.dissolveDefaultEdgeColor;
            }

            Color color = element.StatusTintColor();
            color.a = 1f;
            return color;
        }

        // 몸 그림 범위가 메인 카메라 화면에 조금이라도 걸리는지. 카메라가 없으면 보이지 않는 것으로 친다.
        private static bool OnScreen(Bounds bounds)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            return GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds);
        }

        private void LateUpdate()
        {
            // 첫 프레임: 같은 프레임에 층 억제창이 켜졌으면 그려지기 전에 지운다. 층 번호가 바뀌면(원격은 일괄 파괴와 같은 프레임) 언제든 지운다
            // — 옛 층 자리에 잔상이 남지 않게. 세션이 끝나면(클라이언트 정지·씬 로딩·종료) 바로 지운다.
            bool sessionOver = _quitting || !NetworkClient.active || NetworkClient.isLoadingScene || NetworkServer.isLoadingScene;
            if (sessionOver || CurrentFloor != _floorAtSpawn || (!_checked && Suppressed))
            {
                Destroy(gameObject);
                return;
            }

            _checked = true;
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            _material.SetFloat("_Progress", t);
            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
