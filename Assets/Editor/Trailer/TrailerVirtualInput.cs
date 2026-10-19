using System;
using System.Collections.Generic;
using AbsorbRoguelike.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace AbsorbRoguelike.EditorTools.Trailer
{
    /// <summary>
    /// 트레일러 몽타주 자동 조작(2026-10-19) — 가상 키보드·마우스. InputSystem.AddDevice로 가상 장치를 만들고 상태를
    /// InputSystem.QueueStateEvent(KeyboardState/MouseState)로 넣는다. 게임 코드(PlayerControls·PlayerController)는 그대로 —
    /// 실제 키보드·마우스를 누른 것과 같은 경로로 Move(WASD)·Attack(좌클릭)·Guard(우클릭)·Dash(Space)·Absorb(흡수 키, 기본 E)가 들어간다.
    /// - 흡수(2026-10-19 2차 샷): Gameplay.Absorb의 키보드 바인딩(옵션 창 재설정 포함, InputBindingStore.Shared)을 Begin 때 읽어 그 키를
    ///   누른 채 유지한다 — Hold(0.5초) 상호작용의 started → performed(홀드 충족) / 일찍 떼면 canceled 경로가 실제 키와 똑같이 돈다.
    /// - 공격·가드·대시는 Action.IsPressed()로도 읽히므로 누른 상태를 매 에디터 update마다 다시 넣어 유지한다.
    /// - 조준: PlayerController가 Mouse.current.position으로 바라보는 방향을 정하므로, 가상 마우스를 매 update MakeCurrent하고
    ///   실제 마우스는 켜져 있는 동안 잠시 끈다(촬영 중 손이 닿아도 조준·클릭이 섞이지 않게). 조준 화면 좌표가 HUD(UI 레이캐스트
    ///   대상) 위면 PlayerCombat.IsAttackInputBlockedByUI가 공격을 막으므로, 같은 방향에서 UI와 안 겹치는 거리를 고른다.
    /// - 에디터 포커스: InputSettings 에셋이 없는 프로젝트라 메모리 기본값(Game View 포커스를 따름)으로 동작한다. 시작할 때
    ///   editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView, backgroundBehavior = IgnoreFocus로 바꾸고(원래 값 저장)
    ///   Remove 때 되돌린다. 설정이 에셋이면(경로가 있으면) 바꾸지 않고 경고만 한다(영구 변경 금지).
    /// - 정리: Play 종료(ExitingPlayMode)·어셈블리 리로드 직전에 ReleaseAll + Remove(장치 제거·실제 마우스 복구·설정 복구).
    /// 자세한 내용: docs/04_구현위키/트레일러_촬영_도구.md '자동 조작 샷'
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerVirtualInput
    {
        private const string DevicePrefix = "TrailerVirtual";
        private static readonly float[] AimDistances = { 3f, 2.5f, 2f, 1.5f, 1.1f, 4f, 5f };

        private static Keyboard _keyboard;
        private static Mouse _mouse;

        private static Vector2 _move;
        private static bool _attack;
        private static bool _guard;
        private static bool _absorb;
        private static Key _absorbKey = Key.E;
        private static bool _dashHeld;
        private static int _dashReleaseFrame = -1;

        private enum AimMode { None, World, Direction }
        private static AimMode _aimMode;
        private static Vector3 _aimWorld;
        private static GameObject _aimOrigin;
        private static Vector2 _aimDirection;
        private static Vector2 _lastAimScreen;
        private static bool _warnedAimOverUI;

        private static bool _settingsChanged;
        private static InputSettings.EditorInputBehaviorInPlayMode _originalEditorBehavior;
        private static InputSettings.BackgroundBehavior _originalBackgroundBehavior;

        private static readonly List<InputDevice> _disabledRealMice = new List<InputDevice>();
        private static Mouse _previousMouse;
        private static Keyboard _previousKeyboard;

        /// <summary>가상 장치가 붙어 있는지.</summary>
        public static bool IsActive => _keyboard != null && _mouse != null;

        static TrailerVirtualInput()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingPlayMode)
                {
                    Remove();
                }
            };
            AssemblyReloadEvents.beforeAssemblyReload += Remove;
            EditorApplication.delayCall += RemoveLeftoverDevices; // 리로드 전에 못 지운 가상 장치가 남아 있으면 정리(Input System 초기화 뒤)
        }

        // ── 시작·정리 ─────────────────────────────────────────────

        /// <summary>가상 키보드·마우스를 붙이고 에디터 포커스 설정을 바꾼다(이미 붙어 있으면 아무것도 안 함). Play 중에만.</summary>
        public static bool Begin()
        {
            if (IsActive)
            {
                return true;
            }

            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Trailer] Play 중이 아니라 가상 입력을 붙이지 않는다.");
                return false;
            }

            RemoveLeftoverDevices();
            ApplyFocusSettings();

            _previousMouse = Mouse.current;
            _previousKeyboard = Keyboard.current;
            _keyboard = InputSystem.AddDevice<Keyboard>(DevicePrefix + "Keyboard");
            _mouse = InputSystem.AddDevice<Mouse>(DevicePrefix + "Mouse");

            // 실제 마우스는 잠시 끈다 — 켜 두면 손이 닿는 순간 Mouse.current가 바뀌어 조준이 튀고, 창 클릭이 공격으로 들어간다.
            _disabledRealMice.Clear();
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device is Mouse && device != _mouse && device.enabled)
                {
                    InputSystem.DisableDevice(device);
                    _disabledRealMice.Add(device);
                }
            }

            _mouse.MakeCurrent();
            _absorbKey = ResolveAbsorbKey();
            ResetState();
            Push();
            Debug.Log("[Trailer] 가상 입력 시작(가상 키보드·마우스, 실제 마우스 잠시 끔).");
            return true;
        }

        /// <summary>누른 키·버튼을 모두 뗀다(이동 0, 공격·가드·흡수·대시 해제). 조준은 유지.</summary>
        public static void ReleaseAll()
        {
            _move = Vector2.zero;
            _attack = false;
            _guard = false;
            _absorb = false;
            _dashHeld = false;
            _dashReleaseFrame = -1;
            if (IsActive)
            {
                Push();
            }
        }

        /// <summary>가상 장치 제거 + 실제 마우스 다시 켜기 + 에디터 포커스 설정 복구. 여러 번 불러도 안전.</summary>
        public static void Remove()
        {
            ReleaseAll();
            ResetState();

            // 장치를 지우면 그 장치에 묶인 액션은 취소(canceled)된다 — 눌린 상태가 남지 않는다.
            if (_keyboard != null && _keyboard.added)
            {
                InputSystem.RemoveDevice(_keyboard);
            }

            if (_mouse != null && _mouse.added)
            {
                InputSystem.RemoveDevice(_mouse);
            }

            bool wasActive = _keyboard != null || _mouse != null;
            _keyboard = null;
            _mouse = null;

            foreach (InputDevice device in _disabledRealMice)
            {
                if (device != null && device.added && !device.enabled)
                {
                    InputSystem.EnableDevice(device);
                }
            }

            _disabledRealMice.Clear();

            if (_previousMouse != null && _previousMouse.added)
            {
                _previousMouse.MakeCurrent();
            }

            if (_previousKeyboard != null && _previousKeyboard.added)
            {
                _previousKeyboard.MakeCurrent();
            }

            _previousMouse = null;
            _previousKeyboard = null;
            RestoreFocusSettings();
            RemoveLeftoverDevices();

            if (wasActive)
            {
                Debug.Log("[Trailer] 가상 입력 정리(장치 제거·실제 마우스 복구·포커스 설정 복구).");
            }
        }

        private static void ResetState()
        {
            _aimMode = AimMode.None;
            _aimOrigin = null;
            _warnedAimOverUI = false;
        }

        private static void RemoveLeftoverDevices()
        {
            var leftovers = new List<InputDevice>();
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device.name.StartsWith(DevicePrefix) && device != _keyboard && device != _mouse)
                {
                    leftovers.Add(device);
                }
            }

            foreach (InputDevice device in leftovers)
            {
                InputSystem.RemoveDevice(device);
            }
        }

        // ── 에디터 포커스 설정(메모리에서만) ─────────────────────────

        private static void ApplyFocusSettings()
        {
            if (_settingsChanged)
            {
                return;
            }

            InputSettings settings = InputSystem.settings;
            if (settings == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings)))
            {
                // 에셋이면 값이 프로젝트에 저장될 수 있으므로 건드리지 않는다(영구 변경 금지).
                Debug.LogWarning($"[Trailer] InputSettings가 에셋({AssetDatabase.GetAssetPath(settings)})이라 포커스 설정을 바꾸지 않는다 — " +
                                 "촬영 중 Game View에 포커스를 두어야 가상 입력이 들어간다.");
                return;
            }

            _originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            _originalBackgroundBehavior = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _settingsChanged = true;
        }

        private static void RestoreFocusSettings()
        {
            if (!_settingsChanged)
            {
                return;
            }

            InputSettings settings = InputSystem.settings;
            if (settings != null)
            {
                settings.editorInputBehaviorInPlayMode = _originalEditorBehavior;
                settings.backgroundBehavior = _originalBackgroundBehavior;
            }

            _settingsChanged = false;
        }

        // ── 입력 API ─────────────────────────────────────────────

        /// <summary>이동(WASD). 성분이 ±0.3을 넘는 축의 키를 누른다(8방향).</summary>
        public static void SetMove(Vector2 move)
        {
            _move = move;
            Push();
        }

        /// <summary>공격(좌클릭) 누름/뗌.</summary>
        public static void SetAttack(bool pressed)
        {
            _attack = pressed;
            Push();
        }

        /// <summary>가드(우클릭) 누름/뗌.</summary>
        public static void SetGuard(bool pressed)
        {
            _guard = pressed;
            Push();
        }

        /// <summary>
        /// 흡수 키(PlayerControls Gameplay.Absorb 키보드 바인딩, 기본 E) 누름/뗌. 누른 채 0.5초(Hold) 넘게 두면 performed(흡수 확정),
        /// 그 전에 떼면 canceled(취소) — AbsorptionController 입력 경로 그대로.
        /// </summary>
        public static void SetAbsorb(bool pressed)
        {
            _absorb = pressed;
            Push();
        }

        /// <summary>대시(Space, PlayerControls Dash 바인딩) 한 번 — 누른 뒤 게임 프레임 2개가 지나면 자동으로 뗀다.</summary>
        public static void PressDash()
        {
            _dashHeld = true;
            _dashReleaseFrame = Time.frameCount + 2;
            Push();
        }

        /// <summary>조준점을 월드 좌표로 고정한다(매 update Camera.main.WorldToScreenPoint로 가상 마우스 위치에 넣음).</summary>
        public static void SetAimWorld(Vector3 world)
        {
            _aimMode = AimMode.World;
            _aimWorld = world;
            _warnedAimOverUI = false;
            Push();
        }

        /// <summary>
        /// origin(플레이어)에서 direction 쪽을 조준한다. 매 update origin 위치를 다시 읽어 움직여도 방향이 유지된다.
        /// 화면 좌표가 HUD 위면 같은 방향의 다른 거리(AimDistances 순서)를 고른다.
        /// </summary>
        public static void SetAimDirection(GameObject origin, Vector2 direction)
        {
            _aimMode = AimMode.Direction;
            _aimOrigin = origin;
            _aimDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            _warnedAimOverUI = false;
            Push();
        }

        // ── 상태 넣기 ─────────────────────────────────────────────

        private static void Tick()
        {
            if (!IsActive)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                Remove();
                return;
            }

            if (_dashHeld && Time.frameCount >= _dashReleaseFrame)
            {
                _dashHeld = false;
            }

            Push();
        }

        private static void Push()
        {
            if (!IsActive || !EditorApplication.isPlaying)
            {
                return;
            }

            var keys = new List<Key>(5);
            if (_move.y > 0.3f) keys.Add(Key.W);
            if (_move.y < -0.3f) keys.Add(Key.S);
            if (_move.x < -0.3f) keys.Add(Key.A);
            if (_move.x > 0.3f) keys.Add(Key.D);
            if (_dashHeld) keys.Add(Key.Space);
            if (_absorb && !keys.Contains(_absorbKey)) keys.Add(_absorbKey);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys.ToArray()));

            Vector2 screen = ComputeAimScreen();
            var mouseState = new MouseState { position = screen }
                .WithButton(MouseButton.Left, _attack)
                .WithButton(MouseButton.Right, _guard);
            InputSystem.QueueStateEvent(_mouse, mouseState);

            if (Mouse.current != _mouse)
            {
                _mouse.MakeCurrent();
            }
        }

        private static Vector2 ComputeAimScreen()
        {
            Camera cam = Camera.main;
            if (cam == null || _aimMode == AimMode.None)
            {
                return _lastAimScreen;
            }

            if (_aimMode == AimMode.World)
            {
                _lastAimScreen = cam.WorldToScreenPoint(_aimWorld);
                if (IsScreenPointOverUI(_lastAimScreen))
                {
                    WarnAimOverUI();
                }

                return _lastAimScreen;
            }

            if (_aimOrigin == null)
            {
                return _lastAimScreen;
            }

            Vector2 origin = _aimOrigin.transform.position;
            Vector2 fallback = cam.WorldToScreenPoint(origin + _aimDirection * AimDistances[0]);
            foreach (float distance in AimDistances)
            {
                Vector2 screen = cam.WorldToScreenPoint(origin + _aimDirection * distance);
                if (IsInsideScreen(cam, screen) && !IsScreenPointOverUI(screen))
                {
                    _lastAimScreen = screen;
                    return screen;
                }
            }

            WarnAimOverUI();
            _lastAimScreen = fallback;
            return fallback;
        }

        // 흡수 키: 옵션 창에서 바꾼 키(InputBindingStore.Shared의 오버라이드 적용 경로 effectivePath)를 가상 키보드의 키로 바꾼다.
        // 키보드 바인딩이 없거나 읽지 못하면 기본 E.
        private static Key ResolveAbsorbKey()
        {
            try
            {
                foreach (InputBinding binding in InputBindingStore.Shared.Gameplay.Absorb.bindings)
                {
                    string path = binding.effectivePath;
                    if (string.IsNullOrEmpty(path) || !path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (InputControlPath.TryFindControl(_keyboard, path) is KeyControl key)
                    {
                        return key.keyCode;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Trailer] 흡수 키 바인딩을 읽지 못해 E를 쓴다: {e.Message}");
            }

            return Key.E;
        }

        // 에디터 update 안에서는 Screen.width가 Game View 크기가 아닐 수 있어 카메라 픽셀 크기로 본다.
        private static bool IsInsideScreen(Camera cam, Vector2 screen) =>
            screen.x >= 1f && screen.y >= 1f && screen.x <= cam.pixelWidth - 1f && screen.y <= cam.pixelHeight - 1f;

        private static readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();

        /// <summary>화면 좌표가 UI(레이캐스트 대상) 위인지 — PlayerController의 IsPointerOverGameObject와 같은 기준(EventSystem 레이캐스트).</summary>
        public static bool IsScreenPointOverUI(Vector2 screen)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            _raycastResults.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screen }, _raycastResults);
            return _raycastResults.Count > 0;
        }

        private static void WarnAimOverUI()
        {
            if (_warnedAimOverUI)
            {
                return;
            }

            _warnedAimOverUI = true;
            Debug.LogWarning("[Trailer] 조준 화면 좌표가 HUD(UI) 위라 공격이 막힐 수 있다 — 플레이어를 화면 가운데 쪽 넓은 곳에 두고 다시 찍는다.");
        }
    }
}
