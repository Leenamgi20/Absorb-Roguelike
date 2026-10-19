using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.UI
{
    /// <summary>화면 모드(2026-10-07, 옵션 창). 값 = PlayerPrefs 저장값.
    /// 2026-10-13: 독점 전체 화면(예전 저장값 2)을 없앴다 — 전체 화면은 테두리 없는 전체 화면 창 하나(QA #47, Parsec 등에서 창이 사라짐).</summary>
    public enum DisplayMode
    {
        Windowed = 0,
        Fullscreen = 1, // 테두리 없는 전체 화면 창(FullScreenWindow). 예전 '테두리 없는 창'과 같은 저장값
    }

    /// <summary>
    /// 화면 모드·해상도 설정 저장소(2026-10-07 사용자 요청). PlayerPrefs에 저장하고 게임 시작 때 자동 적용한다.
    /// - 창 모드: 고른 해상도 크기의 창(해상도 고르기는 창 모드에서만).
    /// - 전체 화면: 테두리 없는 전체 화면 창, 항상 모니터 해상도(2026-10-07 수정 — 전체 화면에서 모니터와 다른 해상도를 고르면
    ///   모니터 표시 모드가 바뀌어 화면이 아예 안 보이는 문제가 있어 해상도를 고르지 않는다). 독점 전체 화면은 2026-10-13에 없앴다
    ///   (Parsec 원격 조작에서 전환할 때마다 창이 사라짐 — QA #47. 640×360 도트 게임이라 독점 모드의 이득이 거의 없다).
    /// 해상도 목록(창 크기): 흔히 쓰는 16:9(1280×720, 1600×900, 1920×1080 FHD, 2560×1440 QHD, 3840×2160 4K) 중
    /// 모니터보다 작은 것(같은 크기 창은 제목 표시줄·작업 표시줄 때문에 화면 밖으로 나간다). 하나도 없으면 960×540.
    /// 게임 화면은 640×360 도트 기준이라 720·1080·1440·2160(정수배)이 가장 또렷하다.
    /// 처음 실행 기본값 = 전체 화면.
    /// </summary>
    public static class DisplaySettingsStore
    {
        private const string ModeKey = "display_mode";
        private const string WidthKey = "display_width";
        private const string HeightKey = "display_height";

        private static readonly Vector2Int[] Common =
        {
            new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1440), new Vector2Int(3840, 2160),
        };

        public static event Action Changed;

        public static DisplayMode Mode { get; private set; } = DisplayMode.Fullscreen;
        public static Vector2Int Resolution { get; private set; }

        /// <summary>모니터(주 디스플레이) 해상도.</summary>
        public static Vector2Int Native
        {
            get
            {
                int w = Display.main != null ? Display.main.systemWidth : Screen.currentResolution.width;
                int h = Display.main != null ? Display.main.systemHeight : Screen.currentResolution.height;
                return new Vector2Int(Mathf.Max(640, w), Mathf.Max(360, h));
            }
        }

        public static List<Vector2Int> AvailableResolutions()
        {
            Vector2Int native = Native;
            var list = new List<Vector2Int>();
            foreach (Vector2Int r in Common)
            {
                if (r.x < native.x && r.y < native.y)
                {
                    list.Add(r);
                }
            }

            if (list.Count == 0)
            {
                list.Add(new Vector2Int(960, 540));
            }

            return list;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LoadAndApply()
        {
#if UNITY_WEBGL
            // 브라우저판: 캔버스 크기는 WebGL 템플릿이 정하고, 전체 화면은 사용자 제스처(버튼 클릭)가 있어야 한다 —
            // 시작 때 해상도를 적용하지 않고 PlayerPrefs 화면 키도 읽지 않는다. 전체 화면은 ToggleFullscreen()만 쓴다.
#else
            Vector2Int native = Native;
            // 예전 저장값 2(독점 전체 화면)도 전체 화면(1)으로 읽는다.
            Mode = (DisplayMode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, (int)DisplayMode.Fullscreen), 0, 1);
            List<Vector2Int> list = AvailableResolutions();
            Resolution = new Vector2Int(PlayerPrefs.GetInt(WidthKey, 0), PlayerPrefs.GetInt(HeightKey, 0));
            if (!list.Contains(Resolution))
            {
                Resolution = list[list.Count - 1]; // 창 기본 크기 = 모니터보다 작은 것 중 가장 큰 것
            }

            Apply();
#endif
        }

        public static void SetMode(DisplayMode mode)
        {
            Mode = mode;
            Save();
            Apply();
        }

        /// <summary>창 크기 목록에서 한 칸 앞/뒤(dir = -1/+1)로 바꾼다. 창 모드에서만 — 전체 화면 계열은 모니터 해상도 고정.</summary>
        public static void StepResolution(int dir)
        {
            if (Mode != DisplayMode.Windowed)
            {
                return;
            }

            List<Vector2Int> list = AvailableResolutions();
            int index = list.IndexOf(Resolution);
            if (index < 0)
            {
                index = list.Count - 1;
            }

            index = Mathf.Clamp(index + dir, 0, list.Count - 1);
            Resolution = list[index];
            Save();
            Apply();
        }

        /// <summary>지금 실제로 쓰는 해상도(창 모드만 고른 크기, 나머지는 모니터 해상도).</summary>
        public static Vector2Int EffectiveResolution => Mode == DisplayMode.Windowed ? Resolution : Native;

        /// <summary>브라우저판(WebGL) 전용: 전체 화면 켜기/끄기. 옵션 창 버튼 클릭(사용자 제스처) 안에서 불러야 브라우저가 허용한다.</summary>
        public static void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            Changed?.Invoke();
        }

        private static void Apply()
        {
#if !UNITY_EDITOR && !UNITY_WEBGL // 에디터 게임 창 크기는 건드리지 않는다. 브라우저판은 캔버스 크기를 템플릿이 정한다
            Vector2Int r = EffectiveResolution;
            FullScreenMode mode = Mode == DisplayMode.Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            Screen.SetResolution(r.x, r.y, mode);
#endif
            Changed?.Invoke();
        }

        private static void Save()
        {
            PlayerPrefs.SetInt(ModeKey, (int)Mode);
            PlayerPrefs.SetInt(WidthKey, Resolution.x);
            PlayerPrefs.SetInt(HeightKey, Resolution.y);
            PlayerPrefs.Save();
        }
    }
}
