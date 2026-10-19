using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Network;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Audio
{
    /// <summary>
    /// 배경음악(2026-10-08, 사용자 제작 — VARCO Sound). 곡은 `Resources/Audio/Music/`, 목록·프롬프트는 docs/03_아트/사운드_제작_목록.md.
    /// 지금 상황에 맞는 곡을 매 프레임 고르고, 바뀌면 두 AudioSource로 겹쳐 넘긴다(크로스페이드). 각 화면이 로컬로 판단한다.
    /// - 타이틀: 접속 전, 시작 메뉴(방 대기 화면 포함)가 떠 있을 때
    /// - 던전: 게임 중 평소
    /// - 보스: 보스 방 문이 잠긴 동안(RoomLockSync.BossFightActive). 마지막 층 전용곡(bgm_boss_final)이 있으면 마지막 층에서 그것.
    /// - 결과 화면: 승리·패배 곡(완곡이라 결과 화면 동안 반복)
    /// 곡 끝이 이음매 없는 반복이 아니라서(페이드아웃 등) 끝나기 LoopFade초 전에 처음부터 다시 겹쳐 시작한다.
    /// 볼륨 = 곡별 보정 × 기본 음량 × 옵션 배경음(AudioSettingsStore.Bgm01). 전체 볼륨은 AudioListener가 맡는다.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        private enum Track { None, Title, Dungeon, Boss, BossFinal, Victory, Defeat }

        private const float BaseVolume = 0.55f;  // [PLAYTEST] 효과음 자리를 남기고 깔리는 정도
        private const float FadeTime = 1.2f;
        private const float BossFadeTime = 0.6f; // 보스 문이 잠기는 순간은 빠르게
        private const float LoopFade = 2.5f;

        private static readonly string[] ClipNames =
        {
            null, "bgm_title", "bgm_dungeon", "bgm_boss", "bgm_boss_final", "jingle_victory", "jingle_defeat",
        };

        // 곡별 음량 보정(2026-10-08 측정 RMS: 타이틀 -14.4dB, 던전 -12.9, 보스 -12.0, 승리 -12.3, 패배 -12.9 → 타이틀 기준으로 맞춤)
        private static readonly float[] Gains = { 0f, 1f, 0.84f, 0.76f, 0.76f, 0.79f, 0.84f };

        private readonly AudioClip[] _clips = new AudioClip[ClipNames.Length];
        private AudioSource[] _sources;
        // 크로스페이드 진행(0~1)과 곡별 보정을 소스마다 따로 두고, 실제 음량은 매 프레임 볼륨 설정을 곱해 계산한다 —
        // 그래야 옵션 배경음 슬라이더가 바로 반영된다(2026-10-08: 음량 자체를 서서히 옮기던 방식은 슬라이더를 내려도 거의 안 줄었다).
        private readonly float[] _weights = new float[2];
        private readonly float[] _sourceGains = new float[2];
        private int _active;
        private Track _current = Track.None;
        private float _fadeSpeed = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("MusicDirector");
            DontDestroyOnLoad(go);
            go.AddComponent<MusicDirector>();
        }

        private void Awake()
        {
            _sources = new[] { MakeSource(), MakeSource() };
            for (int i = 1; i < ClipNames.Length; i++)
            {
                _clips[i] = Resources.Load<AudioClip>("Audio/Music/" + ClipNames[i]);
            }
        }

        private AudioSource MakeSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }

        private void Update()
        {
            Track want = ChooseTrack();
            if (want != _current)
            {
                SwitchTo(want, want == Track.Boss || want == Track.BossFinal ? BossFadeTime : FadeTime);
            }

            AudioSource main = _sources[_active];
            AudioClip clip = _clips[(int)_current];
            if (clip != null && main.isPlaying && main.time >= clip.length - LoopFade)
            {
                SwitchTo(_current, LoopFade, force: true); // 곡 끝 — 처음부터 겹쳐 다시
            }

            float step = Time.unscaledDeltaTime * _fadeSpeed;
            float volume = BaseVolume * AudioSettingsStore.Bgm01;
            for (int i = 0; i < _sources.Length; i++)
            {
                AudioSource s = _sources[i];
                bool on = i == _active && _current != Track.None && s.clip == _clips[(int)_current];
                _weights[i] = Mathf.MoveTowards(_weights[i], on ? 1f : 0f, step);
                s.volume = _weights[i] * _sourceGains[i] * volume;
                if (!on && s.isPlaying && _weights[i] <= 0f)
                {
                    s.Stop();
                }
            }
        }

        private Track ChooseTrack()
        {
            if (RunEndScreenController.IsShown)
            {
                return RunEndScreenController.IsVictory ? Track.Victory : Track.Defeat;
            }

            StartMenuController menu = StartMenuController.Instance;
            if (!NetworkClient.isConnected || (menu != null && menu.IsMenuVisible))
            {
                return Track.Title;
            }

            RoomLockSync locks = RoomLockSync.Instance;
            if (locks != null && locks.BossFightActive)
            {
                FloorProgression progression = FloorProgression.Instance;
                return progression != null && progression.IsLastFloor && _clips[(int)Track.BossFinal] != null ? Track.BossFinal : Track.Boss;
            }

            return Track.Dungeon;
        }

        private void SwitchTo(Track track, float fade, bool force = false)
        {
            if (track == _current && !force)
            {
                return;
            }

            _current = track;
            _fadeSpeed = 1f / Mathf.Max(0.05f, fade);
            AudioClip clip = _clips[(int)track];
            if (clip == null)
            {
                return; // 곡이 아직 없으면 이전 곡만 줄어든다
            }

            _active = 1 - _active;
            AudioSource next = _sources[_active];
            next.Stop();
            next.clip = clip;
            next.volume = 0f;
            _weights[_active] = 0f;
            _sourceGains[_active] = Gains[(int)track];
            next.time = 0f;
            next.Play();
        }
    }
}
