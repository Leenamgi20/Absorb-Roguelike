using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Monster;
using AbsorbRoguelike.Player;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 보스 방의 다음 층 출구(TODO 2단계 "다음 층 이동"). 보스가 쓰러지면 나타나고, 보스 시체를 흡수해 속성
    /// 선택까지 마친 플레이어에게만 열린다(보라색, 아니면 회색). 마지막 층에서는 나타나지 않는다(보스 선택 후 승리 화면).
    /// RoomPostProcessing이 보스 방에 붙인다(호스트·참가자 양쪽 로컬 레벨).
    /// **파티 전원 대기(2026-09-30 2인 검증)**: 살아 있는 플레이어 전원이 준비를 마치고 출구 위에 서야 다음 층으로 넘어간다 —
    /// 한 명이 먼저 타면 흡수 안 한 팀원까지 끌려가던 문제. 출구 위에 "대기 N/M"을 띄운다.
    /// 판정(준비 여부·이동)은 서버. 참가자 쪽 로컬 레벨의 보스 자리는 지워지므로, 보스는 네트워크로 스폰된 BossStub을 찾아 쓴다.
    /// **포탈 그림·마지막 층(2026-10-07)**: 흰 네모 대신 돌 고리 + 소용돌이 포탈(`Resources/Fx/portal_*`, 48×64). 일반 층 = 보라 소용돌이,
    /// 마지막 층 = 금빛 소용돌이. 마지막 층 보스는 흡수하지 않으므로(BossStub.TryBeginAbsorb) 보스가 쓰러지면 모두에게 바로 열리고,
    /// 살아 있는 전원이 올라서면 다음 층 대신 승리(FloorProgression.ServerRunVictory). 잠김이면 소용돌이를 어둡게 멈춘다.
    /// **쓰러짐(2026-10-07)**: 쓰러진 동료(PlayerHealth.IsDowned)가 있으면 층 이동·승리 모두 막고 "쓰러진 동료를 먼저 부활시키세요"를 띄운다.
    /// **포탈 빛(2026-10-15, 비주얼 2단계)**: 포탈 자리에 점광원(VisualFx.AttachPointLight). 일반 층 보라·마지막 층 금빛, 잠김이면 세기×portalLockedIntensityMul,
    /// 포탈이 안 보이면 끈다. 각 화면 로컬.
    /// **층 이동 잔상 가드(2026-10-15, 비주얼 3단계 V9)**: 전원이 출구 위에 서면 각 화면이 DissolveGhost.SuppressFor로 디졸브 잔상을 잠깐 억제한다.
    /// </summary>
    public class FloorExit : MonoBehaviour
    {
        private static readonly Color LockedColor = new Color(0.35f, 0.35f, 0.4f, 1f); // 소용돌이 어둡게
        private const float VortexFrameTime = 0.12f;
        private static Sprite _ring;
        private static Sprite[] _vortexNormal, _vortexFinal;
        private const float StandRadius = 1.5f; // 출구 중심에서 이 거리 안이면 '출구 위에 섰다' — 포탈 그림 전체를 덮는 정도(2026-10-12, 0.9 → 1.5 사용자 요청)
        private const float Pixel = 1f / 16f;

        private BossStub _boss;
        private SpriteRenderer _renderer;   // 소용돌이
        private SpriteRenderer _ringRenderer; // 돌 고리(소용돌이 위)
        private TextMesh _label;
        private UnityEngine.Rendering.Universal.Light2D _light; // 2단계 포탈 빛
        private float _nextBossSearch;
        private bool _shownOpen;

        public void Initialize(BossStub boss)
        {
            _boss = boss;

            LoadSprites();
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = _vortexNormal[0] != null ? _vortexNormal[0] : Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            _renderer.sortingOrder = 6;
            _renderer.enabled = false;
            VisualFx.ApplyGlow(_renderer, GlowKind.PortalVortex); // 소용돌이 발광(블룸). 돌 고리는 그대로
            var ringGo = new GameObject("PortalRing");
            ringGo.transform.SetParent(transform, false);
            _ringRenderer = ringGo.AddComponent<SpriteRenderer>();
            _ringRenderer.sprite = _ring;
            _ringRenderer.sortingOrder = 7;
            _ringRenderer.enabled = false;

            _label = DamagePopup.CreateText("ExitWaitLabel", transform, 32, 20);
            _label.transform.localPosition = new Vector3(0f, 1.15f, 0f); // 포탈(2칸 높이) 위
            _label.gameObject.SetActive(false);

            VisualFxConfig fx = VisualFx.Settings;
            _light = VisualFx.AttachPointLight(transform, fx.portalLightColor, fx.portalLightRadius, fx.portalLightIntensity);
            VisualFx.SetLight(_light, false);
        }

        private void Update()
        {
            ResolveBoss();
            bool visible = _boss != null && _boss.IsDefeated && FloorProgression.Instance != null;
            bool final = visible && FloorProgression.Instance.IsLastFloor;
            _renderer.enabled = visible;
            _ringRenderer.enabled = visible && _ring != null;
            VisualFx.SetLight(_light, visible);
            if (!visible)
            {
                _label.gameObject.SetActive(false);
                return;
            }

            if (NetworkServer.active)
            {
                ServerUpdateReadyAndAdvance();
            }

            var localAbsorption = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<AbsorptionController>() : null;
            bool open = localAbsorption != null && localAbsorption.FloorExitReady;
            if (open && !_shownOpen)
            {
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.PortalOpen); // 내 화면에서 포탈이 열린 순간(소용돌이가 돌기 시작, 2026-10-14)
            }

            _shownOpen = open;
            Sprite[] frames = final ? _vortexFinal : _vortexNormal;
            if (frames[0] != null)
            {
                // 열렸을 때만 소용돌이가 돈다
                _renderer.sprite = frames[open ? (int)(Time.time / VortexFrameTime) % frames.Length : 0];
            }

            float pulse = 1f;
            _renderer.color = open ? Color.white : LockedColor;
            if (_light != null)
            {
                VisualFxConfig fx = VisualFx.Settings;
                _light.color = final ? fx.portalFinalLightColor : fx.portalLightColor;
                _light.pointLightOuterRadius = fx.portalLightRadius;
                _light.intensity = fx.portalLightIntensity * (open ? 1f : fx.portalLockedIntensityMul);
            }

            CountParty(out int alive, out int standing, out int downed);
            // 비주얼 3단계 V9(2026-10-15): 살아 있는 전원이 준비를 마치고 출구 위에 섰으면(서버가 곧 AdvanceFloor로 남은 몬스터를 일괄 파괴)
            // 이 화면의 디졸브 잔상을 잠깐 억제한다. 동기화 값으로 각 화면이 계산하는 기존 조건 그대로 — 새 네트워크 없음.
            // 원격 화면에서는 파괴 메시지보다 먼저 켜진다(내가 마지막으로 올라선 경우는 서버보다 내 화면이 먼저 안다). 호스트는 AdvanceFloor가 직접 켠다.
            if (!final && downed == 0 && alive > 0 && standing == alive)
            {
                DissolveGhost.SuppressFor(0.5f);
            }

            // 쓰러진 동료가 있으면 층 이동·승리가 막힌다 — 대기 숫자 대신 "먼저 부활" 안내(2026-10-07).
            bool showLabel = downed > 0 || (alive > 1 && standing > 0);
            _label.gameObject.SetActive(showLabel);
            if (showLabel)
            {
                _label.text = downed > 0 ? Loc.Get("exit.revive_first") : Loc.Get("floor.exit_waiting", standing, alive);
                // 펄스 스케일을 상쇄해 글자 크기는 일정하게
                _label.transform.localScale = Vector3.one / Mathf.Max(0.01f, pulse);
            }
        }

        private static void LoadSprites()
        {
            if (_vortexNormal != null)
            {
                return;
            }

            _ring = Resources.Load<Sprite>("Fx/portal_ring");
            _vortexNormal = new Sprite[4];
            _vortexFinal = new Sprite[4];
            for (int i = 0; i < 4; i++)
            {
                _vortexNormal[i] = Resources.Load<Sprite>("Fx/portal_vortex_normal_" + i);
                _vortexFinal[i] = Resources.Load<Sprite>("Fx/portal_vortex_final_" + i);
            }
        }

        // 참가자 쪽은 템플릿 보스가 지워져 Initialize 때 받은 참조가 없다 — 네트워크로 스폰된 보스를 찾는다(층마다 보스 1기).
        private void ResolveBoss()
        {
            if (_boss != null || Time.time < _nextBossSearch)
            {
                return;
            }

            _nextBossSearch = Time.time + 0.5f;
            _boss = FindFirstObjectByType<BossStub>();
        }

        private bool IsStanding(PlayerController player)
        {
            return Vector2.Distance(player.transform.position, transform.position) <= StandRadius;
        }

        // 살아 있는 플레이어 수와, 그중 준비를 마치고 출구 위에 선 수, 쓰러진(부활 대기) 수(모든 클라이언트가 동기화 값으로 같은 결과를 계산).
        private void CountParty(out int alive, out int standing, out int downed)
        {
            alive = 0;
            standing = 0;
            downed = 0;
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                var health = player.GetComponent<PlayerHealth>();
                if (health != null && health.IsDead)
                {
                    if (health.IsDowned)
                    {
                        downed++;
                    }

                    continue;
                }

                alive++;
                var absorption = player.GetComponent<AbsorptionController>();
                if (absorption != null && absorption.FloorExitReady && IsStanding(player))
                {
                    standing++;
                }
            }
        }

        [Server]
        private void ServerUpdateReadyAndAdvance()
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                var absorption = player.GetComponent<AbsorptionController>();
                if (absorption != null)
                {
                    // 마지막 층은 흡수 없이 보스만 쓰러지면 준비 완료(2026-10-07).
                    bool ready = FloorProgression.Instance.IsLastFloor
                        || (_boss.HasAbsorbed(player.netIdentity) && absorption.PendingBossChoice == null);
                    absorption.SetFloorExitReady(ready);
                }
            }

            CountParty(out int alive, out int standing, out int downed);
            // 쓰러진 동료가 한 명이라도 있으면 다음 층·승리로 넘어가지 않는다 — 쓰러진 채로 다른 층에 끌려가지 않게, 먼저 부활(2026-10-07).
            if (downed == 0 && alive > 0 && standing == alive)
            {
                if (FloorProgression.Instance.IsLastFloor)
                {
                    FloorProgression.Instance.ServerRunVictory(); // 봉인 밖으로 — 승리(한 번만)
                }
                else
                {
                    FloorProgression.Instance.AdvanceFloor(); // 중복 호출은 AdvanceFloor가 막는다
                }
            }
        }
    }
}
