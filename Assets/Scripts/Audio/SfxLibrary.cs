using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbsorbRoguelike.Audio
{
    /// <summary>
    /// 효과음 목록(2026-10-14). 항목 = id 하나(SfxId) + 변형 클립들 + 재생 조정값.
    /// 에셋: Assets/Resources/Audio/SfxLibrary.asset — Sfx가 Resources로 읽는다(MusicDirector와 같은 관례, 씬 배치 없음).
    /// 클립 배열·출처는 에디터 메뉴 Tools/Absorb/사운드 가져오기(SfxImporter)가 채우고, 조정값(볼륨 등)은 가져오기를 다시 해도 보존된다.
    /// 조정값은 레벨 디자인 시트 '사운드' 탭에서 고친다. 구조·재생 위치: docs/04_구현위키/효과음.md.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/Audio/Sfx Library", fileName = "SfxLibrary")]
    public class SfxLibrary : ScriptableObject
    {
        public const string ResourcesPath = "Audio/SfxLibrary";

        /// <summary>클립을 어디서 가져왔는지(가져오기 메뉴가 기록, 시트에 읽기 전용으로 표시).</summary>
        public enum ClipSource
        {
            Missing,
            Synth,
            User,
        }

        [Serializable]
        public class Entry
        {
            public string id;
            [Tooltip("변형 클립(_1, _2…). 재생 때 무작위로 하나")]
            public AudioClip[] clips = Array.Empty<AudioClip>();
            [Range(0f, 1f)] public float volume = 0.8f;
            [Tooltip("재생마다 음높이를 1 ± 이 값 안에서 무작위로")]
            public float pitchJitter = 0.05f;
            [Tooltip("같은 id를 이 시간(초) 안에 다시 재생하면 건너뜀")]
            public float minInterval = 0.03f;
            [Tooltip("같은 id가 동시에 날 수 있는 최대 수(0 = 제한 없음)")]
            public int maxConcurrent = 4;
            [Tooltip("true = 화면 전체로 나는 소리(UI·보스 포효 등), false = 위치 소리")]
            public bool is2D;
            [Tooltip("가져오기 메뉴가 기록 — 사용자 파일 / 합성본 / 없음")]
            public ClipSource source;
        }

        public List<Entry> entries = new List<Entry>();

        [NonSerialized] private Dictionary<string, Entry> _byId;

        /// <summary>id로 항목 찾기(없으면 null). 처음 부를 때 사전을 만든다.</summary>
        public Entry Find(string id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (Entry entry in entries)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.id))
                    {
                        _byId[entry.id] = entry;
                    }
                }
            }

            return id != null && _byId.TryGetValue(id, out Entry found) ? found : null;
        }

        /// <summary>에디터에서 항목을 추가·삭제한 뒤 조회 사전을 다시 만들게 한다.</summary>
        public void InvalidateCache() => _byId = null;

        private void OnValidate() => _byId = null;
    }
}
