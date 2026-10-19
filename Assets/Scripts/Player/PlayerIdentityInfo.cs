using System.Collections.Generic;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 플레이어 번호·표시 이름·표시 색(Tab 지도 2차 점 색·라벨, 왼쪽 위 파티 UI 공용). 번호는 서버가 OnStartServer에서
    /// 지금 접속 중인 플레이어 번호 중 비어 있는 가장 작은 수(1부터)로 준다 — 2P가 나갔다 들어오면 다시 2P.
    /// 이름은 아직 '플레이어 N' 고정(플랫폼 닉네임 연동·이름 입력은 범위 밖).
    /// 머리 위 번호표(2026-10-07): 2명 이상일 때 캐릭터 머리 위에 '1P'·'2P'를 번호 색으로 띄운다(검은 그림자 1px). 혼자면 숨김.
    /// 플레이어 빛(2026-10-15, 비주얼 2단계): 몸에 은은한 점광원(VisualFx.AttachPointLight). 혼자면 따뜻한 흰빛, 2명 이상이면 번호 색.
    /// 각 화면이 로컬로만 그리는 표시 전용.
    /// </summary>
    public class PlayerIdentityInfo : NetworkBehaviour
    {
        // 번호별 표시 색(1P 파랑, 2P 주황, 3P 초록, 4P 분홍). 0(아직 미부여)·범위 밖은 1P 파랑.
        private static readonly Color[] NumberColors =
        {
            new Color(0.35f, 0.6f, 1f, 1f),
            new Color(1f, 0.6f, 0.25f, 1f),
            new Color(0.45f, 0.9f, 0.45f, 1f),
            new Color(0.95f, 0.45f, 0.85f, 1f),
        };

        private static readonly List<PlayerIdentityInfo> _all = new List<PlayerIdentityInfo>();

        private const int TagFontSize = 36;       // 36 × 0.1 × 0.1 ≈ 0.36유닛 ≈ 11px
        private const int TagSortingOrder = 24;   // 캐릭터·체력바·스택 표시 위, 대미지 숫자(25) 아래
        private const float TagGap = 2f / 32f;    // 몸 그림 위 2px

        [SyncVar] private byte playerNumber;

        private TextMesh _tag;
        private TextMesh _tagShadow;
        private SpriteRenderer _body;
        private UnityEngine.Rendering.Universal.Light2D _light; // 2단계 플레이어 빛(각 화면 로컬)

        /// <summary>이 클라이언트에 있는 모든 플레이어(호스트 포함, OnStartClient~OnStopClient 동안).</summary>
        public static IReadOnlyList<PlayerIdentityInfo> All => _all;

        /// <summary>1부터. 0이면 아직 서버 값이 도착하지 않음.</summary>
        public byte PlayerNumber => playerNumber;
        public string DisplayName => playerNumber > 0 ? Loc.Get("party.player_n", playerNumber) : Loc.Get("party.player");
        /// <summary>지도 점 옆 짧은 라벨('1P').</summary>
        public string ShortLabel => playerNumber > 0 ? $"{playerNumber}P" : "?P";
        public Color DisplayColor => ColorForNumber(playerNumber);

        public static Color ColorForNumber(int number)
        {
            return number >= 1 && number <= NumberColors.Length ? NumberColors[number - 1] : NumberColors[0];
        }

        public override void OnStartServer()
        {
            var used = new HashSet<int>();
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                var other = identity != null ? identity.GetComponent<PlayerIdentityInfo>() : null;
                if (other != null && other != this && other.playerNumber > 0)
                {
                    used.Add(other.playerNumber);
                }
            }

            byte number = 1;
            while (used.Contains(number))
            {
                number++;
            }

            playerNumber = number;
            Debug.Log($"[PlayerIdentity] {name} assigned player number {number}");
        }

        public override void OnStartClient()
        {
            if (!_all.Contains(this))
            {
                _all.Add(this);
            }

            _body = GetComponent<SpriteRenderer>();
            var root = new GameObject("PlayerNumberTag").transform;
            root.SetParent(transform, false);
            _tagShadow = DamagePopup.CreateText("Shadow", root, TagFontSize, TagSortingOrder);
            _tagShadow.color = Color.black;
            _tagShadow.transform.localPosition = new Vector3(1f / 32f, -1f / 32f, 0f);
            _tag = DamagePopup.CreateText("Label", root, TagFontSize, TagSortingOrder);
            _tag.GetComponent<MeshRenderer>().sortingOrder = TagSortingOrder + 1;

            // 2단계 플레이어 빛(2026-10-15): 은은한 점광원. 색은 LateUpdate에서 인원에 따라(혼자 = 따뜻한 흰빛, 2명 이상 = 번호 색).
            VisualFxConfig fx = VisualFx.Settings;
            _light = VisualFx.AttachPointLight(transform, fx.playerLightSoloColor, fx.playerLightOuterRadius, fx.playerLightIntensity);
        }

        private void LateUpdate()
        {
            UpdateLight();
            if (_tag == null)
            {
                return;
            }

            bool show = _all.Count >= 2 && (_body == null || _body.enabled);
            Transform root = _tag.transform.parent;
            if (root.gameObject.activeSelf != show)
            {
                root.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            string label = ShortLabel;
            if (_tag.text != label)
            {
                _tag.text = label;
                _tagShadow.text = label;
            }

            _tag.color = DisplayColor;
            float top = _body != null && _body.sprite != null ? _body.bounds.max.y - transform.position.y : 0.5f;
            root.localPosition = new Vector3(0f, top + TagGap, 0f);
        }

        // 인원이 바뀌면(입장·퇴장) 색이 바로 바뀌게 매 프레임 확인. 쓰러짐 중에도 유지(몸 그림이 꺼질 때만 끔).
        private void UpdateLight()
        {
            if (_light == null)
            {
                return;
            }

            VisualFxConfig fx = VisualFx.Settings;
            _light.color = _all.Count >= 2 ? DisplayColor : fx.playerLightSoloColor;
            _light.intensity = fx.playerLightIntensity;
            _light.pointLightOuterRadius = fx.playerLightOuterRadius;
            _light.pointLightInnerRadius = fx.playerLightInnerRadius;
            VisualFx.SetLight(_light, _body == null || _body.enabled);
        }

        public override void OnStopClient()
        {
            _all.Remove(this);
        }

        private void OnDestroy()
        {
            _all.Remove(this);
        }
    }
}
