using System.Collections.Generic;
using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Monster
{
    /// <summary>
    /// 보스 겉모습 무작위(2026-10-07, 사용자 결정): 층마다 12지신 정령 보스 그림 중 하나를 고른다. 그림만 바뀌고 패턴·판정·수치는
    /// 그대로(거대 소 패턴 공용). 서버가 스폰 때 고른 번호를 SyncVar로 보내고 각 화면이 `Resources/Bosses/boss_&lt;동물&gt;`
    /// 스프라이트로 바꾼다. 한 판 안에서는 이미 나온 그림을 피한다(12종 &gt; 5층). 그림은 96×96(PPU 32 = 3칸, 몸은 약 2칸 —
    /// 남는 자리는 정령 불꽃·룬 돌), 판정 콜라이더는 그대로.
    /// **보스 빛(2026-10-15, 비주얼 2단계)**: Apply 뒤 보스에 점광원(VisualFx.AttachPointLight, 각 화면 로컬). 색 = 층 속성색(정령별 불꽃 색 데이터가
    /// 아직 없어 층 속성 기준, None이면 bossLightDefaultColor). BossBrain.bossPhase(SyncVar)를 읽기만 해서 폭주(4)면 붉은 색으로 맥박. 쓰러지면 끔.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class BossAppearance : NetworkBehaviour
    {
        public static readonly string[] Animals =
            { "rat", "ox", "tiger", "rabbit", "dragon", "snake", "horse", "sheep", "monkey", "rooster", "dog", "pig" };

        // 서버 전용: 이번 판에 이미 나온 그림. 1층 보스가 나올 때 비운다.
        private static readonly List<int> UsedThisRun = new List<int>();

        [SyncVar(hook = nameof(OnAppearanceChanged))] private int appearance = -1;

        private UnityEngine.Rendering.Universal.Light2D _light; // 2단계 보스 빛
        private BossBrain _brain;
        private BossStub _stub;

        public override void OnStartServer()
        {
            var dungeon = FloorProgression.Instance;
            if (dungeon == null || dungeon.Floor <= 1)
            {
                UsedThisRun.Clear();
            }

            var candidates = new List<int>();
            for (int i = 0; i < Animals.Length; i++)
            {
                if (!UsedThisRun.Contains(i))
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                UsedThisRun.Clear();
                for (int i = 0; i < Animals.Length; i++)
                {
                    candidates.Add(i);
                }
            }

            appearance = candidates[Random.Range(0, candidates.Count)];
            UsedThisRun.Add(appearance);
            Apply(appearance);
        }

        public override void OnStartClient()
        {
            Apply(appearance);
        }

        private void OnAppearanceChanged(int _, int value)
        {
            Apply(value);
        }

        private void Apply(int index)
        {
            if (index < 0 || index >= Animals.Length)
            {
                return;
            }

            Sprite sprite = Resources.Load<Sprite>("Bosses/boss_" + Animals[index]);
            if (sprite != null)
            {
                GetComponent<SpriteRenderer>().sprite = sprite;
            }

            if (_light == null)
            {
                VisualFxConfig fx = VisualFx.Settings;
                _light = VisualFx.AttachPointLight(transform, BaseLightColor(fx), fx.bossLightRadius, fx.bossLightIntensity);
                _brain = GetComponent<BossBrain>();
                _stub = GetComponent<BossStub>();
            }
        }

        private static Color BaseLightColor(VisualFxConfig fx)
        {
            ElementType element = FloorProgression.Instance != null ? FloorProgression.Instance.CurrentFloorElement : ElementType.None;
            return element == ElementType.None ? fx.bossLightDefaultColor : element.StatusTintColor();
        }

        // 페이즈는 서버 SyncVar를 읽기만(새 네트워크 코드 없음). 쓰러지면(시체) 빛을 끈다.
        private void Update()
        {
            if (_light == null)
            {
                return;
            }

            VisualFxConfig fx = VisualFx.Settings;
            bool alive = _stub == null || !_stub.IsDefeated;
            VisualFx.SetLight(_light, alive);
            if (!alive)
            {
                return;
            }

            _light.pointLightOuterRadius = fx.bossLightRadius;
            if (_brain != null && _brain.BossPhaseValue >= 4)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * fx.bossEnragePulseSpeed);
                _light.color = fx.bossEnrageColor;
                _light.intensity = fx.bossLightIntensity * Mathf.Lerp(fx.bossEnragePulseMin, fx.bossEnragePulseMax, pulse);
            }
            else
            {
                _light.color = BaseLightColor(fx);
                _light.intensity = fx.bossLightIntensity;
            }
        }
    }
}
