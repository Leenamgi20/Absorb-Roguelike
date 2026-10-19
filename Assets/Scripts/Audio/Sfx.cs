using System.Collections.Generic;
using AbsorbRoguelike.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AbsorbRoguelike.Audio
{
    /// <summary>
    /// 효과음 재생 진입점(2026-10-14). 어디서든 <c>Sfx.Play(SfxId.Hit, position)</c> 한 줄로 쓴다 — 네트워크 없음, 부른 화면에서만 난다.
    /// 그래서 두 화면에서 같이 나야 하는 소리는 ClientRpc·SyncVar 훅·표시 지점(각 클라에서 도는 곳)에서 부른다(docs/04_구현위키/효과음.md).
    /// - 게임 시작 때 DontDestroyOnLoad 오브젝트 하나 + AudioSource 풀을 만든다(MusicDirector와 같은 RuntimeInitializeOnLoadMethod 관례).
    /// - 목록: Resources/Audio/SfxLibrary.asset(SfxLibrary). 클립 없는 id는 조용히 건너뛰고, 에디터·Development Build에서만 id당 1회 경고.
    /// - 음량 = 항목 volume × 옵션 효과음(AudioSettingsStore.Sfx01), 매 프레임 다시 곱해 슬라이더가 반복 소리에도 바로 반영.
    ///   전체 볼륨(Master)은 AudioSettingsStore가 AudioListener.volume으로 이미 적용하므로 여기서 또 곱하지 않는다(두 번 곱하면 제곱으로 줄어듦).
    /// - 변형 무작위·음높이 무작위(pitchJitter), 같은 id 최소 간격(minInterval)·최대 동시 수(maxConcurrent).
    /// - 반복 소리는 StartLoop/StopLoop(핸들). 따라갈 Transform이 사라지거나 꺼지면, 씬이 바뀌면 자동으로 멈춘다.
    /// - 배치 모드(서버 빌드·테스트 러너)에서는 아무것도 하지 않는다.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        private const int PoolSize = 24;
        // 위치 소리: 2D 탑다운이라 소스를 리스너와 같은 z에 두고, 이 거리(월드 단위) 안에서 줄어든다.
        private const float MinDistance = 9f;
        private const float MaxDistance = 28f;
        private const float SpatialBlend3D = 0.85f; // 1이면 좌우 쏠림이 너무 강해 조금 남긴다

        private sealed class Voice
        {
            public AudioSource Source;
            public string Id;
            public float BaseVolume;
            public int Handle;          // 0 = 단발
            public Transform Follow;
            public bool HasFollow;
            public float StartedAt;
        }

        private static Sfx _instance;
        private static SfxLibrary _library;
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        private static int _nextHandle = 1;
        private static bool _pendingClick; // 이번 프레임 클릭음 — LateUpdate에서 재생, 같은 프레임에 열기음이 오면 취소

        private readonly List<Voice> _voices = new List<Voice>(PoolSize);

        /// <summary>쓰는 효과음 목록(없으면 null). 시트 미리 듣기·검증용.</summary>
        public static SfxLibrary Library => _library;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null || Application.isBatchMode)
            {
                return;
            }

            var go = new GameObject("Sfx");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Sfx>();
        }

        private void Awake()
        {
            _library = Resources.Load<SfxLibrary>(SfxLibrary.ResourcesPath);
            if (_library == null)
            {
                Debug.LogWarning($"[Sfx] Resources/{SfxLibrary.ResourcesPath} 없음 — 효과음 없이 동작(Tools/Absorb/사운드 가져오기로 생성)");
            }

            for (int i = 0; i < PoolSize; i++)
            {
                var child = new GameObject("Voice" + i);
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = MinDistance;
                source.maxDistance = MaxDistance;
                source.dopplerLevel = 0f;
                _voices.Add(new Voice { Source = source });
            }

            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnSceneChanged(Scene from, Scene to) => StopAllLoops();

        // ── 공개 API ─────────────────────────────────────────

        /// <summary>단발 재생. position이 없거나 항목이 2D면 화면 전체로 난다.</summary>
        public static void Play(string id, Vector3? position = null)
        {
            // 버튼으로 창을 열고 닫을 때 클릭음 + 열기음이 겹치지 않게 — 클릭음은 프레임 끝까지 미뤘다가,
            // 같은 프레임에 열기음이 오면 클릭음을 버리고 열기음만 낸다(2026-10-16, QA #57)
            if (id == SfxId.UiClick && position == null)
            {
                _pendingClick = true;
                return;
            }

            if (id == SfxId.UiOpen)
            {
                _pendingClick = false;
            }

            if (_instance != null)
            {
                _instance.PlayInternal(id, position, null, loop: false);
            }
        }

        /// <summary>반복 재생 시작 → 핸들(0이면 시작 못 함). follow가 있으면 따라가고, 사라지면 자동 정지.</summary>
        public static int StartLoop(string id, Transform follow = null)
        {
            return _instance != null ? _instance.PlayInternal(id, follow != null ? follow.position : (Vector3?)null, follow, loop: true) : 0;
        }

        /// <summary>반복 정지. 핸들을 0으로 돌려놓는다(이미 멈췄거나 0이어도 안전).</summary>
        public static void StopLoop(ref int handle)
        {
            if (handle != 0 && _instance != null)
            {
                foreach (Voice voice in _instance._voices)
                {
                    if (voice.Handle == handle)
                    {
                        Release(voice);
                    }
                }
            }

            handle = 0;
        }

        /// <summary>모든 반복 소리 정지(씬 전환·타이틀 복귀).</summary>
        public static void StopAllLoops()
        {
            if (_instance == null)
            {
                return;
            }

            foreach (Voice voice in _instance._voices)
            {
                if (voice.Handle != 0)
                {
                    Release(voice);
                }
            }
        }

        // ── 내부 ─────────────────────────────────────────────

        private int PlayInternal(string id, Vector3? position, Transform follow, bool loop)
        {
            SfxLibrary.Entry entry = _library != null ? _library.Find(id) : null;
            if (entry == null || entry.clips == null || entry.clips.Length == 0)
            {
                WarnOnce(id);
                return 0;
            }

            float now = Time.unscaledTime;
            if (!loop && entry.minInterval > 0f && LastPlayed.TryGetValue(id, out float last) && now - last < entry.minInterval)
            {
                return 0;
            }

            if (entry.maxConcurrent > 0 && CountPlaying(id) >= entry.maxConcurrent)
            {
                if (loop)
                {
                    return 0;
                }

                Voice oldest = OldestOneShot(id);
                if (oldest == null)
                {
                    return 0;
                }

                Release(oldest);
            }

            AudioClip clip = entry.clips[entry.clips.Length == 1 ? 0 : Random.Range(0, entry.clips.Length)];
            if (clip == null)
            {
                WarnOnce(id);
                return 0;
            }

            Voice voice = FreeVoice();
            if (voice == null)
            {
                return 0;
            }

            LastPlayed[id] = now;
            AudioSource source = voice.Source;
            bool is3D = !entry.is2D && position.HasValue;
            source.spatialBlend = is3D ? SpatialBlend3D : 0f;
            if (is3D)
            {
                voice.Source.transform.position = OnListenerPlane(position.Value);
            }

            source.clip = clip;
            source.loop = loop;
            source.pitch = 1f + Random.Range(-entry.pitchJitter, entry.pitchJitter);
            voice.Id = id;
            voice.BaseVolume = entry.volume;
            voice.Handle = loop ? _nextHandle++ : 0;
            voice.Follow = follow;
            voice.HasFollow = follow != null;
            voice.StartedAt = now;
            source.volume = voice.BaseVolume * AudioSettingsStore.Sfx01;
            source.Play();
            return voice.Handle;
        }

        private void LateUpdate()
        {
            if (_pendingClick)
            {
                _pendingClick = false;
                PlayInternal(SfxId.UiClick, null, null, loop: false);
            }

            float sfx = AudioSettingsStore.Sfx01;
            foreach (Voice voice in _voices)
            {
                if (voice.Id == null)
                {
                    continue;
                }

                if (!voice.Source.isPlaying && voice.Handle == 0)
                {
                    voice.Id = null; // 단발 끝
                    continue;
                }

                if (voice.HasFollow)
                {
                    // 따라가던 대상이 파괴·비활성 → 반복 소리가 남지 않게 정지
                    if (voice.Follow == null || !voice.Follow.gameObject.activeInHierarchy)
                    {
                        Release(voice);
                        continue;
                    }

                    if (voice.Source.spatialBlend > 0f)
                    {
                        voice.Source.transform.position = OnListenerPlane(voice.Follow.position);
                    }
                }

                voice.Source.volume = voice.BaseVolume * sfx;
            }
        }

        private static Vector3 OnListenerPlane(Vector3 position)
        {
            AudioListener listener = FindListener();
            if (listener != null)
            {
                position.z = listener.transform.position.z;
            }

            return position;
        }

        private static AudioListener _listener;

        private static AudioListener FindListener()
        {
            if (_listener == null || !_listener.isActiveAndEnabled)
            {
                _listener = FindAnyObjectByType<AudioListener>();
            }

            return _listener;
        }

        private int CountPlaying(string id)
        {
            int count = 0;
            foreach (Voice voice in _voices)
            {
                if (voice.Id == id && voice.Source.isPlaying)
                {
                    count++;
                }
            }

            return count;
        }

        private Voice OldestOneShot(string id)
        {
            Voice oldest = null;
            foreach (Voice voice in _voices)
            {
                if (voice.Handle == 0 && voice.Source.isPlaying && (id == null || voice.Id == id) && (oldest == null || voice.StartedAt < oldest.StartedAt))
                {
                    oldest = voice;
                }
            }

            return oldest;
        }

        private Voice FreeVoice()
        {
            foreach (Voice voice in _voices)
            {
                if (voice.Handle == 0 && !voice.Source.isPlaying)
                {
                    return voice;
                }
            }

            // 다 쓰는 중이면 가장 오래된 단발을 끊는다(반복 소리는 끊지 않음).
            Voice oldest = OldestOneShot(null);
            if (oldest != null)
            {
                Release(oldest);
            }

            return oldest;
        }

        private static void Release(Voice voice)
        {
            voice.Source.Stop();
            voice.Source.clip = null;
            voice.Source.loop = false;
            voice.Id = null;
            voice.Handle = 0;
            voice.Follow = null;
            voice.HasFollow = false;
        }

        private static void WarnOnce(string id)
        {
            if ((Application.isEditor || Debug.isDebugBuild) && Warned.Add(id ?? "(null)"))
            {
                Debug.LogWarning($"[Sfx] 클립 없음: {id} — 건너뜀(Art_Source/sound/에 파일을 넣고 Tools/Absorb/사운드 가져오기)");
            }
        }
    }
}
