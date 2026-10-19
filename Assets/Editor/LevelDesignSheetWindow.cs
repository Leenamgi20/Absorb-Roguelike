using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AbsorbRoguelike.Absorption;
using AbsorbRoguelike.Audio;
using AbsorbRoguelike.Combat;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Monster;
using AbsorbRoguelike.Player;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AbsorbRoguelike.EditorTools
{
    /// <summary>
    /// 레벨 디자인 시트(2026-10-13 1차, 2026-10-14 2차). 흩어진 수치 에셋(Assets/Data/…)을 탭 9개의 표로 한 화면에서 고친다.
    /// 2026-10-14: '사운드' 탭 — SfxLibrary(Resources/Audio/SfxLibrary.asset) 효과음별 볼륨·간격 등 + 미리 듣기 + 사운드 가져오기 버튼.
    /// 별도 데이터 사본은 만들지 않는다 — 각 칸은 그 에셋의 SerializedProperty를 그대로 편집하고(Undo 지원), 바뀌면 바로 저장한다.
    /// 에셋은 타입으로 찾으므로(AssetDatabase.FindAssets "t:타입") 새 동물·보스 에셋을 만들면 표에 자동으로 줄이 생긴다.
    /// 수치 이름·설명은 이름 사전(Assets/Editor/LevelDesignSheetFields.tsv)에서, 창 고정 문구는 아래 UiText 표에서 한국어/English로 고른다.
    /// 사용법·사전 규칙: docs/04_구현위키/레벨_디자인_시트.md.
    /// </summary>
    public class LevelDesignSheetWindow : EditorWindow
    {
        // 표 크기(2026-10-14 수정 — 고정 폭·고정 제목 높이에서 긴 이름이 잘리거나 칸이 아래로 밀리던 문제).
        // 표는 GUILayout 자동 배치 대신 칸 위치를 직접 계산해 그린다(DrawGrid): 이름 열과 숫자 열의 줄 높이가 항상 같고,
        // 열 폭·제목 높이는 글자 길이에 맞춰 정한다.
        private const float MinCellWidth = 76f;
        private const float MaxCellWidth = 140f;
        private const float MinNameWidth = 110f;
        private const float MaxNameWidth = 260f;
        private const float ScrollbarHeight = 15f;
        private const int TabCount = 9;
        private static float RowHeight => EditorGUIUtility.singleLineHeight + 6f;

        // ── 언어 ─────────────────────────────────────────────
        // 시트 전용 언어(게임 언어 설정과 무관). EditorPrefs에 저장, 기본 한국어.

        private const string LangPrefKey = "Absorb.LevelDesignSheet.Lang";
        private static bool? _english;

        /// <summary>true = English, false = 한국어. 바꾸면 EditorPrefs에 저장한다.</summary>
        public static bool English
        {
            get => _english ??= EditorPrefs.GetInt(LangPrefKey, 0) == 1;
            set
            {
                _english = value;
                EditorPrefs.SetInt(LangPrefKey, value ? 1 : 0);
            }
        }

        private static string L(string ko, string en) => English ? en : ko;

        // 창 고정 문구 표(한 곳에서 관리). 형식 문자열은 string.Format 인자 {0}…을 쓴다.
        private static readonly Dictionary<string, (string ko, string en)> UiText = new Dictionary<string, (string ko, string en)>
        {
            { "window.title", ("레벨 디자인 시트", "Level Design Sheet") },
            { "tab.0", ("잡몹", "Monsters") },
            { "tab.1", ("동물별 패턴", "Animal patterns") },
            { "tab.2", ("정예", "Elite") },
            { "tab.3", ("보스", "Boss") },
            { "tab.4", ("상태이상", "Status effects") },
            { "tab.5", ("난이도", "Difficulty") },
            { "tab.6", ("동물 스킬", "Animal skills") },
            { "tab.7", ("패시브 특성", "Passive traits") },
            { "tab.8", ("사운드", "Sound") },
            { "info.sound", ("Assets/Resources/Audio/SfxLibrary.asset — 효과음 40개의 재생 조정값. 이름에 마우스를 올리면 언제 나는 소리인지, ▶로 미리 듣기(변형 중 무작위 하나).", "Assets/Resources/Audio/SfxLibrary.asset — playback settings for the 40 sound effects. Hover a name to see when it plays; ▶ previews it (a random variant).") },
            { "sound.replace", ("바꾸기: Art_Source/sound/에 같은 이름(sfx_hit.wav, 여러 개면 sfx_hit_1.wav·sfx_hit_2.wav)으로 넣고 '사운드 가져오기' — 사용자 파일이 합성본보다 우선, 볼륨 등은 그대로 유지.", "To replace: put a file with the same name in Art_Source/sound/ (sfx_hit.wav, or sfx_hit_1.wav, sfx_hit_2.wav for variants) and press 'Import sounds' — your files win over the synth ones; volume etc. are kept.") },
            { "sound.import", ("사운드 가져오기", "Import sounds") },
            { "sound.import.tip", ("Art_Source/sound/(사용자)·Art_Source/sound/synth/(합성본)를 Assets/Audio/SFX/로 복사하고 이 목록에 연결(메뉴 Tools/Absorb/사운드 가져오기와 같음)", "Copies Art_Source/sound/ (yours) and Art_Source/sound/synth/ (synth) into Assets/Audio/SFX/ and links them here (same as menu Tools/Absorb/사운드 가져오기)") },
            { "sound.noLibrary", ("SfxLibrary 에셋이 없습니다 — '사운드 가져오기'를 누르면 만들어집니다.", "No SfxLibrary asset — press 'Import sounds' to create it.") },
            { "corner.sound", ("효과음", "Sound") },
            { "col.variants", ("변형 수", "Variants") },
            { "col.variants.tip", ("연결된 클립 개수(_1, _2…). 재생 때 무작위로 하나. 읽기 전용 — 파일을 넣고 가져오기로 바꾼다.", "Number of linked clips (_1, _2…). One is picked at random. Read-only — change by adding files and importing.") },
            { "col.source", ("출처", "Source") },
            { "col.source.tip", ("사용자 파일 / 합성본 / 없음(가져오기가 기록). 읽기 전용.", "User file / synth / missing (recorded by import). Read-only.") },
            { "col.preview", ("미리 듣기", "Preview") },
            { "col.preview.tip", ("에디터에서 변형 중 하나를 무작위로 재생(볼륨·음높이 설정은 반영하지 않음)", "Plays a random variant in the editor (volume and pitch settings are not applied)") },
            { "row.sfx", ("효과음 목록", "Sound library") },
            { "info.traits", ("Assets/Data/PassiveTraits/<동물>.asset — 동물을 흡수해 특성을 든 동안 항상 붙는 보너스(공격력·이동속도). 흡수 스킬 수치는 '동물 스킬' 탭.", "Assets/Data/PassiveTraits/<animal>.asset — always-on bonuses while holding the animal's trait (attack, move speed). Absorb skill values are on the 'Animal skills' tab.") },
            { "info.monsters", ("Assets/Data/Monsters/<동물>.asset — 잡몹 공통 수치(정예도 같은 에셋 + 정예 배율)", "Assets/Data/Monsters/<animal>.asset — common mob values (elites use the same asset + elite multipliers)") },
            { "info.patterns", ("행동 하위 config의 고유 수치만(공통 수치는 '잡몹' 탭). 고유 수치가 없는 동물은 빠진다.", "Only the behavior-specific values (common values are in the 'Monsters' tab). Animals without their own values are skipped.") },
            { "info.elite", ("Assets/Data/Monsters/EliteConfig.asset — 12종 정예 공용 배율", "Assets/Data/Monsters/EliteConfig.asset — elite multipliers shared by all 12") },
            { "info.status", ("Assets/Data/Combat/StatusEffectConfig.asset — 플레이어가 잡몹·보스에 거는 속성 효과(공용)", "Assets/Data/Combat/StatusEffectConfig.asset — Element effects the player applies to mobs and bosses (shared)") },
            { "info.difficulty", ("Assets/Data/Dungeon/DifficultyConfig.asset — 몬스터 전체·층·인원 배율", "Assets/Data/Dungeon/DifficultyConfig.asset — global, floor and party multipliers for monsters") },
            { "info.skills", ("Assets/Data/AbsorbSkills/<동물>SkillConfig.asset — 흡수 스킬 수치", "Assets/Data/AbsorbSkills/<animal>SkillConfig.asset — absorb skill values") },
            { "boss.stats", ("몸 수치 (BossStatsConfig — Assets/Data/Boss/<보스>_Stats.asset)", "Body stats (BossStatsConfig — Assets/Data/Boss/<boss>_Stats.asset)") },
            { "boss.orphans", ("어느 세트에도 없는 패턴", "Patterns not in any set") },
            { "corner.asset", ("에셋", "Asset") },
            { "corner.entry", ("항목", "Entry") },
            { "corner.floor", ("층", "Floor") },
            { "corner.array", ("배열", "Array") },
            { "preview.floor", ("미리보기 층", "Preview floor") },
            { "preview.party", ("인원", "Party") },
            { "preview.elite", ("정예", "Elite") },
            { "preview.noDifficulty", ("DifficultyConfig 에셋이 없어 미리보기 배율은 1로 계산합니다.", "No DifficultyConfig asset — preview multipliers are 1.") },
            { "col.health", ("실제 체력", "Actual health") },
            { "col.health.tip", ("층 × 인원(× 정예) 배율 적용 결과 — DifficultyScaling과 같은 식. 읽기 전용.", "Result after floor × party (× elite) multipliers — same formula as DifficultyScaling. Read-only.") },
            { "col.damage", ("실제 공격", "Actual damage") },
            { "col.damage.tip", ("층 대미지 배율(× 정예 공격 배율) 적용 결과 — DifficultyScaling과 같은 식. 읽기 전용.", "Result after floor damage × (× elite damage) — same formula as DifficultyScaling. Read-only.") },
            { "diff.global", ("전체 배율 (이동속도·공격 쿨·예고 시간)", "Global multipliers (move speed, attack cooldown, telegraph time)") },
            { "diff.arrays", ("층·인원 배열 (칸 = 층 또는 인원, 배열이 짧으면 마지막 값을 씀)", "Floor / party arrays (slot = floor or party size; a short array repeats its last value)") },
            { "diff.preview", ("미리보기 (읽기 전용 — 실제 곱 결과)", "Preview (read-only — actual products)") },
            { "diff.mob1", ("잡몹 체력 배율 (1인)", "Mob health × (1P)") },
            { "diff.mob2", ("잡몹 체력 배율 (2인)", "Mob health × (2P)") },
            { "diff.boss", ("보스 체력 배율", "Boss health ×") },
            { "diff.boss.tip", ("보스는 인원 배율 제외(인원별 몫 규칙)", "Bosses skip the party multiplier (per-player quota rule)") },
            { "diff.damage", ("대미지 배율", "Damage ×") },
            { "diff.floorN", ("{0}층", "Floor {0}") },
            { "diff.slot.tip", ("층 배율 줄 = 층 번호, 인원 배율 줄 = 인원 수", "Floor rows = floor number, party row = party size") },
            { "diff.slots", ("칸 수", "Slots") },
            { "diff.slots.tip", ("배열이 층/인원 수보다 짧으면 마지막 값을 쓴다", "If the array is shorter than the floor/party count, the last value is used") },
            { "diff.slotsNote", ("{0}칸", "{0} slots") },
            { "diff.slotsNoteExpected", ("{0}칸(기대 {1})", "{0} slots (expected {1})") },
            { "unit.floor", ("층 {0}", "Floor {0}") },
            { "unit.party", ("인원 {0}", "Party {0}") },
            { "warn.playing", ("플레이 중 고친 값은 에셋에 그대로 남습니다(ScriptableObject는 플레이가 끝나도 되돌아가지 않음).", "Values changed during Play stay in the asset (ScriptableObjects are not reverted when Play ends).") },
            { "msg.noAsset", ("에셋이 없습니다.", "No asset found.") },
            { "msg.noAssetOf", ("{0} 에셋이 없습니다.", "No {0} asset found.") },
            { "msg.noValues", ("(표시할 수치 없음)", "(no values to show)") },
            { "cell.missingField", ("이 에셋에는 없는 필드", "This asset has no such field") },
            { "cell.array", ("배열 {0}칸…", "Array ({0})…") },
            { "cell.inspector", ("인스펙터…", "Inspector…") },
            { "cell.inspector.tip", ("칸에서 고칠 수 없는 값 — 누르면 인스펙터에서 에셋을 연다", "Not editable in a cell — click to open the asset in the Inspector") },
            { "row.pingTip", ("에셋: {0} — 누르면 프로젝트 창에서 에셋을 보여 줌", "Asset: {0} — click to ping it in the Project window") },
            { "row.skill", ("{0} — 흡수 스킬", "{0} — absorb skill") },
            { "row.bossStats", ("보스({0}) 몸 수치", "Boss ({0}) body stats") },
            { "row.bossSet", ("보스({0}) 패턴 세트", "Boss ({0}) pattern set") },
            { "row.bossPattern", ("보스({0}) 패턴 {1} {2}", "Boss ({0}) pattern {1} {2}") },
            { "row.elite", ("정예 (12종 공용)", "Elite (shared by 12)") },
            { "row.difficulty", ("난이도 배율", "Difficulty multipliers") },
            { "row.status", ("상태이상", "Status effects") },
            { "missing.box", ("이름 사전에 없는 수치 {0}개 — Assets/Editor/LevelDesignSheetFields.tsv에 추가", "{0} values missing from the name dictionary — add them to Assets/Editor/LevelDesignSheetFields.tsv") },
            { "missing.list", ("누락 목록 보기", "Show missing list") },
        };

        private static string U(string id)
        {
            return UiText.TryGetValue(id, out (string ko, string en) text) ? L(text.ko, text.en) : id;
        }

        private static string U(string id, params object[] args) => string.Format(U(id), args);

        // ── 행 표시 이름 ─────────────────────────────────────
        // 에셋 이름 대신 사람이 읽는 이름. 동물 이름은 strings.tsv의 trait.<id>.name 한/영 값과 같게 맞춘다.

        private static readonly Dictionary<string, (string ko, string en)> AnimalNames = new Dictionary<string, (string ko, string en)>
        {
            { "Ox", ("소", "Ox") }, { "Tiger", ("호랑이", "Tiger") }, { "Snake", ("뱀", "Snake") }, { "Horse", ("말", "Horse") },
            { "Pig", ("돼지", "Pig") }, { "Rooster", ("닭", "Rooster") }, { "Rabbit", ("토끼", "Rabbit") }, { "Rat", ("쥐", "Rat") },
            { "Dragon", ("용", "Dragon") }, { "Sheep", ("양", "Sheep") }, { "Monkey", ("원숭이", "Monkey") }, { "Dog", ("개", "Dog") },
        };

        // 보스 에셋 이름 접두어(<보스>_…) → 보스 이름.
        private static readonly Dictionary<string, (string ko, string en)> BossNames = new Dictionary<string, (string ko, string en)>
        {
            { "Ogre", ("오우거", "Ogre") },
        };

        // 보스 패턴 타입 → 표시 이름(한국어는 각 패턴의 DisplayName과 같게).
        private static readonly Dictionary<Type, (string ko, string en)> PatternNames = new Dictionary<Type, (string ko, string en)>
        {
            { typeof(BossPatternSwing), ("뿔 휘두르기", "Horn Swing") },
            { typeof(BossPatternCharge), ("강화 박치기 돌진", "Power Headbutt Charge") },
            { typeof(BossPatternRush), ("연속 돌진", "Chain Charge") },
            { typeof(BossPatternQuake), ("지진 강타", "Quake Slam") },
            { typeof(BossPatternRoar), ("포효 대지분쇄", "Roar Ground Smash") },
        };

        private static string AnimalName(string key, string assetName)
        {
            if (AnimalNames.TryGetValue(key, out (string ko, string en) name))
            {
                return L(name.ko, name.en);
            }

            Missing.Add("row: " + assetName);
            return assetName;
        }

        private static string BossName(UnityEngine.Object asset)
        {
            int underscore = asset.name.IndexOf('_');
            string key = underscore > 0 ? asset.name.Substring(0, underscore) : asset.name;
            if (BossNames.TryGetValue(key, out (string ko, string en) name))
            {
                return L(name.ko, name.en);
            }

            Missing.Add("row: " + asset.name);
            return key;
        }

        /// <summary>표 줄·접기 묶음 제목에 쓰는 사람이 읽는 이름. 매핑이 없으면 에셋 이름 그대로 + 누락 목록에 추가.</summary>
        private static string RowName(UnityEngine.Object asset)
        {
            switch (asset)
            {
                case MonsterStatsConfig _:
                    return AnimalName(asset.name, asset.name);
                case AbsorbSkillConfig _:
                    string animal = asset.name.EndsWith("SkillConfig", StringComparison.Ordinal) ? asset.name.Substring(0, asset.name.Length - "SkillConfig".Length) : asset.name;
                    return U("row.skill", AnimalName(animal, asset.name));
                case PassiveTraitData _:
                    // 특성 에셋 이름은 한국어 동물 이름(호랑이.asset) — 동물 이름 표에서 한국어로 찾아 언어에 맞게 보여 준다.
                    foreach ((string ko, string en) traitAnimal in AnimalNames.Values)
                    {
                        if (traitAnimal.ko == asset.name)
                        {
                            return L(traitAnimal.ko, traitAnimal.en);
                        }
                    }

                    Missing.Add("row: " + asset.name);
                    return asset.name;
                case EliteConfig _:
                    return U("row.elite");
                case DifficultyConfig _:
                    return U("row.difficulty");
                case StatusEffectConfig _:
                    return U("row.status");
                case BossStatsConfig _:
                    return U("row.bossStats", BossName(asset));
                case BossPatternSet _:
                    return U("row.bossSet", BossName(asset));
                case SfxLibrary _:
                    return U("row.sfx");
                case BossPatternBase pattern:
                    if (!PatternNames.TryGetValue(pattern.GetType(), out (string ko, string en) patternName))
                    {
                        Missing.Add("row: " + asset.name);
                        return asset.name;
                    }

                    return U("row.bossPattern", BossName(asset), pattern.PatternId, L(patternName.ko, patternName.en));
                default:
                    Missing.Add("row: " + asset.name);
                    return asset.name;
            }
        }

        // ── 이름 사전 (Assets/Editor/LevelDesignSheetFields.tsv) ──────────
        // 열: key, ko_name, en_name, ko_desc, en_desc. key = 타입이름.필드이름(중첩 타입은 바깥.안쪽.필드) 또는 공용 *.필드이름.
        // 조회 순서: 타입.필드 → 부모 타입들 → *.필드 → 없으면 Unity displayName(+ 누락 목록). 파일 수정 시간이 바뀌면 다시 읽는다.

        public const string FieldsPath = "Assets/Editor/LevelDesignSheetFields.tsv";

        private sealed class FieldText
        {
            public string KoName, EnName, KoDesc, EnDesc;
            public string Name => L(KoName, string.IsNullOrEmpty(EnName) ? KoName : EnName);
            public string Desc => L(KoDesc, string.IsNullOrEmpty(EnDesc) ? KoDesc : EnDesc);
        }

        private static Dictionary<string, FieldText> _fieldTexts;
        private static DateTime _fieldsStamp;
        private static readonly Dictionary<(Type, string), FieldText> FieldCache = new Dictionary<(Type, string), FieldText>();
        private static readonly SortedSet<string> Missing = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>그리는 동안 사전에 없던 수치(타입.필드)·행 이름(row: 에셋) 목록(검증용).</summary>
        public static IReadOnlyCollection<string> MissingNames => Missing;

        /// <summary>사전의 모든 키(검증용).</summary>
        public static IReadOnlyCollection<string> DictionaryKeys
        {
            get
            {
                EnsureFieldTexts();
                return _fieldTexts.Keys;
            }
        }

        private static void EnsureFieldTexts()
        {
            DateTime stamp = File.Exists(FieldsPath) ? File.GetLastWriteTimeUtc(FieldsPath) : DateTime.MinValue;
            if (_fieldTexts != null && stamp == _fieldsStamp)
            {
                return;
            }

            _fieldsStamp = stamp;
            _fieldTexts = new Dictionary<string, FieldText>(StringComparer.Ordinal);
            FieldCache.Clear();
            Missing.Clear();
            if (stamp == DateTime.MinValue)
            {
                Debug.LogWarning($"[LevelDesignSheet] 이름 사전 없음: {FieldsPath}");
                return;
            }

            foreach (string raw in File.ReadAllLines(FieldsPath))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("key\t", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] cols = line.Split('\t');
                if (cols.Length < 2 || string.IsNullOrWhiteSpace(cols[0]))
                {
                    continue;
                }

                string Col(int i) => i < cols.Length ? cols[i].Trim().Replace("\\n", "\n") : string.Empty;
                _fieldTexts[cols[0].Trim()] = new FieldText { KoName = Col(1), EnName = Col(2), KoDesc = Col(3), EnDesc = Col(4) };
            }
        }

        private static string TypeKey(Type type) => type.IsNested ? TypeKey(type.DeclaringType) + "." + type.Name : type.Name;

        private static FieldText FindField(Type type, string field)
        {
            EnsureFieldTexts();
            if (FieldCache.TryGetValue((type, field), out FieldText cached))
            {
                return cached;
            }

            FieldText result = null;
            for (Type t = type; t != null && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
            {
                if (_fieldTexts.TryGetValue(TypeKey(t) + "." + field, out result))
                {
                    break;
                }
            }

            if (result == null && !_fieldTexts.TryGetValue("*." + field, out result))
            {
                Missing.Add(TypeKey(type) + "." + field);
            }

            FieldCache[(type, field)] = result;
            return result;
        }

        private static string NameOf(Type type, string field, string fallback)
        {
            FieldText text = FindField(type, field);
            return text != null && !string.IsNullOrEmpty(text.KoName) ? text.Name : fallback;
        }

        private static string HeaderOf(Type type, SerializedProperty prop) => NameOf(type, prop.name, prop.displayName);

        private static readonly Dictionary<(Type, string), string> AttributeTooltipCache = new Dictionary<(Type, string), string>();

        // 칸·열 머리 툴팁 = 사전 설명 + (필드 이름). 사전에 없으면 필드의 [Tooltip](상속 계층을 올라가며 찾음), 그것도 없으면 필드 이름.
        private static string TooltipOf(Type type, string field)
        {
            FieldText text = FindField(type, field);
            if (text != null && !string.IsNullOrEmpty(text.Desc))
            {
                return $"{text.Desc}\n({field})";
            }

            if (AttributeTooltipCache.TryGetValue((type, field), out string cached))
            {
                return cached;
            }

            string result = field;
            for (Type t = type; t != null && t != typeof(ScriptableObject); t = t.BaseType)
            {
                FieldInfo info = t.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (info == null)
                {
                    continue;
                }

                var tooltip = info.GetCustomAttribute<TooltipAttribute>();
                result = tooltip != null ? $"{field}: {tooltip.tooltip}" : field;
                break;
            }

            AttributeTooltipCache[(type, field)] = result;
            return result;
        }

        // ── 창 상태 ──────────────────────────────────────────

        private int _tab;
        private Vector2 _scroll;
        private bool _showMissing;
        private int _missingShown;
        private readonly Dictionary<string, Vector2> _hScroll = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        // 미리보기(잡몹 탭)
        private int _previewFloor = 1;
        private int _previewParty = 1;
        private bool _previewElite;

        // 탭별 에셋(새로 고침 때 다시 찾는다)
        private List<SerializedObject> _monsters = new List<SerializedObject>();
        private SerializedObject _elite;
        private List<SerializedObject> _bossStats = new List<SerializedObject>();
        private List<SerializedObject> _patternSets = new List<SerializedObject>();
        private List<SerializedObject> _bossPatterns = new List<SerializedObject>();
        private SerializedObject _status;
        private SerializedObject _difficulty;
        private List<SerializedObject> _skills = new List<SerializedObject>();
        private List<SerializedObject> _traits = new List<SerializedObject>();
        private SerializedObject _sfx;

        private static HashSet<string> _baseMonsterFields;

        [MenuItem("Tools/Absorb/레벨 디자인 시트")]
        public static void Open()
        {
            GetWindow<LevelDesignSheetWindow>(U("window.title"));
        }

        private void OnEnable() => Reload();
        private void OnFocus() => Reload();
        private void OnProjectChange()
        {
            Reload();
            Repaint();
        }

        /// <summary>탭별 줄 수(검증 로그용). 키 = 탭 이름.</summary>
        public Dictionary<string, int> RowCounts()
        {
            Reload();
            return new Dictionary<string, int>
            {
                { "잡몹", _monsters.Count },
                { "동물별 패턴", _monsters.Count(so => so.targetObject.GetType() != typeof(MonsterStatsConfig)) },
                { "정예", _elite != null ? 1 : 0 },
                { "보스 몸", _bossStats.Count },
                { "보스 패턴 세트", _patternSets.Count },
                { "보스 패턴", _bossPatterns.Count },
                { "상태이상", _status != null ? 1 : 0 },
                { "난이도", _difficulty != null ? 1 : 0 },
                { "동물 스킬", _skills.Count },
                { "패시브 특성", _traits.Count },
                { "사운드", _sfx != null ? ((SfxLibrary)_sfx.targetObject).entries.Count : 0 },
            };
        }

        /// <summary>탭을 골라 다시 그린다(검증용).</summary>
        public void SelectTab(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, TabCount - 1);
            Repaint();
        }

        /// <summary>지금까지 그려진 접기 묶음을 모두 편다(검증용 — 다음 그리기에서 하위 표까지 그려지게).</summary>
        public void ExpandAllFoldouts()
        {
            foreach (string key in _foldouts.Keys.ToList())
            {
                _foldouts[key] = true;
            }

            Repaint();
        }

        private void Reload()
        {
            _monsters = Load<MonsterStatsConfig>();
            _elite = Load<EliteConfig>().FirstOrDefault();
            _bossStats = Load<BossStatsConfig>();
            _patternSets = Load<BossPatternSet>();
            _bossPatterns = Load<BossPatternBase>();
            _status = Load<StatusEffectConfig>().FirstOrDefault();
            _difficulty = Load<DifficultyConfig>().FirstOrDefault();
            _skills = Load<AbsorbSkillConfig>();
            _traits = Load<PassiveTraitData>();
            _sfx = Load<SfxLibrary>().FirstOrDefault();
        }

        private static List<SerializedObject> Load<T>() where T : ScriptableObject
        {
            return AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null)
                .OrderBy(asset => asset.name, StringComparer.Ordinal)
                .Select(asset => new SerializedObject(asset))
                .ToList();
        }

        private void OnGUI()
        {
            EnsureFieldTexts();
            string title = U("window.title");
            if (titleContent.text != title)
            {
                titleContent = new GUIContent(title);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string[] tabNames = Enumerable.Range(0, TabCount).Select(i => U("tab." + i)).ToArray();
                _tab = GUILayout.Toolbar(_tab, tabNames);
                GUILayout.Space(8);
                int lang = GUILayout.Toolbar(English ? 1 : 0, new[] { "한국어", "English" }, GUILayout.Width(140));
                if (lang != (English ? 1 : 0))
                {
                    English = lang == 1;
                    Repaint();
                }
            }

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(U("warn.playing"), MessageType.Warning);
            }

            // 누락 수는 Layout 때만 다시 읽는다 — 그리는 도중 늘어나도 같은 프레임의 Layout/Repaint 컨트롤 수가 달라지지 않게.
            if (Event.current.type == EventType.Layout)
            {
                _missingShown = Missing.Count;
            }

            if (_missingShown > 0)
            {
                EditorGUILayout.HelpBox(U("missing.box", _missingShown), MessageType.Warning);
                _showMissing = EditorGUILayout.Foldout(_showMissing, U("missing.list"), true);
                if (_showMissing)
                {
                    EditorGUILayout.SelectableLabel(string.Join("\n", Missing), EditorStyles.textArea,
                        GUILayout.Height(Mathf.Min(10, _missingShown) * EditorGUIUtility.singleLineHeight + 6f));
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: DrawMonsterTab(); break;
                case 1: DrawMonsterPatternTab(); break;
                case 2: DrawEliteTab(); break;
                case 3: DrawBossTab(); break;
                case 4: DrawStatusTab(); break;
                case 5: DrawDifficultyTab(); break;
                case 6: DrawSkillTab(); break;
                case 7: DrawTraitTab(); break;
                case 8: DrawSoundTab(); break;
            }

            EditorGUILayout.EndScrollView();

            // 이번 그리기에서 새 누락이 생겼으면 위쪽 안내를 바로 갱신한다.
            if (Missing.Count != _missingShown)
            {
                Repaint();
            }
        }

        // ── 탭 ① 잡몹 ─────────────────────────────────────────

        private void DrawMonsterTab()
        {
            Info(U("info.monsters"));
            using (new EditorGUILayout.HorizontalScope())
            {
                float oldLabel = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 90f;
                _previewFloor = EditorGUILayout.IntPopup(U("preview.floor"), _previewFloor, new[] { "1", "2", "3", "4", "5" }, new[] { 1, 2, 3, 4, 5 }, GUILayout.Width(170));
                GUILayout.Space(12);
                EditorGUIUtility.labelWidth = 50f;
                _previewParty = EditorGUILayout.IntPopup(U("preview.party"), _previewParty, new[] { "1", "2" }, new[] { 1, 2 }, GUILayout.Width(120));
                GUILayout.Space(12);
                _previewElite = EditorGUILayout.ToggleLeft(U("preview.elite"), _previewElite, GUILayout.Width(80));
                EditorGUIUtility.labelWidth = oldLabel;
                GUILayout.FlexibleSpace();
            }

            if (_difficulty == null)
            {
                EditorGUILayout.HelpBox(U("preview.noDifficulty"), MessageType.Info);
            }

            List<Column> columns = PropertyColumns(_monsters, onlyBase: true);
            InsertAfter(columns, "maxHealth", Column.Computed(U("col.health"), U("col.health.tip"), PreviewHealth));
            InsertAfter(columns, "attackDamage", Column.Computed(U("col.damage"), U("col.damage.tip"), PreviewDamage));
            DrawTable("monsters", _monsters, columns);
        }

        private string PreviewHealth(SerializedObject so)
        {
            int health = so.FindProperty("maxHealth").intValue;
            if (_previewElite)
            {
                health = Mathf.RoundToInt(health * EliteFloat("healthMultiplier")); // MonsterStub.ApplyEliteHealthMultiplier와 같은 반올림
            }

            float scale = DifficultyScaling.HealthScale(_difficulty != null ? (DifficultyConfig)_difficulty.targetObject : null, _previewFloor, _previewParty, includeParty: true);
            return DifficultyScaling.ScaleHealth(health, scale).ToString();
        }

        private string PreviewDamage(SerializedObject so)
        {
            int damage = so.FindProperty("attackDamage").intValue;
            if (_previewElite)
            {
                damage = Mathf.RoundToInt(damage * EliteFloat("attackDamageMultiplier")); // MonsterBehaviorController.ApplyEliteDamageMultiplier와 같은 반올림
            }

            var config = _difficulty != null ? (DifficultyConfig)_difficulty.targetObject : null;
            return DifficultyScaling.ScaleDamage(damage, config != null ? config.FloorDamageMultiplier(_previewFloor) : 1f).ToString();
        }

        private float EliteFloat(string field)
        {
            SerializedProperty prop = _elite != null ? _elite.FindProperty(field) : null;
            return prop != null ? prop.floatValue : 1f;
        }

        // ── 탭 ② 동물별 패턴 ──────────────────────────────────

        private void DrawMonsterPatternTab()
        {
            Info(U("info.patterns"));
            foreach (SerializedObject so in _monsters)
            {
                if (so.targetObject.GetType() == typeof(MonsterStatsConfig))
                {
                    continue;
                }

                var rows = new List<SerializedObject> { so };
                List<Column> columns = PropertyColumns(rows, onlyBase: false, excludeBase: true);
                if (columns.Count == 0)
                {
                    continue;
                }

                // 묶음 제목은 사람이 읽는 이름만(코드 타입 이름은 섞지 않는다 — 2026-10-14 언어 통일).
                if (Foldout("pattern/" + so.targetObject.name, RowName(so.targetObject)))
                {
                    DrawTable("pattern/" + so.targetObject.name, rows, columns);
                }
            }
        }

        // ── 탭 ③ 정예 ────────────────────────────────────────

        private void DrawEliteTab()
        {
            Info(U("info.elite"));
            if (_elite == null)
            {
                EditorGUILayout.HelpBox(U("msg.noAssetOf", "EliteConfig"), MessageType.Info);
                return;
            }

            var rows = new List<SerializedObject> { _elite };
            DrawTable("elite", rows, PropertyColumns(rows, onlyBase: false));
        }

        // ── 탭 ④ 보스 ────────────────────────────────────────

        private void DrawBossTab()
        {
            EditorGUILayout.LabelField(U("boss.stats"), EditorStyles.boldLabel);
            DrawTable("boss/stats", _bossStats, PropertyColumns(_bossStats, onlyBase: false));

            var referenced = new HashSet<UnityEngine.Object>();
            foreach (SerializedObject set in _patternSets)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField(new GUIContent(RowName(set.targetObject), U("row.pingTip", set.targetObject.name)), EditorStyles.boldLabel);
                set.Update();
                SerializedProperty entries = set.FindProperty("patterns");
                if (entries != null && entries.isArray)
                {
                    DrawEntryTable("boss/set/" + set.targetObject.name, set, entries);
                    for (int i = 0; i < entries.arraySize; i++)
                    {
                        UnityEngine.Object pattern = entries.GetArrayElementAtIndex(i).FindPropertyRelative("pattern").objectReferenceValue;
                        if (pattern != null)
                        {
                            referenced.Add(pattern);
                        }
                    }
                }

                var setRow = new List<SerializedObject> { set };
                DrawTable("boss/setfields/" + set.targetObject.name, setRow, PropertyColumns(setRow, onlyBase: false));

                foreach (SerializedObject pattern in _bossPatterns.Where(p => referenced.Contains(p.targetObject) && IsInSet(set, p.targetObject)))
                {
                    DrawPatternAsset(pattern);
                }
            }

            List<SerializedObject> orphans = _bossPatterns.Where(p => !referenced.Contains(p.targetObject)).ToList();
            if (orphans.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField(U("boss.orphans"), EditorStyles.boldLabel);
                foreach (SerializedObject pattern in orphans)
                {
                    DrawPatternAsset(pattern);
                }
            }
        }

        private static bool IsInSet(SerializedObject set, UnityEngine.Object pattern)
        {
            SerializedProperty entries = set.FindProperty("patterns");
            for (int i = 0; entries != null && i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("pattern").objectReferenceValue == pattern)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawPatternAsset(SerializedObject pattern)
        {
            if (Foldout("boss/pattern/" + pattern.targetObject.name, RowName(pattern.targetObject)))
            {
                var rows = new List<SerializedObject> { pattern };
                DrawTable("boss/pattern/" + pattern.targetObject.name, rows, PropertyColumns(rows, onlyBase: false));
            }
        }

        // 세트 항목 배열: 줄 = 항목 번호, 열 = Entry 필드.
        private void DrawEntryTable(string key, SerializedObject set, SerializedProperty entries)
        {
            string[] fields = { "pattern", "unlockPhase", "firstCooldown", "applyEnrageMultiplier" };
            float[] widths = { 170f, 110f, 150f, 130f };
            Type entryType = typeof(BossPatternSet.Entry);

            var names = new List<GUIContent>();
            for (int i = 0; i < entries.arraySize; i++)
            {
                names.Add(new GUIContent($"#{i + 1}"));
            }

            List<GUIContent> headers = fields.Select(f => new GUIContent(NameOf(entryType, f, f), TooltipOf(entryType, f))).ToList();
            DrawGrid(key, U("corner.entry"), names, null, headers, widths, (rect, row, col) =>
            {
                SerializedProperty prop = entries.GetArrayElementAtIndex(row).FindPropertyRelative(fields[col]);
                EditorGUI.BeginChangeCheck();
                if (!ValueField(rect, prop))
                {
                    InspectorButton(rect, set.targetObject, headers[col].tooltip);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    Save(set);
                }

                CellTooltip(rect, headers[col].tooltip);
            });
        }

        // ── 탭 ⑤ 상태이상 ────────────────────────────────────

        private void DrawStatusTab()
        {
            Info(U("info.status"));
            if (_status == null)
            {
                EditorGUILayout.HelpBox(U("msg.noAssetOf", "StatusEffectConfig"), MessageType.Info);
                return;
            }

            DrawVerticalTwoColumns(_status);
        }

        /// <summary>
        /// 필드가 많은 한 줄짜리 에셋: 이름 | 값을 좌우 2열로 나눠 세로로 보여 준다.
        /// 예전엔 EditorGUILayout.PropertyField라 [Header]가 라벨 위에 끼어 줄 높이가 들쭉날쭉했다(2026-10-14) —
        /// 지금은 줄마다 RowHeight 고정 칸에 이름 라벨 + ValueField(배열은 칸 안에 원소를 나란히)를 직접 그린다.
        /// </summary>
        private void DrawVerticalTwoColumns(SerializedObject so)
        {
            so.Update();
            List<SerializedProperty> props = TopLevelProperties(so).ToList();
            if (props.Count == 0)
            {
                EditorGUILayout.LabelField(U("msg.noValues"), EditorStyles.miniLabel);
                return;
            }

            Type type = so.targetObject.GetType();
            float rowHeight = RowHeight;
            int half = (props.Count + 1) / 2;
            Rect area = GUILayoutUtility.GetRect(0f, half * rowHeight, GUILayout.ExpandWidth(true));
            area.xMin += 4f;
            area.xMax -= 4f;
            const float gap = 16f;
            float columnWidth = Mathf.Max(220f, (area.width - gap) * 0.5f);
            List<GUIContent> labels = props.Select(p => new GUIContent(HeaderOf(type, p), TooltipOf(type, p.name))).ToList();
            // 이름 칸 폭 = 가장 긴 이름에 맞춤(값 칸은 최소 90 남김).
            float labelWidth = Mathf.Min(columnWidth - 90f, labels.Max(l => NameStyle.CalcSize(l).x) + 16f);

            if (Event.current.type == EventType.Repaint)
            {
                for (int r = 1; r < half; r += 2)
                {
                    EditorGUI.DrawRect(new Rect(area.x, area.y + r * rowHeight, area.width, rowHeight), StripeColor);
                }
            }

            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < props.Count; i++)
            {
                SerializedProperty prop = props[i];
                float x = area.x + (i / half) * (columnWidth + gap);
                float y = area.y + (i % half) * rowHeight;
                GUI.Label(new Rect(x + 4f, y, labelWidth - 8f, rowHeight), labels[i], NameStyle);
                Rect valueRect = new Rect(x + labelWidth, y + (rowHeight - EditorGUIUtility.singleLineHeight) * 0.5f, columnWidth - labelWidth, EditorGUIUtility.singleLineHeight);
                if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
                {
                    InlineArray(valueRect, prop, so.targetObject, labels[i].tooltip);
                }
                else
                {
                    if (!ValueField(valueRect, prop))
                    {
                        InspectorButton(valueRect, so.targetObject, labels[i].tooltip);
                    }

                    CellTooltip(valueRect, labels[i].tooltip);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                Save(so);
            }

            EditorGUILayout.Space(4);
        }

        // 짧은 배열(냉기 스택별 배율 등)을 한 칸 안에 원소 입력란 + [+][-]로 그린다. 원소가 많으면 인스펙터로 넘긴다.
        private static void InlineArray(Rect rect, SerializedProperty array, UnityEngine.Object target, string tooltip)
        {
            const float buttonWidth = 22f;
            int count = array.arraySize;
            float fieldsWidth = rect.width - buttonWidth * 2f - 4f;
            if (count > 8 || (count > 0 && fieldsWidth / count < 32f))
            {
                InspectorButton(rect, target, tooltip, U("cell.array", count));
                return;
            }

            float width = count > 0 ? fieldsWidth / count : 0f;
            for (int i = 0; i < count; i++)
            {
                Rect element = new Rect(rect.x + i * width, rect.y, width - 2f, rect.height);
                if (!ValueField(element, array.GetArrayElementAtIndex(i)))
                {
                    InspectorButton(rect, target, tooltip, U("cell.array", count));
                    return;
                }

                CellTooltip(element, $"{tooltip}\n[{i + 1}]");
            }

            Rect plus = new Rect(rect.xMax - buttonWidth * 2f, rect.y, buttonWidth, rect.height);
            Rect minus = new Rect(rect.xMax - buttonWidth, rect.y, buttonWidth, rect.height);
            if (GUI.Button(plus, "+", EditorStyles.miniButtonLeft))
            {
                array.arraySize++;
                GUI.changed = true;
            }

            using (new EditorGUI.DisabledScope(count <= 1))
            {
                if (GUI.Button(minus, "-", EditorStyles.miniButtonRight))
                {
                    array.arraySize--;
                    GUI.changed = true;
                }
            }
        }

        // ── 탭 ⑥ 난이도 ──────────────────────────────────────

        private void DrawDifficultyTab()
        {
            Info(U("info.difficulty"));
            if (_difficulty == null)
            {
                EditorGUILayout.HelpBox(U("msg.noAssetOf", "DifficultyConfig"), MessageType.Info);
                return;
            }

            var rows = new List<SerializedObject> { _difficulty };
            EditorGUILayout.LabelField(U("diff.global"), EditorStyles.boldLabel);
            // 층·인원 배열은 바로 아래 표에서 칸별로 고치므로 이 표에서는 뺀다(예전엔 '배열 5칸…' 버튼으로 겹쳐 나와 헷갈렸다 — 2026-10-14 피드백).
            List<Column> globalColumns = PropertyColumns(rows, onlyBase: false)
                .Where(c => c.Property == null || !_difficulty.FindProperty(c.Property).isArray)
                .ToList();
            DrawTable("difficulty/global", rows, globalColumns);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(U("diff.arrays"), EditorStyles.boldLabel);
            _difficulty.Update();
            DrawArrayRows();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(U("diff.preview"), EditorStyles.boldLabel);
            var config = (DifficultyConfig)_difficulty.targetObject;
            var floors = new List<GUIContent>();
            for (int floor = 1; floor <= 5; floor++)
            {
                floors.Add(new GUIContent(U("diff.floorN", floor)));
            }

            var previewHeaders = new List<GUIContent>
            {
                new GUIContent(U("diff.mob1")), new GUIContent(U("diff.mob2")),
                new GUIContent(U("diff.boss"), U("diff.boss.tip")), new GUIContent(U("diff.damage")),
            };
            DrawGrid("difficulty/preview", U("corner.floor"), floors, null, previewHeaders, null, (rect, row, col) =>
            {
                int floor = row + 1;
                float value = col == 0 ? DifficultyScaling.HealthScale(config, floor, 1, true)
                    : col == 1 ? DifficultyScaling.HealthScale(config, floor, 2, true)
                    : col == 2 ? DifficultyScaling.HealthScale(config, floor, 1, false)
                    : config.FloorDamageMultiplier(floor);
                EditorGUI.LabelField(rect, value.ToString("0.###"), ValueStyle);
            });
        }

        // 층·인원 배열 3줄: 줄 = 배열, 열 = 칸 번호(층 또는 인원) + 마지막 열은 칸 수 조절 버튼.
        private void DrawArrayRows()
        {
            (string field, string unit, int expected)[] arrays =
            {
                ("floorHealthMultipliers", "unit.floor", 5),
                ("floorDamageMultipliers", "unit.floor", 5),
                ("partyHealthMultipliers", "unit.party", 4),
            };
            SerializedProperty[] props = arrays.Select(a => _difficulty.FindProperty(a.field)).ToArray();
            int slots = Mathf.Max(5, props.Where(p => p != null).Select(p => p.arraySize).DefaultIfEmpty(0).Max());

            Type type = typeof(DifficultyConfig);
            List<GUIContent> names = arrays.Select(a => new GUIContent(NameOf(type, a.field, a.field), TooltipOf(type, a.field))).ToList();
            var headers = new List<GUIContent>();
            for (int i = 1; i <= slots; i++)
            {
                headers.Add(new GUIContent($"{i}", U("diff.slot.tip")));
            }

            headers.Add(new GUIContent(U("diff.slots"), U("diff.slots.tip")));
            var widths = Enumerable.Repeat(MinCellWidth, slots).Concat(new[] { 170f }).ToList();

            DrawGrid("difficulty/arrays", U("corner.array"), names, null, headers, widths, (rect, row, col) =>
            {
                SerializedProperty array = props[row];
                if (array == null)
                {
                    return;
                }

                if (col < slots)
                {
                    if (col < array.arraySize)
                    {
                        EditorGUI.BeginChangeCheck();
                        ValueField(rect, array.GetArrayElementAtIndex(col));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Save(_difficulty);
                        }

                        CellTooltip(rect, U(arrays[row].unit, col + 1));
                    }
                    else
                    {
                        EditorGUI.LabelField(rect, "—", ValueStyle);
                    }

                    return;
                }

                // 칸 수 조절
                Rect plus = new Rect(rect.x, rect.y, 26f, rect.height);
                Rect minus = new Rect(rect.x + 30f, rect.y, 26f, rect.height);
                Rect note = new Rect(rect.x + 60f, rect.y, rect.width - 60f, rect.height);
                if (GUI.Button(plus, "+"))
                {
                    array.arraySize++;
                    Save(_difficulty);
                }

                if (array.arraySize > 1 && GUI.Button(minus, "-"))
                {
                    array.arraySize--;
                    Save(_difficulty);
                }

                EditorGUI.LabelField(note, array.arraySize == arrays[row].expected
                    ? U("diff.slotsNote", array.arraySize)
                    : U("diff.slotsNoteExpected", array.arraySize, arrays[row].expected));
            });
        }

        // ── 탭 ⑦ 동물 스킬 ───────────────────────────────────

        private void DrawSkillTab()
        {
            Info(U("info.skills"));
            foreach (SerializedObject so in _skills)
            {
                if (Foldout("skill/" + so.targetObject.name, RowName(so.targetObject)))
                {
                    var rows = new List<SerializedObject> { so };
                    DrawTable("skill/" + so.targetObject.name, rows, PropertyColumns(rows, onlyBase: false));
                }
            }
        }

        // ── 탭 ⑧ 패시브 특성 (2026-10-14) ────────────────────
        // 12종을 한 표로(줄 = 동물). 밸런스 수치(공격력 보너스·이동속도 배율)만 보여 준다 — 표시 이름·언어 키·연결된 스킬 종류는
        // 콘텐츠 설정이라 시트에서 고치지 않는다(바꾸면 흡수 스킬 연결이 깨질 수 있음, 인스펙터에서).
        private static readonly HashSet<string> TraitBalanceFields = new HashSet<string> { "attackDamageBonus", "moveSpeedMultiplier" };

        private void DrawTraitTab()
        {
            Info(U("info.traits"));
            List<Column> columns = PropertyColumns(_traits, onlyBase: false)
                .Where(c => c.Property != null && TraitBalanceFields.Contains(c.Property))
                .ToList();
            DrawTable("traits", _traits, columns);
        }

        // ── 탭 ⑨ 사운드 (2026-10-14) ─────────────────────────
        // 줄 = SfxLibrary 항목(효과음 id), 이름·툴팁 = 이름 사전 sfx.<id>(ko/en 이름, 언제 나는 소리).
        // 열 = 항목 필드(볼륨·음높이 폭·최소 간격·최대 동시 수·화면 전체) + 변형 수·출처(읽기 전용) + 미리 듣기.
        private static readonly string[] SoundFields = { "volume", "pitchJitter", "minInterval", "maxConcurrent", "is2D" };

        private void DrawSoundTab()
        {
            Info(U("info.sound"));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(U("sound.import"), U("sound.import.tip")), GUILayout.Width(160)))
                {
                    Debug.Log(SfxImporter.ImportAll());
                    Reload();
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(8);
                EditorGUILayout.LabelField(U("sound.replace"), InfoStyle);
            }

            if (_sfx == null)
            {
                EditorGUILayout.HelpBox(U("sound.noLibrary"), MessageType.Info);
                return;
            }

            _sfx.Update();
            SerializedProperty entries = _sfx.FindProperty("entries");
            if (entries == null || entries.arraySize == 0)
            {
                EditorGUILayout.HelpBox(U("msg.noValues"), MessageType.Info);
                return;
            }

            Type entryType = typeof(SfxLibrary.Entry);
            var names = new List<GUIContent>();
            for (int i = 0; i < entries.arraySize; i++)
            {
                string id = entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                names.Add(SoundRowName(id));
            }

            List<GUIContent> headers = SoundFields.Select(f => new GUIContent(NameOf(entryType, f, f), TooltipOf(entryType, f))).ToList();
            headers.Add(new GUIContent(U("col.variants"), U("col.variants.tip")));
            headers.Add(new GUIContent(U("col.source"), U("col.source.tip")));
            headers.Add(new GUIContent(U("col.preview"), U("col.preview.tip")));
            int fieldCount = SoundFields.Length;

            DrawGrid("sound", U("corner.sound"), names, null, headers, null, (rect, row, col) =>
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(row);
                if (col < fieldCount)
                {
                    SerializedProperty prop = entry.FindPropertyRelative(SoundFields[col]);
                    EditorGUI.BeginChangeCheck();
                    if (prop == null || !ValueField(rect, prop))
                    {
                        InspectorButton(rect, _sfx.targetObject, headers[col].tooltip);
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        Save(_sfx);
                    }

                    CellTooltip(rect, headers[col].tooltip);
                    return;
                }

                SerializedProperty clips = entry.FindPropertyRelative("clips");
                if (col == fieldCount)
                {
                    EditorGUI.LabelField(rect, new GUIContent(clips.arraySize.ToString(), headers[col].tooltip), ValueStyle);
                }
                else if (col == fieldCount + 1)
                {
                    SerializedProperty source = entry.FindPropertyRelative("source");
                    string sourceName = source.enumValueIndex >= 0 && source.enumValueIndex < source.enumNames.Length ? source.enumNames[source.enumValueIndex] : "?";
                    EditorGUI.LabelField(rect, new GUIContent(SourceName(sourceName), headers[col].tooltip), ValueStyle);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(clips.arraySize == 0))
                    {
                        if (GUI.Button(rect, new GUIContent("▶", headers[col].tooltip), EditorStyles.miniButton))
                        {
                            var clip = clips.GetArrayElementAtIndex(UnityEngine.Random.Range(0, clips.arraySize)).objectReferenceValue as AudioClip;
                            PreviewClip(clip);
                        }
                    }
                }
            });
        }

        // 효과음 줄 이름 = 사전 sfx.<id>의 이름, 툴팁 = 설명(언제 나는 소리) + id. 없으면 id 그대로 + 누락 목록.
        private static GUIContent SoundRowName(string id)
        {
            EnsureFieldTexts();
            if (_fieldTexts.TryGetValue("sfx." + id, out FieldText text) && !string.IsNullOrEmpty(text.KoName))
            {
                return new GUIContent(text.Name, string.IsNullOrEmpty(text.Desc) ? id : $"{text.Desc}\n({id})");
            }

            Missing.Add("sfx." + id);
            return new GUIContent(id, id);
        }

        // 출처 표시 이름: 사전 enum.SfxLibrary.ClipSource.<값>, 없으면 값 이름.
        private static string SourceName(string value)
        {
            EnsureFieldTexts();
            return _fieldTexts.TryGetValue("enum.SfxLibrary.ClipSource." + value, out FieldText text) ? text.Name : value;
        }

        // 에디터 미리 듣기: 내부 API UnityEditor.AudioUtil.PlayPreviewClip(clip, 0, false)을 리플렉션으로 부른다(공개 API 없음).
        private static MethodInfo _playPreview, _stopPreviews;

        private static void PreviewClip(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (_playPreview == null)
            {
                Type audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
                _playPreview = audioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
                _stopPreviews = audioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public);
            }

            if (_playPreview == null)
            {
                Debug.LogWarning("[LevelDesignSheet] UnityEditor.AudioUtil.PlayPreviewClip 없음 — 미리 듣기를 쓸 수 없음");
                return;
            }

            _stopPreviews?.Invoke(null, null);
            _playPreview.Invoke(null, new object[] { clip, 0, false });
        }

        // ── 공통 표 ──────────────────────────────────────────

        private sealed class Column
        {
            public string Property;   // null이면 계산 열
            public string Header;
            public string Tooltip;
            public Func<SerializedObject, string> Compute;

            public static Column Computed(string header, string tooltip, Func<SerializedObject, string> compute)
            {
                return new Column { Header = header, Tooltip = tooltip, Compute = compute };
            }
        }

        private static void InsertAfter(List<Column> columns, string property, Column column)
        {
            int index = columns.FindIndex(c => c.Property == property);
            columns.Insert(index >= 0 ? index + 1 : columns.Count, column);
        }

        // 행 에셋들의 최상위 직렬화 필드로 열을 만든다(첫 행 기준 순서, 다른 행에만 있는 필드는 뒤에 붙임).
        private static List<Column> PropertyColumns(List<SerializedObject> rows, bool onlyBase, bool excludeBase = false)
        {
            var columns = new List<Column>();
            var seen = new HashSet<string>();
            foreach (SerializedObject so in rows)
            {
                Type type = so.targetObject.GetType();
                foreach (SerializedProperty prop in TopLevelProperties(so))
                {
                    bool isBase = BaseMonsterFields.Contains(prop.name);
                    if ((onlyBase && !isBase) || (excludeBase && isBase) || !seen.Add(prop.name))
                    {
                        continue;
                    }

                    columns.Add(new Column { Property = prop.name, Header = HeaderOf(type, prop), Tooltip = TooltipOf(type, prop.name) });
                }
            }

            return columns;
        }

        private static HashSet<string> BaseMonsterFields
        {
            get
            {
                if (_baseMonsterFields == null)
                {
                    _baseMonsterFields = new HashSet<string>(typeof(MonsterStatsConfig)
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Select(f => f.Name));
                }

                return _baseMonsterFields;
            }
        }

        private static IEnumerable<SerializedProperty> TopLevelProperties(SerializedObject so)
        {
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script")
                {
                    continue;
                }

                yield return it.Copy();
            }
        }

        private void DrawTable(string key, List<SerializedObject> rows, List<Column> columns)
        {
            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox(U("msg.noAsset"), MessageType.Info);
                return;
            }

            if (columns.Count == 0)
            {
                EditorGUILayout.LabelField(U("msg.noValues"), EditorStyles.miniLabel);
                return;
            }

            foreach (SerializedObject so in rows)
            {
                so.Update();
            }

            List<GUIContent> names = rows.Select(so => new GUIContent(RowName(so.targetObject), U("row.pingTip", so.targetObject.name))).ToList();
            List<UnityEngine.Object> targets = rows.Select(so => so.targetObject).ToList();
            List<GUIContent> headers = columns.Select(c => new GUIContent(c.Header, c.Tooltip)).ToList();
            DrawGrid(key, U("corner.asset"), names, targets, headers, null, (rect, row, col) =>
            {
                SerializedObject so = rows[row];
                EditorGUI.BeginChangeCheck();
                DrawCell(rect, so, columns[col]);
                if (EditorGUI.EndChangeCheck())
                {
                    Save(so);
                }
            });
        }

        /// <summary>
        /// 표 하나를 직접 계산한 칸 위치로 그린다. 왼쪽 이름 열은 고정, 오른쪽 값 열들은 가로 스크롤.
        /// 줄 높이는 RowHeight 하나라 이름 열과 값 열이 어긋나지 않는다. 열 폭은 widths(없으면 제목 길이로 자동),
        /// 제목 칸 높이는 가장 긴(줄바꿈된) 제목에 맞춘다.
        /// </summary>
        private void DrawGrid(string key, string corner, IReadOnlyList<GUIContent> rowNames, IReadOnlyList<UnityEngine.Object> pingTargets,
            IReadOnlyList<GUIContent> headers, IReadOnlyList<float> widths, Action<Rect, int, int> drawCell)
        {
            float rowHeight = RowHeight;
            var colWidths = new float[headers.Count];
            float headerHeight = rowHeight * 2f;
            for (int c = 0; c < headers.Count; c++)
            {
                colWidths[c] = widths != null ? widths[c] : AutoColumnWidth(headers[c]);
                headerHeight = Mathf.Max(headerHeight, HeaderStyle.CalcHeight(headers[c], colWidths[c] - 6f) + 8f);
            }

            float nameWidth = MinNameWidth;
            foreach (GUIContent name in rowNames)
            {
                nameWidth = Mathf.Max(nameWidth, NameStyle.CalcSize(name).x + 16f);
            }

            nameWidth = Mathf.Min(nameWidth, MaxNameWidth);
            float contentWidth = colWidths.Sum();
            float bodyHeight = headerHeight + rowNames.Count * rowHeight;
            Rect area = GUILayoutUtility.GetRect(0f, bodyHeight + ScrollbarHeight, GUILayout.ExpandWidth(true));
            area.xMin += 4f;
            area.xMax -= 4f;

            // 줄 배경(홀수 줄 음영) — 이름 열과 값 열을 가로질러 같은 줄임을 보이게.
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(area.x, area.y + headerHeight - 1f, area.width, 1f), LineColor);
                for (int r = 1; r < rowNames.Count; r += 2)
                {
                    EditorGUI.DrawRect(new Rect(area.x, area.y + headerHeight + r * rowHeight, area.width, rowHeight), StripeColor);
                }
            }

            // 고정 이름 열
            GUI.Label(new Rect(area.x, area.y, nameWidth, headerHeight), corner, HeaderStyle);
            for (int r = 0; r < rowNames.Count; r++)
            {
                Rect nameRect = new Rect(area.x + 4f, area.y + headerHeight + r * rowHeight, nameWidth - 8f, rowHeight);
                UnityEngine.Object target = pingTargets != null ? pingTargets[r] : null;
                if (target != null)
                {
                    if (GUI.Button(nameRect, rowNames[r], NameStyle))
                    {
                        EditorGUIUtility.PingObject(target);
                    }
                }
                else
                {
                    GUI.Label(nameRect, rowNames[r], NameStyle);
                }
            }

            // 가로 스크롤되는 값 열
            Rect view = new Rect(area.x + nameWidth, area.y, Mathf.Max(0f, area.width - nameWidth), area.height);
            _hScroll.TryGetValue(key, out Vector2 scroll);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, contentWidth, bodyHeight), false, false);
            float x = 0f;
            for (int c = 0; c < headers.Count; c++)
            {
                GUI.Label(new Rect(x + 3f, 0f, colWidths[c] - 6f, headerHeight), headers[c], HeaderStyle);
                x += colWidths[c];
            }

            for (int r = 0; r < rowNames.Count; r++)
            {
                x = 0f;
                float y = headerHeight + r * rowHeight + (rowHeight - EditorGUIUtility.singleLineHeight) * 0.5f;
                for (int c = 0; c < headers.Count; c++)
                {
                    drawCell(new Rect(x + 3f, y, colWidths[c] - 6f, EditorGUIUtility.singleLineHeight), r, c);
                    x += colWidths[c];
                }
            }

            GUI.EndScrollView();
            _hScroll[key] = scroll;
            EditorGUILayout.Space(4);
        }

        // 제목 길이에 맞춘 열 폭: 한 줄에 들어가면 그 폭, 길면 두 줄로 접히는 폭(최대 MaxCellWidth, 넘으면 세 줄 이상으로 접힘).
        private static float AutoColumnWidth(GUIContent header)
        {
            float oneLine = HeaderStyle.CalcSize(header).x + 10f;
            if (oneLine <= MaxCellWidth)
            {
                return Mathf.Max(MinCellWidth, oneLine);
            }

            return Mathf.Clamp(oneLine * 0.5f + 16f, MinCellWidth, MaxCellWidth);
        }

        /// <summary>
        /// 값 칸 하나 — 값 입력란만 그린다. 표 칸에서는 EditorGUI.PropertyField를 쓰지 않는다: PropertyField는 필드에 붙은
        /// [Header]·[Space]·[Range] 장식까지 칸 안에 그려서 묶음 첫 필드의 값이 아래로 밀려 잘렸다(2026-10-14 사용자 피드백 —
        /// 잡몹 체력·예고 시간, 동물 스킬 탭 개·원숭이의 첫 필드인 프리팹 참조 등). 그래서 타입별 입력란을 직접 쓴다.
        /// 에셋 참조는 필드의 실제 타입으로 제한한 ObjectField. 칸에 못 그리는 타입이면 false — 호출 쪽이 '인스펙터…' 버튼을 그린다.
        /// BeginProperty/EndProperty로 감싸 Undo·프리팹 덮어쓰기 표시는 그대로 유지한다.
        /// </summary>
        private static bool ValueField(Rect rect, SerializedProperty prop)
        {
            bool handled = true;
            EditorGUI.BeginProperty(rect, GUIContent.none, prop);
            EditorGUI.BeginChangeCheck();
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                {
                    int value = EditorGUI.IntField(rect, prop.intValue);
                    if (EditorGUI.EndChangeCheck()) prop.intValue = value;
                    break;
                }
                case SerializedPropertyType.Float:
                {
                    float value = EditorGUI.FloatField(rect, prop.floatValue);
                    if (EditorGUI.EndChangeCheck()) prop.floatValue = value;
                    break;
                }
                case SerializedPropertyType.Boolean:
                {
                    bool value = EditorGUI.Toggle(rect, prop.boolValue);
                    if (EditorGUI.EndChangeCheck()) prop.boolValue = value;
                    break;
                }
                case SerializedPropertyType.Enum:
                {
                    int value = EditorGUI.Popup(rect, prop.enumValueIndex, EnumOptionNames(prop));
                    if (EditorGUI.EndChangeCheck()) prop.enumValueIndex = value;
                    break;
                }
                case SerializedPropertyType.String:
                {
                    string value = EditorGUI.TextField(rect, prop.stringValue);
                    if (EditorGUI.EndChangeCheck()) prop.stringValue = value;
                    break;
                }
                case SerializedPropertyType.Color:
                {
                    Color value = EditorGUI.ColorField(rect, prop.colorValue);
                    if (EditorGUI.EndChangeCheck()) prop.colorValue = value;
                    break;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    UnityEngine.Object value = EditorGUI.ObjectField(rect, prop.objectReferenceValue, FieldTypeOf(prop), false);
                    if (EditorGUI.EndChangeCheck()) prop.objectReferenceValue = value;
                    break;
                }
                case SerializedPropertyType.Vector2:
                {
                    Vector2 value = EditorGUI.Vector2Field(rect, GUIContent.none, prop.vector2Value);
                    if (EditorGUI.EndChangeCheck()) prop.vector2Value = value;
                    break;
                }
                case SerializedPropertyType.Vector3:
                {
                    Vector3 value = EditorGUI.Vector3Field(rect, GUIContent.none, prop.vector3Value);
                    if (EditorGUI.EndChangeCheck()) prop.vector3Value = value;
                    break;
                }
                case SerializedPropertyType.Vector4:
                {
                    Vector4 value = EditorGUI.Vector4Field(rect, GUIContent.none, prop.vector4Value);
                    if (EditorGUI.EndChangeCheck()) prop.vector4Value = value;
                    break;
                }
                case SerializedPropertyType.Vector2Int:
                {
                    Vector2Int value = EditorGUI.Vector2IntField(rect, GUIContent.none, prop.vector2IntValue);
                    if (EditorGUI.EndChangeCheck()) prop.vector2IntValue = value;
                    break;
                }
                case SerializedPropertyType.Vector3Int:
                {
                    Vector3Int value = EditorGUI.Vector3IntField(rect, GUIContent.none, prop.vector3IntValue);
                    if (EditorGUI.EndChangeCheck()) prop.vector3IntValue = value;
                    break;
                }
                case SerializedPropertyType.AnimationCurve:
                {
                    AnimationCurve value = EditorGUI.CurveField(rect, prop.animationCurveValue);
                    if (EditorGUI.EndChangeCheck()) prop.animationCurveValue = value;
                    break;
                }
                case SerializedPropertyType.Gradient:
                {
                    Gradient value = EditorGUI.GradientField(rect, prop.gradientValue);
                    if (EditorGUI.EndChangeCheck()) prop.gradientValue = value;
                    break;
                }
                case SerializedPropertyType.LayerMask:
                {
                    int shown = InternalEditorUtility.LayerMaskToConcatenatedLayersMask(prop.intValue);
                    int value = EditorGUI.MaskField(rect, shown, InternalEditorUtility.layers);
                    if (EditorGUI.EndChangeCheck()) prop.intValue = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(value);
                    break;
                }
                default:
                    // Rect·Bounds(두 줄 필드)·문자·관리 참조 등 — 칸에 못 그린다.
                    EditorGUI.EndChangeCheck();
                    handled = false;
                    break;
            }

            EditorGUI.EndProperty();
            return handled;
        }

        private static readonly Dictionary<(Type, string), Type> FieldTypeCache = new Dictionary<(Type, string), Type>();

        /// <summary>
        /// 직렬화 경로(propertyPath)가 가리키는 C# 필드 타입(ObjectField 타입 제한용). 예: BossPatternSet의
        /// patterns.Array.data[0].pattern → BossPatternBase. Array.data[i]는 배열·List 원소 타입으로, 상속 필드는 부모 타입까지 찾는다.
        /// 못 찾으면 UnityEngine.Object.
        /// </summary>
        // 선택 칸(enum) 항목 이름: 사전 키 enum.<타입>.<값 이름>(예: enum.BossPatternSet.FirstCooldownRule.Zero), 없으면 Unity 표시 이름.
        private static string[] EnumOptionNames(SerializedProperty prop)
        {
            EnsureFieldTexts();
            Type enumType = FieldTypeOf(prop);
            string[] names = prop.enumNames;
            string[] display = prop.enumDisplayNames;
            var result = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                result[i] = enumType != null && enumType.IsEnum && _fieldTexts.TryGetValue("enum." + TypeKey(enumType) + "." + names[i], out FieldText text)
                    ? text.Name
                    : display[i];
            }

            return result;
        }

        private static Type FieldTypeOf(SerializedProperty prop)
        {
            Type root = prop.serializedObject.targetObject.GetType();
            string path = prop.propertyPath;
            if (FieldTypeCache.TryGetValue((root, path), out Type cached))
            {
                return cached;
            }

            Type type = root;
            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length && type != null; i++)
            {
                if (parts[i] == "Array" && i + 1 < parts.Length && parts[i + 1].StartsWith("data[", StringComparison.Ordinal))
                {
                    type = ElementType(type);
                    i++;
                    continue;
                }

                type = FindFieldInHierarchy(type, parts[i])?.FieldType;
            }

            Type result = type != null && typeof(UnityEngine.Object).IsAssignableFrom(type) ? type : typeof(UnityEngine.Object);
            FieldTypeCache[(root, path)] = result;
            return result;
        }

        private static Type ElementType(Type collection)
        {
            if (collection.IsArray)
            {
                return collection.GetElementType();
            }

            return collection.IsGenericType && collection.GetGenericTypeDefinition() == typeof(List<>) ? collection.GetGenericArguments()[0] : null;
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (info != null)
                {
                    return info;
                }
            }

            return null;
        }

        // 칸에 못 그리는 값: 누르면 인스펙터에서 그 에셋을 연다.
        private static void InspectorButton(Rect rect, UnityEngine.Object target, string tooltip, string label = null)
        {
            string tip = string.IsNullOrEmpty(tooltip) ? U("cell.inspector.tip") : tooltip + "\n" + U("cell.inspector.tip");
            if (GUI.Button(rect, new GUIContent(label ?? U("cell.inspector"), tip), EditorStyles.miniButton))
            {
                Selection.activeObject = target;
            }
        }

        // 값 칸에 마우스를 올리면 설명이 뜨게(라벨 없는 입력란은 툴팁을 보여 주지 않음). 입력은 막지 않는다.
        private static void CellTooltip(Rect rect, string tooltip)
        {
            if (!string.IsNullOrEmpty(tooltip))
            {
                GUI.Label(rect, new GUIContent(string.Empty, tooltip));
            }
        }

        // 탭 맨 위 설명 줄(줄바꿈, 보통 크기 — 예전엔 작은 한 줄 글자라 잘렸다).
        private static void Info(string text)
        {
            EditorGUILayout.LabelField(text, InfoStyle);
            EditorGUILayout.Space(2);
        }

        private static void DrawCell(Rect rect, SerializedObject so, Column column)
        {
            if (column.Compute != null)
            {
                EditorGUI.LabelField(rect, new GUIContent(column.Compute(so), column.Tooltip), ComputedStyle);
                return;
            }

            SerializedProperty prop = so.FindProperty(column.Property);
            if (prop == null)
            {
                EditorGUI.LabelField(rect, new GUIContent("—", U("cell.missingField")));
                return;
            }

            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                // 배열·리스트는 칸이 좁아 인스펙터로 넘긴다.
                InspectorButton(rect, so.targetObject, column.Tooltip, U("cell.array", prop.arraySize));
                return;
            }

            if (!ValueField(rect, prop))
            {
                InspectorButton(rect, so.targetObject, column.Tooltip);
                return;
            }

            CellTooltip(rect, column.Tooltip);
        }

        private static void Save(SerializedObject so)
        {
            so.ApplyModifiedProperties(); // Undo 기록 포함
            EditorUtility.SetDirty(so.targetObject);
            AssetDatabase.SaveAssetIfDirty(so.targetObject);
        }

        private bool Foldout(string key, string label)
        {
            _foldouts.TryGetValue(key, out bool open);
            open = EditorGUILayout.Foldout(open, label, true);
            _foldouts[key] = open;
            return open;
        }

        // 글자 스타일 — 모두 보통 크기(작은 글자 miniBoldLabel은 잘리고 읽기 어려워 쓰지 않는다).
        private static GUIStyle _headerStyle, _nameStyle, _valueStyle, _computedStyle, _infoStyle;

        // 열 제목: 굵게, 줄바꿈, 칸 아래쪽 가운데 정렬(제목 길이가 달라도 값 바로 위에 붙는다).
        private static GUIStyle HeaderStyle => _headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            wordWrap = true, alignment = TextAnchor.LowerCenter, clipping = TextClipping.Clip,
        };

        private static GUIStyle NameStyle => _nameStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip,
        };

        private static GUIStyle ValueStyle => _valueStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
        };

        private static GUIStyle ComputedStyle => _computedStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
        };

        private static GUIStyle InfoStyle => _infoStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel);

        private static Color StripeColor => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.04f) : new Color(0f, 0f, 0f, 0.05f);
        private static Color LineColor => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.15f) : new Color(0f, 0f, 0f, 0.2f);
    }
}
