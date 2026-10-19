using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 쓰러진 플레이어 표시(2026-10-07 쓰러짐·부활): 몸(BodyVisual — SlimeBodyAnimator가 루트 대신 그리는 그림, 없으면 루트)과 무기 그림 위에
    /// 반투명 검정 오버레이를 덮는다. HitWhiteFlash와 같은 방식(`Resources/Fx/SpriteSolidColor` 단색 셰이더, 몸 color는 건드리지 않음).
    /// 순수 표시 전용 — PlayerHealth의 쓰러짐 훅(모든 화면)이 <see cref="Set"/>을 부른다. 부활하면 오버레이를 끈다.
    /// </summary>
    public class PlayerDownedVisual : MonoBehaviour
    {
        private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.6f); // [PLAYTEST]
        private const int OrderAboveBody = 4; // 피격 번쩍임(+3) 위

        private static Material _material;
        private static bool _materialLoaded;

        private readonly List<SpriteRenderer> _targets = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _overlays = new List<SpriteRenderer>();
        private bool _downed;

        public static void Set(GameObject player, bool downed)
        {
            var visual = player.GetComponent<PlayerDownedVisual>();
            if (visual == null)
            {
                if (!downed)
                {
                    return;
                }

                visual = player.AddComponent<PlayerDownedVisual>();
            }

            visual._downed = downed;
            visual.Sync();
        }

        private static Material Material
        {
            get
            {
                if (!_materialLoaded)
                {
                    _materialLoaded = true;
                    var shader = Resources.Load<Shader>("Fx/SpriteSolidColor");
                    if (shader != null)
                    {
                        _material = new Material(shader) { color = Color.white }; // 색은 오버레이 렌더러 color(검정·반투명)로
                    }
                }

                return _material;
            }
        }

        private void CollectTargets()
        {
            if (_targets.Count > 0 || Material == null)
            {
                return;
            }

            Transform body = transform.Find("BodyVisual");
            SpriteRenderer bodyRenderer = body != null ? body.GetComponent<SpriteRenderer>() : GetComponent<SpriteRenderer>();
            AddTarget(bodyRenderer);
            Transform weapon = transform.Find("Weapon");
            AddTarget(weapon != null ? weapon.GetComponent<SpriteRenderer>() : null);
        }

        private void AddTarget(SpriteRenderer target)
        {
            if (target == null)
            {
                return;
            }

            var go = new GameObject("DownedOverlay");
            go.transform.SetParent(target.transform, false);
            var overlay = go.AddComponent<SpriteRenderer>();
            overlay.sharedMaterial = Material;
            overlay.color = OverlayColor;
            overlay.enabled = false;
            _targets.Add(target);
            _overlays.Add(overlay);
        }

        private void LateUpdate()
        {
            if (_downed)
            {
                Sync();
            }
        }

        private void Sync()
        {
            CollectTargets();
            for (int i = 0; i < _targets.Count; i++)
            {
                SpriteRenderer target = _targets[i];
                SpriteRenderer overlay = _overlays[i];
                if (overlay == null)
                {
                    continue;
                }

                bool show = _downed && target != null && target.enabled && !target.forceRenderingOff && target.sprite != null;
                overlay.enabled = show;
                if (!show)
                {
                    continue;
                }

                overlay.sprite = target.sprite;
                overlay.flipX = target.flipX;
                overlay.flipY = target.flipY;
                overlay.sortingLayerID = target.sortingLayerID;
                overlay.sortingOrder = target.sortingOrder + OrderAboveBody;
            }
        }
    }
}
