using System;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>
    /// 볼륨 설정 저장소(2026-09-30, 타이틀·일시정지·옵션). 전체/배경음/효과음 0~100 정수를 PlayerPrefs에 저장한다.
    /// 전체 볼륨은 AudioListener.volume으로 바로 적용하고, 배경음·효과음은 값만 제공한다 — 배경음은 Audio.MusicDirector가 매 프레임
    /// <see cref="Bgm01"/>을 읽는다(2026-10-08). 효과음(<see cref="Sfx01"/>)은 효과음 연결 때 같은 방식으로 읽는다.
    /// </summary>
    public static class AudioSettingsStore
    {
        public const string MasterKey = "Absorb.Volume.Master";
        public const string BgmKey = "Absorb.Volume.Bgm";
        public const string SfxKey = "Absorb.Volume.Sfx";
        private const int DefaultVolume = 100;

        private static int _master = DefaultVolume;
        private static int _bgm = DefaultVolume;
        private static int _sfx = DefaultVolume;

        public static event Action Changed;

        public static int Master
        {
            get => _master;
            set
            {
                _master = Mathf.Clamp(value, 0, 100);
                AudioListener.volume = _master / 100f;
                Save(MasterKey, _master);
            }
        }

        public static int Bgm
        {
            get => _bgm;
            set
            {
                _bgm = Mathf.Clamp(value, 0, 100);
                Save(BgmKey, _bgm);
            }
        }

        public static int Sfx
        {
            get => _sfx;
            set
            {
                _sfx = Mathf.Clamp(value, 0, 100);
                Save(SfxKey, _sfx);
            }
        }

        public static float Bgm01 => _bgm / 100f;
        public static float Sfx01 => _sfx / 100f;

        /// <summary>게임 시작 시 1회: 저장된 볼륨·키 설정을 불러온다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LoadOnStart()
        {
            _master = Load(MasterKey);
            _bgm = Load(BgmKey);
            _sfx = Load(SfxKey);
            AudioListener.volume = _master / 100f;
            InputBindingStore.EnsureLoaded();
        }

        private static int Load(string key)
        {
            try
            {
                return Mathf.Clamp(PlayerPrefs.GetInt(key, DefaultVolume), 0, 100);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AudioSettingsStore] PlayerPrefs read failed: {e.Message}");
                return DefaultVolume;
            }
        }

        private static void Save(string key, int value)
        {
            try
            {
                PlayerPrefs.SetInt(key, value);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AudioSettingsStore] PlayerPrefs save failed: {e.Message}");
            }

            Changed?.Invoke();
        }
    }
}
