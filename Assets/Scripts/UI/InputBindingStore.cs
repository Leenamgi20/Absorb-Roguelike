using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 키 재설정(리바인딩) 저장소(2026-09-30, 타이틀·일시정지·옵션). 입력 액션 에셋은 건드리지 않고 '바인딩 오버라이드'만
    /// JSON으로 PlayerPrefs에 저장한다. 게임 중 만들어지는 <see cref="PlayerControls"/> 인스턴스(PlayerController·AbsorptionController가
    /// 각자 하나씩)를 <see cref="Register"/>로 모아 두고, 저장값이 바뀌면 모두에 같은 JSON을 다시 적용한다.
    /// 옵션 창의 리바인딩은 편집용 공유 인스턴스(<see cref="Shared"/>)에서 한다.
    /// </summary>
    public static class InputBindingStore
    {
        public const string PrefsKey = "Absorb.BindingOverrides";

        private static readonly List<PlayerControls> Registered = new List<PlayerControls>();
        private static PlayerControls _shared;
        private static string _json;
        private static bool _loaded;

        /// <summary>저장값이 바뀌었을 때(리바인딩 완료·기본값 복원).</summary>
        public static event Action Changed;

        /// <summary>옵션 창 편집용 인스턴스(맵은 켜지 않는다).</summary>
        public static PlayerControls Shared
        {
            get
            {
                EnsureLoaded();
                return _shared;
            }
        }

        /// <summary>저장된 오버라이드를 한 번 읽어 공유 인스턴스에 적용한다(여러 번 불러도 1회만).</summary>
        public static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _shared = new PlayerControls();
            try
            {
                _json = PlayerPrefs.GetString(PrefsKey, "");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InputBindingStore] PlayerPrefs read failed: {e.Message}");
                _json = "";
            }

            if (!Apply(_shared, _json))
            {
                _json = ""; // 파싱 실패 — 기본값 사용
            }
        }

        /// <summary>게임 중 생성한 입력 인스턴스를 등록하고 저장된 키 설정을 적용한다.</summary>
        public static void Register(PlayerControls controls)
        {
            if (controls == null)
            {
                return;
            }

            EnsureLoaded();
            if (!Registered.Contains(controls))
            {
                Registered.Add(controls);
            }

            Apply(controls, _json);
        }

        public static void Unregister(PlayerControls controls)
        {
            Registered.Remove(controls);
        }

        /// <summary>공유 인스턴스의 현재 오버라이드를 저장하고 등록된 모든 인스턴스에 다시 적용한다.</summary>
        public static void SaveFromShared()
        {
            EnsureLoaded();
            _json = _shared.asset.SaveBindingOverridesAsJson();
            try
            {
                PlayerPrefs.SetString(PrefsKey, _json);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InputBindingStore] PlayerPrefs save failed: {e.Message}");
            }

            ApplyToRegistered();
        }

        /// <summary>모든 키를 기본값으로 되돌리고 저장 키를 지운다.</summary>
        public static void ResetAll()
        {
            EnsureLoaded();
            _shared.asset.RemoveAllBindingOverrides();
            _json = "";
            try
            {
                PlayerPrefs.DeleteKey(PrefsKey);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InputBindingStore] PlayerPrefs delete failed: {e.Message}");
            }

            ApplyToRegistered();
        }

        private static void ApplyToRegistered()
        {
            for (int i = Registered.Count - 1; i >= 0; i--)
            {
                if (Registered[i] == null)
                {
                    Registered.RemoveAt(i);
                    continue;
                }

                Apply(Registered[i], _json);
            }

            Changed?.Invoke();
        }

        // 오버라이드 전부 교체. 실패하면 경고 후 기본값(오버라이드 없음)으로 둔다.
        private static bool Apply(PlayerControls controls, string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                {
                    controls.asset.RemoveAllBindingOverrides();
                }
                else
                {
                    controls.asset.LoadBindingOverridesFromJson(json, true);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InputBindingStore] binding overrides parse failed — using defaults: {e.Message}");
                try
                {
                    controls.asset.RemoveAllBindingOverrides();
                }
                catch (Exception)
                {
                    // 무시 — 기본값 그대로
                }

                return false;
            }
        }
    }
}
