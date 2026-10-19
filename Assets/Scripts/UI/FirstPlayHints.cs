using System;
using System.Collections.Generic;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Localization;
using AbsorbRoguelike.Monster;
using AbsorbRoguelike.Network;
using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 첫 플레이 힌트(2026-10-16): 로컬 플레이어 화면 아래 가운데에 한 줄 안내를 띄운다. 이 화면에만 보이고 게임을 멈추지 않는다
    /// (네트워크 코드 없음 — 클라이언트에 이미 동기화된 상태만 읽는다).
    ///   순서 힌트: 이동 → 공격 → 가드 → 대시. 해당 행동을 하거나 12초(Time.deltaTime 누적 — 1인 일시정지 중엔 멈춤)가 지나면 다음.
    ///   상황 힌트(순서 힌트보다 우선): 흡수 — 흡수 범위 안에 흡수할 수 있는 대상이 처음 생겼을 때 / 부활 — 다른 플레이어가 처음 쓰러졌을 때.
    /// 한 번 끝난 힌트는 PlayerPrefs 'Absorb.Hint.&lt;id&gt;' = 1로 다시 안 나온다(옵션 '힌트 다시 보기' = <see cref="ResetAll"/>).
    /// 입력은 따로 만든 PlayerControls 인스턴스에서 읽기만 한다(소비·차단 없음). 생성: PlayerController.OnStartLocalPlayer → <see cref="EnsureFor"/>.
    /// 문서: docs/04_구현위키/첫_플레이_안내.md.
    /// </summary>
    public class FirstPlayHints : MonoBehaviour
    {
        public const string PrefsPrefix = "Absorb.Hint.";
        private const int CanvasSortOrder = 35; // HUD(0)·지도(15) 위, 시작 메뉴(40)·일시정지(45)·옵션(50) 아래
        private const float HintDuration = 12f;
        private const float ScanInterval = 0.25f;

        private const string Move = "move";
        private const string Attack = "attack";
        private const string Guard = "guard";
        private const string Dash = "dash";
        private const string Absorb = "absorb";
        private const string Revive = "revive";
        private const string Debris = "debris";          // 보스 파편이 가까이 생김 → 눌러서 머금기(한 번)
        private const string Spit = "spit";              // 파편을 머금음 → 공격 키로 뱉기(한 번, 뱉을 때까지 유지)
        private const string BossAbsorb = "boss_absorb"; // 보스를 쓰러뜨림 → 시체 흡수(매 층, 흡수할 때까지 유지, 저장 안 함)

        private static readonly string[] SequenceIds = { Move, Attack, Guard, Dash };
        private static readonly string[] AllIds = { Move, Attack, Guard, Dash, Absorb, Revive, Debris, Spit };

        private static FirstPlayHints _instance;

        private readonly HashSet<string> _done = new HashSet<string>();
        private GameObject _player;
        private AbsorptionController _absorption;
        private PlayerHealth _health;
        private PlayerControls _controls;
        private GameObject _panel;
        private Text _text;
        private string _current;
        private float _elapsed;
        private float _scanTimer;
        private bool _textDirty;

        /// <summary>로컬 플레이어가 생길 때 한 번 부른다. 이미 같은 플레이어에 붙어 있으면 그대로, 다른 플레이어면 바꿔 단다.</summary>
        public static void EnsureFor(GameObject localPlayer)
        {
            if (localPlayer == null)
            {
                return;
            }

            if (_instance != null)
            {
                if (_instance._player == localPlayer)
                {
                    return;
                }

                Destroy(_instance.gameObject);
                _instance = null;
            }

            var go = new GameObject("FirstPlayHints");
            _instance = go.AddComponent<FirstPlayHints>();
            _instance.Init(localPlayer);
        }

        /// <summary>모든 힌트 기록을 지운다(옵션 '힌트 다시 보기'). 지금 떠 있는 힌트 화면도 처음부터 다시.</summary>
        public static void ResetAll()
        {
            try
            {
                foreach (string id in AllIds)
                {
                    PlayerPrefs.DeleteKey(PrefsPrefix + id);
                }

                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FirstPlayHints] PlayerPrefs delete failed: {e.Message}");
            }

            if (_instance != null)
            {
                _instance._done.Clear();
                _instance._current = null;
            }
        }

        private void Init(GameObject player)
        {
            _player = player;
            _absorption = player.GetComponent<AbsorptionController>();
            _health = player.GetComponent<PlayerHealth>();
            foreach (string id in AllIds)
            {
                if (ReadDone(id))
                {
                    _done.Add(id);
                }
            }

            _controls = new PlayerControls();
            InputBindingStore.Register(_controls); // 옵션에서 바꾼 키로 읽는다
            _controls.Gameplay.Enable();
            Loc.Changed += MarkTextDirty;
            InputBindingStore.Changed += MarkTextDirty;
            Build();
        }

        private void OnDestroy()
        {
            Loc.Changed -= MarkTextDirty;
            InputBindingStore.Changed -= MarkTextDirty;
            if (_controls != null)
            {
                InputBindingStore.Unregister(_controls);
                _controls.Dispose();
                _controls = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void MarkTextDirty() => _textDirty = true;

        private void Update()
        {
            if (_player == null)
            {
                Destroy(gameObject); // 로컬 플레이어가 사라짐(타이틀로·재시작·연결 끊김)
                return;
            }

            if (ShouldHide())
            {
                SetVisible(false);
                return;
            }

            _scanTimer -= Time.unscaledDeltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                string situational = FindSituationalHint();
                if (situational != null && situational != _current && (!IsSituational(_current) || situational == BossAbsorb))
                {
                    Show(situational); // 순서 힌트를 잠시 밀어낸다(그 힌트는 완료 처리하지 않고 나중에 다시)
                }
            }

            if (_current == null)
            {
                string next = NextSequenceHint();
                if (next == null)
                {
                    SetVisible(false); // 모든 한 번짜리 힌트를 봐도 남아 있는다 — 보스 흡수 안내는 매 층 다시 뜬다
                    return;
                }

                Show(next);
            }

            _elapsed += Time.deltaTime;
            bool timedOut = _current != BossAbsorb && _current != Spit && _elapsed >= HintDuration; // 이 둘은 행동할 때까지 유지
            if (ActionDone(_current) || timedOut)
            {
                Complete(_current);
                SetVisible(false);
                return;
            }

            if (_textDirty)
            {
                RefreshText();
            }

            SetVisible(true);
        }

        private bool ShouldHide()
        {
            if (PauseMenuController.IsOpen || OptionsMenuController.IsOpen || ControlsTableOverlay.IsOpen
                || BossChoiceUIController.IsOpen || RunEndScreenController.IsShown || StoryIntroController.IsPlaying)
            {
                return true;
            }

            StartMenuController startMenu = StartMenuController.Instance;
            if (startMenu != null && startMenu.IsMenuVisible)
            {
                return true;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.isPressed)
            {
                return true; // Tab 지도
            }

            return _health != null && (_health.IsDead || _health.IsDowned);
        }

        private static bool IsSituational(string id) => id == Absorb || id == Revive || id == Debris || id == Spit || id == BossAbsorb;

        private string NextSequenceHint()
        {
            foreach (string id in SequenceIds)
            {
                if (!_done.Contains(id))
                {
                    return id;
                }
            }

            return null;
        }

        private string FindSituationalHint()
        {
            if (BossAwaitingLocalAbsorb())
            {
                return BossAbsorb; // 가장 중요 — 다른 안내를 밀어내고 흡수할 때까지 남는다
            }

            if (!_done.Contains(Revive) && OtherPlayerDowned())
            {
                return Revive;
            }

            if (!_done.Contains(Spit) && _absorption != null && _absorption.HasDebris)
            {
                return Spit;
            }

            if (!_done.Contains(Debris) && DebrisNearby())
            {
                return Debris;
            }

            if (!_done.Contains(Absorb) && AbsorbTargetNearby())
            {
                return Absorb;
            }

            return null;
        }

        // 다른 플레이어 쓰러짐(동기화 값 isDowned).
        private bool OtherPlayerDowned()
        {
            foreach (PlayerHealth other in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                if (other != _health && other.IsDowned)
                {
                    return true;
                }
            }

            return false;
        }

        // 흡수 범위 안 흡수 가능 대상: 살아 있는 일반 잡몹(이 방에서 아직 흡수 안 함) · 쓰러진 정예 시체 · 쓰러진 보스 시체(마지막 층 제외).
        // 서버 판정(AbsorptionController.FindNearest*)과 같은 조건을 클라이언트에 동기화된 값으로만 본다 — 잠금·이미 흡수한 시체 여부는 보지 않는다.
        private bool AbsorbTargetNearby()
        {
            if (_absorption == null)
            {
                return false;
            }

            foreach (Collider2D hit in Physics2D.OverlapCircleAll(_player.transform.position, _absorption.AbsorbRange))
            {
                MonsterStub monster = hit.GetComponent<MonsterStub>();
                if (monster != null)
                {
                    bool absorbable = monster.IsElite ? monster.IsDefeated : !monster.IsDead && !_absorption.HasAbsorbedThisRoom;
                    if (absorbable)
                    {
                        return true;
                    }

                    continue;
                }

                BossStub boss = hit.GetComponent<BossStub>();
                if (boss != null && boss.IsDefeated && !(FloorProgression.Instance != null && FloorProgression.Instance.IsLastFloor))
                {
                    return true;
                }
            }

            return false;
        }

        // 쓰러진 보스 시체 중 이 플레이어가 아직 흡수하지 않은 것(마지막 층 보스는 흡수 불가라 제외). absorbedNetIds는 동기화 목록.
        private bool BossAwaitingLocalAbsorb()
        {
            if (FloorProgression.Instance != null && FloorProgression.Instance.IsLastFloor)
            {
                return false;
            }

            NetworkIdentity identity = _player.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                return false;
            }

            foreach (BossStub boss in FindObjectsByType<BossStub>(FindObjectsSortMode.None))
            {
                if (boss.IsDefeated && !boss.IsAbsorbedBy(identity.netId))
                {
                    return true;
                }
            }

            return false;
        }

        // 보스 파편이 흡수 범위의 3배 안에 있음(다가가서 누르면 머금는다 — AbsorptionController.ServerAbsorbStart).
        private bool DebrisNearby()
        {
            if (_absorption == null || _absorption.HasDebris)
            {
                return false;
            }

            float range = _absorption.AbsorbRange * 3f;
            foreach (BossDebris debris in FindObjectsByType<BossDebris>(FindObjectsSortMode.None))
            {
                if (Vector2.Distance(debris.transform.position, _player.transform.position) <= range)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ActionDone(string id)
        {
            PlayerControls.GameplayActions g = _controls.Gameplay;
            switch (id)
            {
                case Debris: return _absorption != null && _absorption.HasDebris;
                case Spit: return _absorption != null && !_absorption.HasDebris;
                case BossAbsorb: return !BossAwaitingLocalAbsorb();
                case Move: return g.Move.ReadValue<Vector2>().sqrMagnitude > 0.01f;
                case Attack: return g.Attack.triggered;
                case Guard: return g.Guard.IsPressed();
                case Dash: return g.Dash.triggered;
                case Absorb: return g.Absorb.IsPressed(); // 홀드 시작 순간
                case Revive: return g.Revive.IsPressed();
                default: return true;
            }
        }

        private void Show(string id)
        {
            _current = id;
            _elapsed = 0f;
            RefreshText();
        }

        private void Complete(string id)
        {
            _current = null;
            if (id == BossAbsorb)
            {
                return; // 매 층 다시 나와야 하므로 저장하지 않는다
            }

            _done.Add(id);
            try
            {
                PlayerPrefs.SetInt(PrefsPrefix + id, 1);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FirstPlayHints] PlayerPrefs save failed: {e.Message}");
            }
        }

        private static bool ReadDone(string id)
        {
            try
            {
                return PlayerPrefs.GetInt(PrefsPrefix + id, 0) == 1;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FirstPlayHints] PlayerPrefs read failed: {e.Message}");
                return true; // 못 읽으면 띄우지 않는다
            }
        }

        private void RefreshText()
        {
            _textDirty = false;
            if (_current == null || _text == null)
            {
                return;
            }

            _text.text = Loc.Get("hint." + _current, KeyFor(_current));
        }

        // 키 글자는 옵션 리바인딩을 반영한 공유 인스턴스에서 읽는다(조작법 표와 같은 함수).
        private static string KeyFor(string id)
        {
            PlayerControls.GameplayActions g = InputBindingStore.Shared.Gameplay;
            switch (id)
            {
                case Move: return ControlsTableOverlay.MoveKeys();
                case Attack: return ControlsTableOverlay.KeyDisplay(g.Attack);
                case Guard: return ControlsTableOverlay.KeyDisplay(g.Guard);
                case Dash: return ControlsTableOverlay.KeyDisplay(g.Dash);
                case Absorb: return ControlsTableOverlay.KeyDisplay(g.Absorb);
                case Revive: return ControlsTableOverlay.KeyDisplay(g.Revive);
                case Debris: return ControlsTableOverlay.KeyDisplay(g.Absorb);
                case Spit: return ControlsTableOverlay.KeyDisplay(g.Attack);
                case BossAbsorb: return ControlsTableOverlay.KeyDisplay(g.Absorb);
                default: return "";
            }
        }

        private void SetVisible(bool visible)
        {
            if (_panel != null && _panel.activeSelf != visible)
            {
                _panel.SetActive(visible);
            }
        }

        // ── UI 만들기 ─────────────────────────────────────────────

        private void Build()
        {
            Canvas canvas = UIBuilder.CreateCanvas(transform, "FirstPlayHintsCanvas", CanvasSortOrder);
            Destroy(canvas.GetComponent<GraphicRaycaster>()); // 클릭을 받지 않는다(게임 조작 방해 없음)

            _panel = new GameObject("HintPanel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)_panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 110f);
            rect.sizeDelta = new Vector2(780f, 48f); // 2026-10-16 보스 흡수·파편 안내가 길어 넓힘(620 → 780)
            var image = _panel.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f);
            image.raycastTarget = false;

            _text = UIBuilder.AddText(_panel.transform, "", 18, Color.white, 46f);
            UIBuilder.Stretch(_text.rectTransform);
            _text.rectTransform.offsetMin = new Vector2(16f, 0f);
            _text.rectTransform.offsetMax = new Vector2(-16f, 0f);
            UIBuilder.FitButtonLabel(_text); // 영어가 길면 한 줄 안에서 축소
            _panel.SetActive(false);
        }
    }
}
