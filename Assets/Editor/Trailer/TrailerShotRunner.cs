using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AbsorbRoguelike.Abilities;
using AbsorbRoguelike.Absorption;
using AbsorbRoguelike.Dungeon;
using AbsorbRoguelike.Monster;
using AbsorbRoguelike.Player;
using AbsorbRoguelike.UI;
using Mirror;
using UnityEditor;
using UnityEngine;

namespace AbsorbRoguelike.EditorTools.Trailer
{
    /// <summary>
    /// 트레일러 자동 샷(2026-10-19). EditorApplication.update로 단계(IEnumerator)를 한 칸씩 진행하는 상태 기계 — 대기는
    /// EditorApplication.timeSinceStartup(실시간) 기준. 공통 흐름: 전제 확인 → (호스트 샷) 플레이어 무적 켜기(원래 값 기억)
    /// → F1 패널 닫기 → 준비 단계 → 녹화 시작 → 연출 단계 → 녹화 정지 → 무적 원래 값 복구.
    /// 한 번에 한 시퀀스만, 취소·Play 종료 시 녹화 정지 + 복구. 게임 규칙·수치는 바꾸지 않고 디버그 메뉴의 정적 헬퍼만 부른다.
    /// 몽타주 자동 조작 샷(2-1~2-9): 특성 부여 → 몹 소환 → 녹화 → 가상 입력(TrailerVirtualInput) 대본 → 녹화 정지를 테이크 수만큼 반복
    /// (RunMontage·RunMontageAll — MCP Unity_RunCommand에서 리플렉션 없이 호출 가능). 대본은 정해진 입력만(실시간 판단 없음).
    /// 2차 자동 조작 샷(1-1·1-2/1-3·3-1·3-2·5-2·6-2·6-5·7-1, 2026-10-19): 촬영 준비(PrepareCleanRoom — 가장 넓은 일반 방으로 이동·방 몹
    /// 처치·주변 몬스터/시체 숨김) 뒤 같은 테이크 흐름 + 보스·승리 전용 흐름(RunExtra·RunExtraAll). 7-1은 판이 끝나므로 항상 마지막.
    /// 자세한 내용: docs/04_구현위키/트레일러_촬영_도구.md
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerShotRunner
    {
        public enum Shot
        {
            FloorTiles,   // 5-1 층 타일셋
            BossEntrance, // 6-1 보스 방 입장
            Quake,        // 6-3 지진 강타(패턴 D)
            EnrageRoar,   // 6-4 폭주 대지분쇄(패턴 E)
            Title,        // 7-2 타이틀
            // 몽타주 자동 조작 샷(2026-10-19) — 2. 핵심 루프 몽타주
            Ox,           // 2-1 소 돌진 넉백
            Tiger,        // 2-2 호랑이 3연타
            Horse,        // 2-3 말 2단 관통 대시
            Monkey,       // 2-4 원숭이 바나나
            Dragon,       // 2-5 용 브레스
            Rabbit,       // 2-6 토끼 귀 채찍 끝부분
            Snake,        // 2-7 뱀 휘감기 둔화
            Rat,          // 2-8 쥐 갉기 취약 스택
            Pig,          // 2-9 돼지 가드 반격
            // 2차 자동 조작 샷(2026-10-19) — 몽타주 밖의 1·3·5·6·7번 샷. 몽타주(Ox~Pig) 뒤에 두고 IsExtra로 따로 판정한다.
            Walk,         // 1-1 슬라임 혼자 → 걸어 나감
            AbsorbFirst,  // 1-2 + 1-3 처치 직전까지 → 흡수 → 탈 착용(한 파일)
            Swap,         // 3-1 호랑이 탈 → 용 흡수로 탈 교체
            Release,      // 3-2 피격으로 특성 방출(무적 끔)
            Burn,         // 5-2 화염 속성 명중(화상 숫자·상태 표시)
            BossDodge,    // 6-2 패턴 C 연속 돌진을 대시로 피함
            BossFinish,   // 6-5 보스 체력 1 → 마지막 한 대
            Victory,      // 7-1 마지막 층 금빛 포탈로 걸어 들어감(판 종료)
        }

        /// <summary>몽타주 샷 순서(RunMontageAll·창 버튼).</summary>
        public static readonly Shot[] MontageShots =
        {
            Shot.Ox, Shot.Tiger, Shot.Horse, Shot.Monkey, Shot.Dragon, Shot.Rabbit, Shot.Snake, Shot.Rat, Shot.Pig,
        };

        /// <summary>2차 샷 순서(RunExtraAll·창 버튼). 7-1은 판이 끝나므로 항상 마지막.</summary>
        public static readonly Shot[] ExtraShots =
        {
            Shot.Walk, Shot.AbsorbFirst, Shot.Swap, Shot.Release, Shot.Burn, Shot.BossDodge, Shot.BossFinish, Shot.Victory,
        };

        /// <summary>몽타주 샷 기본 테이크 수.</summary>
        public const int DefaultTakes = 1;

        /// <summary>몽타주(2-1~2-9) 샷인지. 2차 샷이 enum 뒤에 붙어 있어 범위로 판정한다.</summary>
        public static bool IsMontage(Shot shot) => shot >= Shot.Ox && shot <= Shot.Pig;

        /// <summary>2차 자동 조작 샷(1-1·1-2/1-3·3-1·3-2·5-2·6-2·6-5·7-1)인지.</summary>
        public static bool IsExtra(Shot shot) => shot >= Shot.Walk;

        /// <summary>2차 샷 중 넓은 일반 방에서 찍는 샷(테이크 흐름 = 몽타주와 같음). 나머지(6-2·6-5·7-1)는 보스 방.</summary>
        public static bool IsRoomShot(Shot shot) => shot >= Shot.Walk && shot <= Shot.Burn;

        private const int FloorTileRepeats = 4;
        private const float FloorTileHold = 1.5f;
        private const float GenerateTimeout = 20f;
        private const float BossAppearTimeout = 10f;

        private sealed class WaitSeconds
        {
            public readonly double Until;
            public WaitSeconds(float seconds) { Until = EditorApplication.timeSinceStartup + seconds; }
        }

        // 게임 시간(Time.time) 대기 — 몽타주 입력 대본용. Recorder가 60fps 고정으로 돌리면 게임 시간이 실시간보다 느릴 수 있어
        // 스킬 타이밍(말 이어쓰기 창 등)은 게임 시간으로 맞춘다.
        private sealed class WaitGameSeconds
        {
            public readonly float Until;
            public WaitGameSeconds(float seconds) { Until = Time.time + seconds; }
        }

        private sealed class WaitCondition
        {
            public readonly Func<bool> Condition;
            public readonly double Deadline;
            public readonly string TimeoutMessage;

            public WaitCondition(Func<bool> condition, float timeout, string timeoutMessage)
            {
                Condition = condition;
                Deadline = EditorApplication.timeSinceStartup + timeout;
                TimeoutMessage = timeoutMessage;
            }
        }

        private static IEnumerator _routine;
        private static object _current;
        private static PlayerHealth _invincibleTarget;
        private static bool _originalInvincible;

        /// <summary>시퀀스 실행 중인지.</summary>
        public static bool IsRunning => _routine != null;

        /// <summary>실행 중인 시퀀스 이름(창 표시용). 없으면 null.</summary>
        public static string RunningName { get; private set; }

        /// <summary>현재 단계 설명(창 표시용).</summary>
        public static string Status { get; private set; }

        static TrailerShotRunner()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingPlayMode && IsRunning)
                {
                    Abort("Play 종료 — 시퀀스를 강제 중단한다.");
                }
            };
        }

        public static string ShotCode(Shot shot)
        {
            switch (shot)
            {
                case Shot.FloorTiles: return "5-1";
                case Shot.BossEntrance: return "6-1";
                case Shot.Quake: return "6-3";
                case Shot.EnrageRoar: return "6-4";
                case Shot.Title: return "7-2";
                case Shot.Ox: return "2-1";
                case Shot.Tiger: return "2-2";
                case Shot.Horse: return "2-3";
                case Shot.Monkey: return "2-4";
                case Shot.Dragon: return "2-5";
                case Shot.Rabbit: return "2-6";
                case Shot.Snake: return "2-7";
                case Shot.Rat: return "2-8";
                case Shot.Pig: return "2-9";
                case Shot.Walk: return "1-1";
                case Shot.AbsorbFirst: return "1-2";
                case Shot.Swap: return "3-1";
                case Shot.Release: return "3-2";
                case Shot.Burn: return "5-2";
                case Shot.BossDodge: return "6-2";
                case Shot.BossFinish: return "6-5";
                case Shot.Victory: return "7-1";
                default: throw new ArgumentOutOfRangeException(nameof(shot), shot, null);
            }
        }

        public static string ShotTitle(Shot shot)
        {
            switch (shot)
            {
                case Shot.FloorTiles: return "층 타일셋";
                case Shot.BossEntrance: return "보스 방 입장";
                case Shot.Quake: return "지진 강타";
                case Shot.EnrageRoar: return "폭주 대지분쇄";
                case Shot.Title: return "타이틀";
                case Shot.Ox: return "소 돌진 넉백";
                case Shot.Tiger: return "호랑이 3연타";
                case Shot.Horse: return "말 2단 대시";
                case Shot.Monkey: return "원숭이 바나나";
                case Shot.Dragon: return "용 브레스";
                case Shot.Rabbit: return "토끼 귀 채찍";
                case Shot.Snake: return "뱀 휘감기";
                case Shot.Rat: return "쥐 갉기 스택";
                case Shot.Pig: return "돼지 가드 반격";
                case Shot.Walk: return "슬라임 걸어 나감";
                case Shot.AbsorbFirst: return "처치·흡수(1-2+1-3)";
                case Shot.Swap: return "탈 교체(호랑이→용)";
                case Shot.Release: return "피격 특성 방출";
                case Shot.Burn: return "화염 명중";
                case Shot.BossDodge: return "연속 돌진 회피";
                case Shot.BossFinish: return "보스 마지막 한 대";
                case Shot.Victory: return "금빛 포탈 승리";
                default: throw new ArgumentOutOfRangeException(nameof(shot), shot, null);
            }
        }

        // ── 전제 조건 ─────────────────────────────────────────────

        private static GameObject LocalPlayer => NetworkClient.localPlayer != null ? NetworkClient.localPlayer.gameObject : null;

        /// <summary>호스트 샷 공통: Play + NetworkServer.active + 로컬 플레이어.</summary>
        public static bool IsHostReady => EditorApplication.isPlaying && NetworkServer.active && LocalPlayer != null;

        /// <summary>타이틀 샷: Play 중이고 호스트·클라이언트 모두 시작 전.</summary>
        public static bool IsTitleReady => EditorApplication.isPlaying && !NetworkServer.active && !NetworkClient.active;

        /// <summary>
        /// 샷을 지금 실행할 수 없는 이유(실행 가능하면 null). 창의 버튼 활성/툴팁과 Run의 확인에 같이 쓴다.
        /// </summary>
        public static string CannotRunReason(Shot shot)
        {
            if (IsRunning)
            {
                return "다른 시퀀스 실행 중";
            }

            if (TrailerRecorder.IsRecording)
            {
                return "수동 녹화 중";
            }

            return ShotUnavailableReason(shot);
        }

        // 실행 중·녹화 중 여부를 빼고 본 샷 전제 조건 — 2차 샷 연속 촬영(RunExtraAll)이 샷마다 다시 확인할 때도 쓴다.
        private static string ShotUnavailableReason(Shot shot)
        {
            if (shot == Shot.Title)
            {
                return IsTitleReady ? null : "Play 중 + 호스트 시작 전(타이틀 화면)에서만";
            }

            if (!IsHostReady)
            {
                return "Play 중 + 호스트 + 로컬 플레이어 필요";
            }

            if (IsMontage(shot))
            {
                return MontageUnavailableReason(shot);
            }

            if (IsExtra(shot))
            {
                return ExtraUnavailableReason(shot);
            }

            switch (shot)
            {
                case Shot.FloorTiles:
                    if (FloorProgression.Instance == null)
                    {
                        return "FloorProgression 없음";
                    }

                    if (FloorProgression.Instance.IsLastFloor)
                    {
                        return "마지막 층이라 다음 층이 없음";
                    }

                    return DebugMenuController.SpawnPoint() == null ? "시작 지점 없음" : null;
                case Shot.BossEntrance:
                    return BossRoomUnavailableReason();
                case Shot.Quake:
                case Shot.EnrageRoar:
                    return DebugMenuController.FindAliveBossAttack() != null ? null : BossRoomUnavailableReason();
            }

            return null;
        }

        private static string BossRoomUnavailableReason()
        {
            RoomClearController room = DebugMenuController.FindBossRoom();
            if (room == null)
            {
                return "보스 방 없음";
            }

            return room.IsCleared ? "이미 보스 처치(보스 방 클리어)" : null;
        }

        /// <summary>5-1 경고: 1층이 아니면 4개 층을 다 못 보여 줄 수 있다.</summary>
        public static string FloorTilesWarning()
        {
            var dungeon = FloorProgression.Instance;
            if (dungeon == null || dungeon.Floor == 1)
            {
                return null;
            }

            return $"지금 {dungeon.Floor}층 — 1층에서 시작해야 1~{Mathf.Min(dungeon.MaxFloors, 1 + FloorTileRepeats)}층 타일셋이 모두 찍힌다.";
        }

        // ── 실행·취소 ─────────────────────────────────────────────

        public static void Run(Shot shot)
        {
            if (IsMontage(shot))
            {
                RunMontage(shot, DefaultTakes);
                return;
            }

            if (IsExtra(shot))
            {
                RunExtra(shot, DefaultTakes);
                return;
            }

            string reason = CannotRunReason(shot);
            if (reason != null)
            {
                Debug.LogError($"[Trailer] {ShotCode(shot)} 실행 불가: {reason}");
                return;
            }

            if (shot == Shot.FloorTiles && FloorTilesWarning() != null)
            {
                Debug.LogWarning($"[Trailer] {FloorTilesWarning()}");
            }

            RunningName = $"{ShotCode(shot)} {ShotTitle(shot)}";
            Status = "시작";
            _current = null;
            _routine = Sequence(shot);
            Debug.Log($"[Trailer] 시퀀스 시작: {RunningName}");
        }

        public static void Cancel()
        {
            if (IsRunning)
            {
                Abort("사용자 취소.");
            }
        }

        private static void Abort(string message)
        {
            Debug.LogWarning($"[Trailer] {RunningName} 중단: {message}");
            try
            {
                RestoreHidden(); // 중단이면 촬영 준비가 숨긴 몬스터·시체를 되돌린다(정상 완료면 숨긴 채로 둔다)
            }
            finally
            {
                Finish();
            }
        }

        // 완료·취소·Play 종료·예외 공통 정리. 한 단계가 실패해도 나머지는 하도록 finally로 잇는다.
        private static void Finish()
        {
            try
            {
                TrailerRecorder.StopRecording();
            }
            finally
            {
                try
                {
                    CleanupMontage(); // 가상 입력 ReleaseAll+Remove(포커스 설정 복구), 몽타주 소환 몹 제거
                }
                finally
                {
                    RestoreElement(); // 5-2가 바꾼 속성 원복(바꾼 적 없으면 아무것도 안 함)
                    RestoreInvincible();
                    _routine = null;
                    _current = null;
                    RunningName = null;
                    Status = null;
                }
            }
        }

        private static void Tick()
        {
            if (_routine == null)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                Abort("Play 중이 아님.");
                return;
            }

            try
            {
                // 대기 중인 단계가 끝날 때까지 진행하지 않는다.
                if (_current is WaitSeconds wait)
                {
                    if (EditorApplication.timeSinceStartup < wait.Until)
                    {
                        return;
                    }
                }
                else if (_current is WaitGameSeconds gameWait)
                {
                    if (Time.time < gameWait.Until)
                    {
                        return;
                    }
                }
                else if (_current is WaitCondition until)
                {
                    if (!until.Condition())
                    {
                        if (EditorApplication.timeSinceStartup > until.Deadline)
                        {
                            Debug.LogError($"[Trailer] {until.TimeoutMessage}");
                            Abort("대기 시간 초과.");
                        }

                        return;
                    }
                }

                if (_routine.MoveNext())
                {
                    _current = _routine.Current;
                }
                else
                {
                    Debug.Log($"[Trailer] 시퀀스 완료: {RunningName} → {TrailerRecorder.LastFilePath}");
                    Finish();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Abort("예외 발생.");
            }
        }

        // ── 공통 흐름 ─────────────────────────────────────────────

        private static IEnumerator Sequence(Shot shot)
        {
            bool hostShot = shot != Shot.Title;
            if (hostShot)
            {
                EnableInvincible();
            }

            DebugMenuController.CloseIfOpen();

            Status = "준비";
            IEnumerator prepare = Prepare(shot);
            while (prepare.MoveNext())
            {
                yield return prepare.Current;
            }

            Status = "녹화 시작";
            if (!TrailerRecorder.StartRecording(ShotCode(shot)))
            {
                throw new InvalidOperationException("녹화 시작 실패."); // Tick의 catch가 Abort한다
            }

            yield return null; // 첫 프레임이 녹화되도록 한 프레임 넘긴다

            Status = "연출";
            IEnumerator perform = Perform(shot);
            while (perform.MoveNext())
            {
                yield return perform.Current;
            }

            Status = "녹화 정지";
            // 정지·무적 복구는 Finish에서(완료·취소·Play 종료 공통).
        }

        private static void EnableInvincible()
        {
            GameObject player = LocalPlayer;
            _invincibleTarget = player != null ? player.GetComponent<PlayerHealth>() : null;
            if (_invincibleTarget == null)
            {
                return;
            }

            _originalInvincible = _invincibleTarget.DebugInvincible;
            if (!_originalInvincible)
            {
                _invincibleTarget.SetDebugInvincible(true);
            }
        }

        // 몽타주 샷별 무적 전환. EnableInvincible이 잡아 둔 대상·원래 값을 그대로 쓴다.
        private static void SetMontageInvincible(bool on)
        {
            if (_invincibleTarget != null && NetworkServer.active && _invincibleTarget.DebugInvincible != on)
            {
                _invincibleTarget.SetDebugInvincible(on);
            }
        }

        private static void RestoreInvincible()
        {
            if (_invincibleTarget != null && NetworkServer.active && _invincibleTarget.DebugInvincible != _originalInvincible)
            {
                _invincibleTarget.SetDebugInvincible(_originalInvincible);
            }

            _invincibleTarget = null;
        }

        // ── 샷별 준비 단계(녹화 전) ─────────────────────────────────

        private static IEnumerator Prepare(Shot shot)
        {
            switch (shot)
            {
                case Shot.FloorTiles:
                    DebugMenuController.TeleportToSpawnRoom(LocalPlayer);
                    yield return new WaitSeconds(0.5f); // 카메라가 따라붙을 시간
                    break;
                case Shot.Quake:
                    foreach (object step in EnsureBoss())
                    {
                        yield return step;
                    }

                    break;
                case Shot.EnrageRoar:
                    foreach (object step in EnsureBoss())
                    {
                        yield return step;
                    }

                    foreach (object step in PrepareEnrage())
                    {
                        yield return step;
                    }

                    break;
            }
        }

        // 살아 있는 보스가 없으면 보스 방으로 가서(기존 입장 트리거로 스폰) 등장을 기다린다.
        private static IEnumerable EnsureBoss()
        {
            if (DebugMenuController.FindAliveBossAttack() != null)
            {
                yield break;
            }

            Status = "보스 방 이동 · 보스 등장 대기";
            DebugMenuController.TeleportToBossRoom(LocalPlayer);
            yield return new WaitCondition(() => DebugMenuController.FindAliveBossAttack() != null, BossAppearTimeout,
                "보스가 나타나지 않았다(보스 방 입장 트리거 확인).");
            yield return new WaitSeconds(1.5f); // 등장 연출
        }

        // 체력 10% → 폭주(페이즈 4) 전환 대기 → 패턴 E 경고 원 밖·보스 방 안·벽 아닌 지점으로 이동.
        private static IEnumerable PrepareEnrage()
        {
            Status = "보스 체력 10% · 폭주 대기";
            DebugMenuController.SetBossHealthPercent(0.1f);
            yield return new WaitCondition(() =>
                {
                    BossBrain b = DebugMenuController.FindAliveBossAttack();
                    return b != null && b.BossPhaseValue >= 4;
                }, 5f, "보스가 폭주(페이즈 4)로 바뀌지 않았다(패턴 세트 EnrageHealthRatio 확인).");
            yield return new WaitSeconds(1.5f); // 페이즈 전환 표시가 지나가게

            BossBrain brain = DebugMenuController.FindAliveBossAttack();
            if (brain == null)
            {
                throw new InvalidOperationException("폭주 대기 중 보스가 사라졌다.");
            }

            float radius = ReadRoarRadius(brain);
            RoomClearController room = DebugMenuController.FindBossRoom();
            Vector2 target = FindSafePoint(room, brain.Body != null ? brain.Body.position : (Vector2)brain.transform.position, radius + 1.5f);
            DebugMenuController.Teleport(LocalPlayer, target);
            yield return new WaitSeconds(0.5f);
        }

        // 패턴 E 경고 원 반경을 보스의 패턴 세트 에셋에서 읽는다(BossPatternRoar.roarRadius, 하드코딩 금지).
        private static float ReadRoarRadius(BossBrain brain) => ReadPatternProperty(brain, "E", "roarRadius").floatValue;

        // 보스 패턴 세트에서 PatternId가 같은 항목 에셋의 직렬화 필드를 읽는다(수치 하드코딩 금지). 없으면 예외.
        private static SerializedProperty ReadPatternProperty(BossBrain brain, string patternId, string field)
        {
            if (brain.PatternSet != null)
            {
                foreach (BossPatternSet.Entry entry in brain.PatternSet.Patterns)
                {
                    if (entry.pattern != null && entry.pattern.PatternId == patternId)
                    {
                        SerializedProperty prop = new SerializedObject(entry.pattern).FindProperty(field);
                        if (prop != null)
                        {
                            return prop;
                        }
                    }
                }
            }

            throw new InvalidOperationException($"보스 패턴 세트에서 패턴 {patternId}({field})를 찾지 못했다.");
        }

        private static Vector2 FindSafePoint(RoomClearController room, Vector2 boss, float distance)
        {
            if (room == null || room.RoomArea == null)
            {
                throw new InvalidOperationException("보스 방이 없다.");
            }

            Physics2D.SyncTransforms();
            // 거리를 조금씩 늘리며 8방향을 본다(경고 원 밖이 보장되도록 distance 이상만).
            for (float d = distance; d <= distance + 3f; d += 0.5f)
            {
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    Vector2 point = boss + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * d;
                    if (room.RoomArea.OverlapPoint(point) && !DebugMenuController.OverlapsSolid(point))
                    {
                        return point;
                    }
                }
            }

            throw new InvalidOperationException($"보스 방 안에서 보스로부터 {distance:0.0} 이상 떨어진 빈 지점을 찾지 못했다.");
        }

        // ── 샷별 연출 단계(녹화 중) ─────────────────────────────────

        private static IEnumerator Perform(Shot shot)
        {
            switch (shot)
            {
                case Shot.FloorTiles:
                    yield return new WaitSeconds(FloorTileHold);
                    for (int i = 0; i < FloorTileRepeats; i++)
                    {
                        FloorProgression dungeon = FloorProgression.Instance;
                        if (dungeon == null || dungeon.IsLastFloor)
                        {
                            break;
                        }

                        Status = $"다음 층 생성 ({i + 1}/{FloorTileRepeats})";
                        dungeon.AdvanceFloor();
                        yield return new WaitCondition(() => !DungeonGameManager.Instance.IsGenerating, GenerateTimeout,
                            "층 생성이 끝나지 않았다.");
                        yield return new WaitSeconds(0.3f); // 시작 지점·카메라 정리
                        DebugMenuController.TeleportToSpawnRoom(LocalPlayer);
                        Status = $"{dungeon.Floor}층 시작 방";
                        yield return new WaitSeconds(FloorTileHold);
                    }

                    break;
                case Shot.BossEntrance:
                    DebugMenuController.TeleportToBossRoom(LocalPlayer); // 기존 입장 트리거 → 문 잠김·보스 스폰
                    yield return new WaitSeconds(4f);
                    break;
                case Shot.Quake:
                    DebugMenuController.UseBossPattern("D");
                    yield return new WaitSeconds(4f);
                    break;
                case Shot.EnrageRoar:
                    DebugMenuController.UseBossPattern("E");
                    yield return new WaitSeconds(6f); // 경고 → 대지분쇄 → 무방비
                    break;
                case Shot.Title:
                    yield return new WaitSeconds(4f);
                    break;
            }
        }

        // ── 몽타주 자동 조작 샷(2-1~2-9) ─────────────────────────────
        // 흐름(테이크마다): 입력 떼기·이전 몹 제거 → 시작 위치로 이동 → 특성 확인(DebugGrantTrait) → 조준 → 몹 소환
        // → 녹화 시작 → 입력 대본(게임 시간) → 여운 → 녹화 정지 → 몹 제거. 샷 시작 전 무적 켜기, 끝(완료·취소·예외·Play 종료)에
        // Finish가 녹화 정지·가상 입력 정리·몹 제거·무적 복구. 컷 길이 = 촬영 목록 표 길이 + 앞뒤 MontagePad초.

        private const float MontagePad = 1f;
        private const float BetweenTakes = 0.8f; // 대시·돌진 재사용 대기가 풀리도록

        private static readonly List<GameObject> _montageSpawned = new List<GameObject>();

        // 몹 배치: forward = 조준 방향 거리, side = 왼쪽(+)/오른쪽(-) 거리. animal = FloorProgression 동물 표시 이름.
        private readonly struct Mob
        {
            public readonly string Animal;
            public readonly float Forward;
            public readonly float Side;

            public Mob(string animal, float forward, float side)
            {
                Animal = animal;
                Forward = forward;
                Side = side;
            }
        }

        private sealed class MontageSpec
        {
            public string Animal;     // 특성을 받을 동물(표시 이름). ClearTrait면 null
            public float Length;      // 촬영 목록 표 길이(초)
            public float Clearance;   // 조준 방향으로 벽이 없어야 하는 거리(돌진·대시 이동 포함)
            public Mob[] Mobs;
            // 2차 샷용 옵션(2026-10-19). 기본값은 몽타주 동작 그대로.
            public bool ClearTrait;   // 특성 없이(DebugClearTrait) 찍는다 — 1-1·1-2/1-3·5-2
            public bool KeepTrait;    // 든 특성이 있으면 그대로(없을 때만 Animal 특성) — 3-2
            public bool Freeze = true; // 몹 긴 경직(제자리 고정). 막을 공격·때릴 몹이 필요하면 false
            public bool Aggro;        // 소환 직후 몹이 플레이어를 쫓게(MonsterBehaviorController.Aggro) — 3-2
            public bool Vulnerable;   // 무적을 끄고 체력을 채운다 — 2-9·3-2
            public bool ResetAbsorb;  // 방당 흡수 1회 제한을 풀고(ResetRoomAbsorption) 흡수 재사용 대기를 기다린다 — 1-2/1-3·3-1
            public ElementType? Element; // 녹화 동안만 바꿀 속성(끝나면 원복) — 5-2
        }

        // 수치 근거(에셋 값, 2026-10-19): 소 돌진 3칸/0.2초, 호랑이 반경 2.4·반각 25°, 대시 12×0.2=2.4칸·말 이어쓰기 창 0.6초,
        // 바나나 사거리 6, 용 브레스 반경 4·반각 35°, 토끼 귀 길이 4(끝 1칸 = 3~4), 뱀 반경 1.6(원 전체), 쥐 스택 최대 5, 돼지 막기 180°.
        private static MontageSpec Spec(Shot shot)
        {
            switch (shot)
            {
                case Shot.Ox:
                    return new MontageSpec { Animal = "소", Length = 3f, Clearance = 7f,
                        Mobs = new[] { new Mob("소", 1.8f, 0f) } };
                case Shot.Tiger:
                    return new MontageSpec { Animal = "호랑이", Length = 3f, Clearance = 3f,
                        Mobs = new[] { new Mob("호랑이", 2.0f, 0f) } };
                case Shot.Horse:
                    return new MontageSpec { Animal = "말", Length = 2f, Clearance = 6f,
                        Mobs = new[] { new Mob("말", 1.8f, 0f) } };
                case Shot.Monkey:
                    return new MontageSpec { Animal = "원숭이", Length = 3f, Clearance = 5.5f,
                        Mobs = new[] { new Mob("원숭이", 4.0f, 0f) } };
                case Shot.Dragon:
                    return new MontageSpec { Animal = "용", Length = 3f, Clearance = 4.5f,
                        Mobs = new[] { new Mob("용", 2.6f, 0f) } };
                case Shot.Rabbit:
                    return new MontageSpec { Animal = "토끼", Length = 2f, Clearance = 4.5f,
                        Mobs = new[] { new Mob("토끼", 3.5f, 0f) } };
                case Shot.Snake:
                    return new MontageSpec { Animal = "뱀", Length = 2f, Clearance = 4f,
                        Mobs = new[] { new Mob("뱀", 1.2f, 0f) } }; // 몹이 고정이라 휘감기 반경 1.6 안에 둔다
                case Shot.Rat:
                    return new MontageSpec { Animal = "쥐", Length = 2f, Clearance = 2.5f,
                        Mobs = new[] { new Mob("쥐", 1.1f, 0f) } }; // 대검 반경 1.6 안쪽
                case Shot.Pig:
                    return new MontageSpec { Animal = "돼지", Length = 2f, Clearance = 3f, Freeze = false, Vulnerable = true,
                        Mobs = new[] { new Mob("돼지", 2.2f, 0f) } };
                // ── 2차 샷(일반 방) ── 이동 속도 4.5(Player.prefab moveSpeed), 대검 반경 1.6, 흡수 반경 1.2(AbsorbRange)
                case Shot.Walk: // 1초 서 있다가 1.5초 걷기 ≈ 6.75칸
                    return new MontageSpec { ClearTrait = true, Length = 2.5f, Clearance = 7.5f, Mobs = new Mob[0] };
                case Shot.AbsorbFirst: // 1-2(3초) + 1-3(3초)를 한 파일로. 대검·흡수 반경 안쪽
                    return new MontageSpec { ClearTrait = true, ResetAbsorb = true, Length = 6f, Clearance = 2.5f,
                        Mobs = new[] { new Mob("호랑이", 1.0f, 0f) } };
                case Shot.Swap: // 호랑이 3연타(반경 2.4)로 처치 직전 → 용 흡수
                    return new MontageSpec { Animal = "호랑이", ResetAbsorb = true, Length = 4f, Clearance = 2.5f,
                        Mobs = new[] { new Mob("용", 1.0f, 0f) } };
                case Shot.Release: // 근접 몹 2마리가 쫓아와 때림 → 특성 방출
                    return new MontageSpec { Animal = "용", KeepTrait = true, Freeze = false, Aggro = true, Vulnerable = true,
                        Length = 4f, Clearance = 3f,
                        Mobs = new[] { new Mob("호랑이", 1.8f, 0.9f), new Mob("호랑이", 1.8f, -0.9f) } };
                case Shot.Burn: // 양(체력 50) — 화상 2스택이 3초 타도 죽지 않게 타격 수를 체력으로 정한다(Script)
                    return new MontageSpec { ClearTrait = true, Element = ElementType.Fire, Length = 4f, Clearance = 2.5f,
                        Mobs = new[] { new Mob("양", 1.2f, 0f) } };
                default:
                    throw new ArgumentOutOfRangeException(nameof(shot), $"{shot}은 몽타주·일반 방 샷이 아니다.");
            }
        }

        // 입력 대본 — 녹화 시작(t=0)부터. 조준은 녹화 전에 몹 쪽(forward)으로 맞춰 둔다. 대본이 끝나면 총 길이(Length + 앞뒤 Pad)까지 여운.
        private static IEnumerable Script(Shot shot, GameObject player, Vector2 forward)
        {
            switch (shot)
            {
                case Shot.Walk: // 1-1: 특성 없는 슬라임이 서 있다가 벽 없는 방향으로 걸어 나간다
                    yield return new WaitGameSeconds(MontagePad);
                    TrailerVirtualInput.SetMove(forward);
                    yield return new WaitGameSeconds(1.5f);
                    TrailerVirtualInput.SetMove(Vector2.zero);
                    break;
                case Shot.AbsorbFirst: // 1-2/1-3: 대검으로 처치 직전까지 → 흡수 홀드 → 탈 착용(여운)
                    yield return new WaitGameSeconds(MontagePad);
                    foreach (object step in BeatDownAndAbsorb(player)) yield return step;
                    break;
                case Shot.Swap: // 3-1: 호랑이 탈로 용을 처치 직전까지 → 흡수 → 탈이 용으로 바뀜
                    yield return new WaitGameSeconds(0.8f);
                    foreach (object step in BeatDownAndAbsorb(player)) yield return step;
                    break;
                case Shot.Release: // 3-2: 가만히 서서 맞는다 → 특성이 벗겨지는 순간 무적 복구·몹 제거
                    foreach (object step in ReleaseScript(player)) yield return step;
                    break;
                case Shot.Burn: // 5-2: 화염 속성 대검으로 몇 번만 쳐서 화상 숫자·상태 표시(몹은 살아 있게)
                    foreach (object step in BurnScript()) yield return step;
                    break;
                case Shot.Ox: // 클릭 1번 = 돌진 1번(IsOxCharging 0.2초 + 재사용 대기 0.2초 — 0.1초 누름이면 한 번만). 첫 돌진 → 넉백, 이어서 한 번 더
                    yield return new WaitGameSeconds(MontagePad);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 0.1f)) yield return step;
                    yield return new WaitGameSeconds(1.3f);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 0.1f)) yield return step;
                    break;
                case Shot.Tiger: // 누르고 있으면 스윙(0.25초, 3타) + 재사용 대기(0.2초)마다 자동 연속 — 약 5~6번 3연타
                    yield return new WaitGameSeconds(MontagePad);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 2.6f)) yield return step;
                    break;
                case Shot.Horse: // 대시(2.4칸) → 대시 끝 0.6초 이어쓰기 창 안에 두 번째 대시. 이동 입력 없음 = 조준 방향으로 대시
                    yield return new WaitGameSeconds(0.8f);
                    TrailerVirtualInput.PressDash();
                    yield return new WaitGameSeconds(0.32f);
                    TrailerVirtualInput.PressDash();
                    break;
                case Shot.Monkey: // 누르고 있으면 바나나를 연속으로 던진다(약 0.45초 간격)
                    yield return new WaitGameSeconds(0.8f);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 2.4f)) yield return step;
                    break;
                case Shot.Dragon: // 이동 없이 누르고 있으면 제자리 속사 브레스(틱 0.2초)
                    yield return new WaitGameSeconds(0.8f);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 2.8f)) yield return step;
                    break;
                case Shot.Rabbit: // 끝부분(3~4칸)에 선 토끼 몹에게 귀 채찍 두 번
                    yield return new WaitGameSeconds(0.7f);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 0.1f)) yield return step;
                    yield return new WaitGameSeconds(0.7f);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 0.1f)) yield return step;
                    break;
                case Shot.Snake: // 다가오는 몹이 반경 1.6에 들어올 즈음 휘두르기 → 둔화된 채 움직이는 모습을 여운에 담는다
                    yield return new WaitGameSeconds(0.9f);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 1.6f)) yield return step;
                    break;
                case Shot.Rat: // 같은 몹(쥐 1마리)에게 연속 휘두르기로 취약 스택(최대 5)을 쌓아 대미지가 커지는 것을 보인다
                    yield return new WaitGameSeconds(0.4f);
                    foreach (object step in Hold(TrailerVirtualInput.SetAttack, 3.0f)) yield return step;
                    break;
                case Shot.Pig: // 몹 쪽을 조준하고 가드 유지 — 근접 몹(호랑이 3연타)의 공격을 막을 때마다 반격(재사용 1초)
                    yield return new WaitGameSeconds(0.2f);
                    foreach (object step in Hold(TrailerVirtualInput.SetGuard, 3.3f)) yield return step;
                    break;
            }
        }

        private static IEnumerable Hold(Action<bool> set, float seconds)
        {
            set(true);
            yield return new WaitGameSeconds(seconds);
            set(false);
        }

        private static string MontageUnavailableReason(Shot shot)
        {
            if (FloorProgression.Instance == null)
            {
                return "FloorProgression 없음";
            }

            GameObject player = LocalPlayer;
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead)
            {
                return "플레이어가 쓰러져 있음";
            }

            if (player.GetComponent<AbsorptionController>() == null)
            {
                return "AbsorptionController 없음";
            }

            if (!IsMontage(shot) && !IsRoomShot(shot))
            {
                return null; // 보스·승리 샷은 ExtraUnavailableReason이 따로 본다
            }

            MontageSpec spec = Spec(shot);
            if (spec.Animal != null && TraitOf(spec.Animal) == null)
            {
                return $"동물 '{spec.Animal}' 항목·특성 없음";
            }

            if (spec.Element.HasValue && player.GetComponent<MainAbilityController>() == null)
            {
                return "MainAbilityController 없음";
            }

            foreach (Mob mob in spec.Mobs)
            {
                if (DebugMenuController.FindAnimal(mob.Animal) == null)
                {
                    return $"소환할 동물 '{mob.Animal}' 항목 없음";
                }
            }

            return null;
        }

        private static PassiveTraitData TraitOf(string animal)
        {
            FloorProgression.AnimalEntry entry = DebugMenuController.FindAnimal(animal);
            MonsterStub stub = entry != null && entry.normalPrefab != null ? entry.normalPrefab.GetComponent<MonsterStub>() : null;
            return stub != null ? stub.TraitData : null;
        }

        /// <summary>몽타주 샷 하나를 takes번 촬영한다(파일: &lt;샷&gt;_t&lt;테이크&gt;_&lt;시각&gt;.mp4). 넓은 빈 방 가운데에 선 채로 실행한다.</summary>
        public static void RunMontage(Shot shot, int takes = DefaultTakes)
        {
            StartMontage(new[] { shot }, takes);
        }

        /// <summary>몽타주 9샷(2-1~2-9)을 각각 takes번씩 이어서 촬영한다. 모든 테이크는 실행 시점의 플레이어 위치에서 시작한다.</summary>
        public static void RunMontageAll(int takes = DefaultTakes)
        {
            StartMontage(MontageShots, takes);
        }

        private static void StartMontage(Shot[] shots, int takes)
        {
            foreach (Shot shot in shots)
            {
                if (!IsMontage(shot))
                {
                    Debug.LogError($"[Trailer] {ShotCode(shot)}은 몽타주 샷이 아니다 — Run을 쓴다.");
                    return;
                }

                string reason = CannotRunReason(shot);
                if (reason != null)
                {
                    Debug.LogError($"[Trailer] {ShotCode(shot)} 실행 불가: {reason}");
                    return;
                }
            }

            takes = Mathf.Clamp(takes, 1, 20);
            RunningName = shots.Length == 1
                ? $"{ShotCode(shots[0])} {ShotTitle(shots[0])} ×{takes}"
                : $"몽타주 전부 ({shots.Length}샷 ×{takes})";
            Status = "시작";
            _current = null;
            _routine = MontageSequence(shots, takes);
            Debug.Log($"[Trailer] 시퀀스 시작: {RunningName}");
        }

        private static IEnumerator MontageSequence(Shot[] shots, int takes)
        {
            EnableInvincible();
            DebugMenuController.CloseIfOpen();
            if (!TrailerVirtualInput.Begin())
            {
                throw new InvalidOperationException("가상 입력을 붙이지 못했다.");
            }

            Vector2 origin = LocalPlayer.transform.position;
            var files = new List<string>();
            foreach (Shot shot in shots)
            {
                MontageSpec spec = Spec(shot);
                Vector2 forward = FindMontageDirection(origin, spec);
                for (int take = 1; take <= takes; take++)
                {
                    Status = $"{ShotCode(shot)} 테이크 {take}/{takes} 준비";
                    foreach (object step in MontageTake(shot, spec, take, takes, origin, forward))
                    {
                        yield return step;
                    }

                    files.Add(System.IO.Path.GetFileName(TrailerRecorder.LastFilePath));
                }
            }

            Debug.Log($"[Trailer] 몽타주 파일 {files.Count}개: {string.Join(", ", files)}");
        }

        private static IEnumerable MontageTake(Shot shot, MontageSpec spec, int take, int takes, Vector2 origin, Vector2 forward)
        {
            GameObject player = LocalPlayer;
            if (player == null)
            {
                throw new InvalidOperationException("로컬 플레이어가 없다.");
            }

            // 상태 초기화: 입력 떼기 → 이전 몹 제거 → 시작 위치 → 특성
            TrailerVirtualInput.ReleaseAll();
            DebugMenuController.DestroySpawned(_montageSpawned);
            DebugMenuController.Teleport(player, origin);
            yield return new WaitGameSeconds(0.3f);

            var absorption = player.GetComponent<AbsorptionController>();
            if (spec.ClearTrait)
            {
                if (absorption.CurrentTrait != null)
                {
                    absorption.DebugClearTrait();
                    yield return new WaitGameSeconds(0.3f); // 탈 벗는 연출이 녹화 전에 지나가게
                }
            }
            else if (!(spec.KeepTrait && absorption.CurrentTrait != null))
            {
                PassiveTraitData trait = TraitOf(spec.Animal);
                if (absorption.CurrentTrait != trait)
                {
                    absorption.DebugGrantTrait(trait);
                    yield return new WaitGameSeconds(0.3f); // 탈 바뀌는 연출이 녹화 전에 지나가게
                }
            }

            if (spec.ResetAbsorb)
            {
                // 같은 방 흡수 1회 제한(HasAbsorbedThisRoom)은 기존 공개 경로 ResetRoomAbsorption으로 풀고(방 입장 트리거와 같은 호출),
                // 흡수 재사용 대기(absorbCooldown 1.5초)는 마지막 흡수 뒤 시간을 기다린다.
                absorption.ResetRoomAbsorption();
                if (Time.time < _absorbReadyTime)
                {
                    yield return new WaitGameSeconds(_absorbReadyTime - Time.time);
                }
            }

            // 디버그 무적은 PlayerHealth에서 가드 판정보다 먼저 대미지를 버려 PlayerGuard → PigCounterSkill 반격이 안 불리고, 피격 특성 방출도
            // 안 일어난다. 그래서 2-9 돼지·3-2 방출만 무적을 끄고 체력을 채워 둔다(원래 값은 Finish의 RestoreInvincible이 복구).
            SetMontageInvincible(!spec.Vulnerable);
            if (spec.Vulnerable)
            {
                _invincibleTarget?.DebugHealFull();
            }

            if (spec.Element.HasValue)
            {
                ApplyShotElement(player, spec.Element.Value);
            }

            TrailerVirtualInput.SetAimDirection(player, forward);
            SpawnMobs(spec, origin, forward, player.transform.position.z, spec.Freeze, spec.Aggro);
            yield return new WaitGameSeconds(0.15f);

            Status = $"{ShotCode(shot)} 테이크 {take}/{takes} 녹화";
            if (!TrailerRecorder.StartRecording(ShotCode(shot), take))
            {
                throw new InvalidOperationException("녹화 시작 실패."); // Tick의 catch가 Abort한다
            }

            yield return null; // 첫 프레임이 녹화되도록 한 프레임 넘긴다

            float start = Time.time;
            float total = spec.Length + MontagePad * 2f;
            foreach (object step in Script(shot, player, forward))
            {
                yield return step;
            }

            float remaining = total - (Time.time - start);
            if (remaining > 0f)
            {
                yield return new WaitGameSeconds(remaining);
            }

            TrailerVirtualInput.ReleaseAll();
            TrailerRecorder.StopRecording();
            DebugMenuController.DestroySpawned(_montageSpawned);
            SetMontageInvincible(true); // 3-2처럼 무적을 끈 샷도 녹화가 끝나면 바로 다시 켠다(원래 값은 Finish가 복구)
            RestoreElement();
            if (shot == Shot.Release)
            {
                HideDroppedTraits(origin); // 바닥에 떨어진 특성이 다음 샷 배경에 남지 않게 숨긴다(숨김 목록에 기억)
            }

            yield return new WaitGameSeconds(BetweenTakes);
        }

        // freeze: 긴 경직(ApplyStun)으로 몹이 제자리에서 움직이지도 공격하지도 않게 한다 — 넉백은 경직보다 먼저 처리돼 그대로 보인다.
        // 2-9 돼지는 막을 공격이 필요해 freeze=false. aggro: 소환 직후 바로 플레이어를 쫓게(3-2 — 방 입장 때와 같은 Aggro 호출).
        private static void SpawnMobs(MontageSpec spec, Vector2 origin, Vector2 forward, float z, bool freeze, bool aggro)
        {
            Vector2 side = new Vector2(-forward.y, forward.x);
            foreach (Mob mob in spec.Mobs)
            {
                FloorProgression.AnimalEntry entry = DebugMenuController.FindAnimal(mob.Animal);
                GameObject prefab = entry.normalPrefab; // 트레일러는 일반 몹만(정예 X)
                Vector2 p = origin + forward * mob.Forward + side * mob.Side;
                GameObject spawned = DebugMenuController.SpawnMonsterAt(prefab, new Vector3(p.x, p.y, z));
                _montageSpawned.Add(spawned);
                if (freeze && spawned != null)
                {
                    spawned.GetComponent<MonsterBehaviorController>()?.ApplyStun(999f);
                }
                else if (aggro && spawned != null)
                {
                    spawned.GetComponent<MonsterBehaviorController>()?.Aggro();
                }
            }
        }

        // 조준 방향 후보(가로 우선 — 화면이 16:9이고 탈 옆모습이 잘 보임) 중 몹 위치·이동 경로에 벽이 없는 첫 방향.
        private static Vector2 FindMontageDirection(Vector2 origin, MontageSpec spec)
        {
            Vector2[] candidates =
            {
                Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1f, 1f).normalized, new Vector2(-1f, 1f).normalized,
                new Vector2(1f, -1f).normalized, new Vector2(-1f, -1f).normalized,
            };

            Physics2D.SyncTransforms();
            float clearance = spec.Clearance;
            foreach (Mob mob in spec.Mobs)
            {
                clearance = Mathf.Max(clearance, mob.Forward + 0.6f);
            }

            foreach (Vector2 forward in candidates)
            {
                if (IsLayoutClear(origin, forward, spec, clearance))
                {
                    return forward;
                }
            }

            throw new InvalidOperationException(
                $"플레이어 주변에 {clearance:0.0}칸 이상 빈 방향이 없다 — 넓은 빈 방 가운데에 서서 다시 실행한다.");
        }

        private static bool IsLayoutClear(Vector2 origin, Vector2 forward, MontageSpec spec, float clearance)
        {
            Vector2 side = new Vector2(-forward.y, forward.x);
            // 이동 경로(돌진·대시)와 몸 폭: 가운데와 좌우 0.35칸 평행선
            foreach (float offset in new[] { 0f, 0.35f, -0.35f })
            {
                Vector2 a = origin + side * offset;
                if (IsPathBlocked(a, a + forward * clearance))
                {
                    return false;
                }
            }

            foreach (Mob mob in spec.Mobs)
            {
                Vector2 p = origin + forward * mob.Forward + side * mob.Side;
                if (IsPointSolid(p) || IsPathBlocked(origin, p))
                {
                    return false;
                }
            }

            return true;
        }

        // 벽 판정: 트리거가 아니고 리지드바디가 없거나 Static인 콜라이더(몬스터·플레이어 같은 움직이는 몸은 제외).
        private static bool IsSolid(Collider2D col) =>
            col != null && !col.isTrigger && (col.attachedRigidbody == null || col.attachedRigidbody.bodyType == RigidbodyType2D.Static);

        private static bool IsPointSolid(Vector2 point)
        {
            foreach (Collider2D col in Physics2D.OverlapPointAll(point))
            {
                if (IsSolid(col))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPathBlocked(Vector2 a, Vector2 b)
        {
            foreach (RaycastHit2D hit in Physics2D.LinecastAll(a, b))
            {
                if (IsSolid(hit.collider))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CleanupMontage()
        {
            TrailerVirtualInput.Remove(); // ReleaseAll + 장치 제거 + 실제 마우스·포커스 설정 복구(붙어 있지 않으면 아무것도 안 함)
            if (NetworkServer.active)
            {
                DebugMenuController.DestroySpawned(_montageSpawned);
            }
            else
            {
                _montageSpawned.Clear();
            }
        }

        // ── 2차 자동 조작 샷(1-1·1-2/1-3·3-1·3-2·5-2·6-2·6-5·7-1, 2026-10-19) ─────────────
        // 일반 방 샷(1-1~5-2)은 몽타주와 같은 테이크 흐름(MontageTake — Spec·Script에 대본). 시작 전에 촬영 준비(CleanRoomSteps)로
        // 보스 방이 아닌 가장 넓은 방 가운데에 서서 방 몹을 처치하고 주변 몬스터·시체를 숨긴다. 보스 샷(6-2·6-5)·승리(7-1)는 전용 흐름.
        // 정리: Finish가 가상 입력·소환 몹·속성·무적을 복구하고, 중단(Abort)이면 숨긴 몬스터·시체도 되돌린다.

        private const float HideRadius = 20f;          // 촬영 준비: 플레이어 주변 이 거리 안 몬스터·시체를 숨긴다
        private const float SpawnWaitTimeout = 5f;     // 촬영 준비: 방 입장(몹 생성)·처치 후 방 열림 대기
        private const int MaxBeatSwings = 4;           // 처치 직전까지 휘두르는 최대 횟수
        private const float AbsorbHold = 0.75f;        // 흡수 키 누르는 시간(PlayerControls Absorb Hold 0.5초 + 여유)
        private const float AbsorbCooldownWait = 1.6f; // 흡수 재사용 대기(AbsorptionController absorbCooldown 1.5초) + 여유
        private const float ReleaseTimeout = 6f;       // 3-2: 피격으로 특성이 벗겨질 때까지 최대 대기
        private const float DodgeStartDistance = 4.5f; // 6-2: 보스와 떨어져 설 거리
        private const float DodgeDashLead = 0.22f;     // 6-2: 예고가 끝나기 이만큼 전에 대시(대시 0.2초 = 2.4칸)
        private const float DodgeClearance = 3f;       // 6-2: 회피 방향으로 벽이 없어야 하는 거리
        private const float ExitStandRadius = 1.5f;    // 7-1: FloorExit '출구 위에 섰다' 반경(FloorExit.StandRadius와 같은 값)

        // BossPatternRush.Runtime 단계 번호(예고 0 · 돌진 1 · 재조준 2)
        private const int RushStepFirstAim = 0;
        private const int RushStepCharge = 1;
        private const int RushStepReaim = 2;

        // 6-2 회피 타이밍: BossBrain의 동기화 필드(예고 표시용 state·patternIndex·patternStep)를 읽기만 한다. 게임 코드는 그대로.
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo BrainStateField = typeof(BossBrain).GetField("state", PrivateInstance);
        private static readonly FieldInfo BrainIndexField = typeof(BossBrain).GetField("patternIndex", PrivateInstance);
        private static readonly FieldInfo BrainStepField = typeof(BossBrain).GetField("patternStep", PrivateInstance);

        private static readonly List<GameObject> _hidden = new List<GameObject>();
        private static float _absorbReadyTime;
        private static MainAbilityController _elementTarget;
        private static ElementType _originalElement;
        private static bool _elementChanged;

        /// <summary>촬영 준비가 숨겨 둔 오브젝트 수(창 표시용).</summary>
        public static int HiddenCount
        {
            get
            {
                _hidden.RemoveAll(go => go == null);
                return _hidden.Count;
            }
        }

        private static string ExtraUnavailableReason(Shot shot)
        {
            if (FloorProgression.Instance == null)
            {
                return "FloorProgression 없음";
            }

            if (RunEndScreenController.IsShown)
            {
                return "판 종료 화면이 떠 있음";
            }

            GameObject player = LocalPlayer;
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead)
            {
                return "플레이어가 쓰러져 있음";
            }

            if (player.GetComponent<AbsorptionController>() == null)
            {
                return "AbsorptionController 없음";
            }

            if (IsRoomShot(shot))
            {
                string reason = MontageUnavailableReason(shot);
                if (reason != null)
                {
                    return reason;
                }

                return FindLargestRoom() == null ? "보스 방이 아닌 일반 방 없음" : null;
            }

            switch (shot)
            {
                case Shot.BossDodge:
                case Shot.BossFinish:
                    return DebugMenuController.FindAliveBossAttack() != null ? null : BossRoomUnavailableReason();
                case Shot.Victory:
                    // 마지막 층이 아니면 AdvanceFloor로 넘어간다. 마지막 층이면 보스 방(또는 이미 열린 포탈)이 있어야 한다.
                    if (FloorProgression.Instance.IsLastFloor && DebugMenuController.FindBossRoom() == null && FindFloorExit() == null)
                    {
                        return "보스 방 없음";
                    }

                    return null;
            }

            return null;
        }

        // ── 촬영 준비 ─────────────────────────────────────────────

        /// <summary>촬영 준비를 지금 할 수 없는 이유(가능하면 null). 창 버튼 활성/툴팁용.</summary>
        public static string CannotPrepareReason()
        {
            if (IsRunning)
            {
                return "다른 시퀀스 실행 중";
            }

            if (TrailerRecorder.IsRecording)
            {
                return "수동 녹화 중";
            }

            if (!IsHostReady)
            {
                return "Play 중 + 호스트 + 로컬 플레이어 필요";
            }

            return FindLargestRoom() == null ? "보스 방이 아닌 일반 방 없음" : null;
        }

        /// <summary>
        /// 촬영 준비만 단독으로 한다(MCP·창 버튼): 보스 방을 뺀 가장 넓은 방 가운데(벽 피해 보정)로 이동 → 방 몹 처치(DebugKillAllEnemies)
        /// → 방 열림 대기 → 플레이어 반경 20칸 안 몬스터·시체(정예 시체 포함)를 SetActive(false)로 숨김. 몽타주(RunMontage·RunMontageAll)
        /// 전에도 부를 수 있다. 숨긴 것은 정상 완료면 그대로 두고, 중단되면 되돌린다(RestoreHidden으로 직접 되돌릴 수도 있음).
        /// </summary>
        public static void PrepareCleanRoom()
        {
            string reason = CannotPrepareReason();
            if (reason != null)
            {
                Debug.LogError($"[Trailer] 촬영 준비 실행 불가: {reason}");
                return;
            }

            RunningName = "촬영 준비(넓은 방·주변 정리)";
            Status = "시작";
            _current = null;
            _routine = CleanRoomSequence();
            Debug.Log($"[Trailer] 시퀀스 시작: {RunningName}");
        }

        /// <summary>촬영 준비가 숨긴 몬스터·시체를 다시 보이게 한다(이미 사라진 것은 건너뜀). 실행 중이 아닐 때 MCP로 불러도 된다.</summary>
        public static void RestoreHidden()
        {
            int restored = 0;
            foreach (GameObject go in _hidden)
            {
                if (go != null && !go.activeSelf)
                {
                    go.SetActive(true);
                    restored++;
                }
            }

            _hidden.Clear();
            if (restored > 0)
            {
                Debug.Log($"[Trailer] 숨긴 오브젝트 {restored}개를 되돌렸다.");
            }
        }

        private static IEnumerator CleanRoomSequence()
        {
            EnableInvincible();
            DebugMenuController.CloseIfOpen();
            foreach (object step in CleanRoomSteps())
            {
                yield return step;
            }
        }

        private static IEnumerable CleanRoomSteps()
        {
            GameObject player = LocalPlayer;
            if (player == null)
            {
                throw new InvalidOperationException("로컬 플레이어가 없다.");
            }

            RoomClearController room = FindLargestRoom();
            if (room == null)
            {
                throw new InvalidOperationException("보스 방이 아닌 일반 방이 없다.");
            }

            Vector2 center = FindRoomCenter(room);
            Status = "촬영 준비: 넓은 방 이동";
            DebugMenuController.Teleport(player, center);

            // 방 입장 트리거(RoomEnterTrigger)가 몹을 생성하면 방이 잠기고, 몹이 없던 방은 바로 클리어된다.
            yield return new WaitCondition(() => room.IsLocked || room.IsCleared, SpawnWaitTimeout,
                "넓은 방에 들어갔는데 몹 생성(방 잠김)·클리어가 일어나지 않았다(방 입장 트리거 확인).");

            if (room.IsLocked)
            {
                Status = "촬영 준비: 방 몹 처치";
                yield return new WaitSeconds(0.2f); // 생성 직후 한 박자
                room.DebugKillAllEnemies(); // 잡몹은 처치, 정예는 기존 규칙대로 시체로 남는다(아래에서 숨김)
                yield return new WaitCondition(() => !room.IsLocked, SpawnWaitTimeout, "방 몹을 처치했는데 방이 열리지 않았다.");
            }

            yield return new WaitSeconds(0.8f); // 처치 잔상(디졸브)이 지나가게

            Status = "촬영 준비: 주변 몬스터·시체 숨김";
            int hidden = HideNearby((Vector2)player.transform.position, HideRadius);
            Debug.Log($"[Trailer] 촬영 준비 완료: {room.name} 가운데 {center}, 반경 {HideRadius:0}칸 안 몬스터·시체 {hidden}개 숨김.");
            yield return new WaitSeconds(0.3f);
        }

        // 보스 방을 뺀 방 중 입장 트리거(RoomArea) 경계 면적이 가장 큰 방.
        private static RoomClearController FindLargestRoom()
        {
            RoomClearController bossRoom = DebugMenuController.FindBossRoom();
            RoomClearController best = null;
            float bestArea = 0f;
            foreach (RoomClearController room in UnityEngine.Object.FindObjectsByType<RoomClearController>(FindObjectsSortMode.None))
            {
                if (room.RoomArea == null || room == bossRoom || room.IsBossRoom)
                {
                    continue;
                }

                Vector3 size = room.RoomArea.bounds.size;
                float area = size.x * size.y;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = room;
                }
            }

            return best;
        }

        // 방 경계 중심이 벽·장식(정적 콜라이더)과 겹치면 가까운 빈 점으로 옮긴다(0.5칸씩 바깥으로 16방향).
        private static Vector2 FindRoomCenter(RoomClearController room)
        {
            Physics2D.SyncTransforms();
            Bounds b = room.RoomArea.bounds;
            Vector2 center = b.center;
            float maxRadius = Mathf.Max(b.extents.x, b.extents.y);
            for (float r = 0f; r <= maxRadius; r += 0.5f)
            {
                int steps = r <= 0f ? 1 : 16;
                for (int i = 0; i < steps; i++)
                {
                    float angle = i * Mathf.PI * 2f / 16f;
                    Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                    if (IsOpenPoint(room, p))
                    {
                        return p;
                    }
                }
            }

            throw new InvalidOperationException($"{room.name} 안에서 벽과 안 겹치는 빈 지점을 찾지 못했다.");
        }

        // 방 안이고, 점과 몸 폭(±0.4칸)이 벽 등 정적 콜라이더와 겹치지 않는지.
        private static bool IsOpenPoint(RoomClearController room, Vector2 p)
        {
            if (!room.RoomArea.OverlapPoint(p))
            {
                return false;
            }

            foreach (Vector2 offset in new[] { Vector2.zero, new Vector2(0.4f, 0f), new Vector2(-0.4f, 0f), new Vector2(0f, 0.4f), new Vector2(0f, -0.4f) })
            {
                if (DebugMenuController.OverlapsSolid(p + offset) || IsPointSolid(p + offset))
                {
                    return false;
                }
            }

            return true;
        }

        // 몬스터(살아 있는 것·정예 시체)를 SetActive(false)로 숨긴다 — NetworkServer.Destroy는 쓰지 않는다(정예 시체 파괴 시 Mirror OnStopServer 예외).
        private static int HideNearby(Vector2 center, float radius)
        {
            int count = 0;
            foreach (MonsterStub monster in UnityEngine.Object.FindObjectsByType<MonsterStub>(FindObjectsSortMode.None))
            {
                GameObject go = monster.gameObject;
                if (_montageSpawned.Contains(go) || Vector2.Distance(go.transform.position, center) > radius)
                {
                    continue;
                }

                go.SetActive(false);
                _hidden.Add(go);
                count++;
            }

            return count;
        }

        // 3-2 뒤 바닥에 떨어진 특성(DroppedTrait)도 같은 방식으로 숨긴다.
        private static void HideDroppedTraits(Vector2 center)
        {
            foreach (DroppedTrait dropped in UnityEngine.Object.FindObjectsByType<DroppedTrait>(FindObjectsSortMode.None))
            {
                if (Vector2.Distance(dropped.transform.position, center) <= HideRadius)
                {
                    dropped.gameObject.SetActive(false);
                    _hidden.Add(dropped.gameObject);
                }
            }
        }

        // ── 속성(5-2) ─────────────────────────────────────────────

        private static void ApplyShotElement(GameObject player, ElementType element)
        {
            var ability = player.GetComponent<MainAbilityController>();
            if (ability == null)
            {
                return;
            }

            if (!_elementChanged)
            {
                _elementTarget = ability;
                _originalElement = ability.CurrentElement;
                _elementChanged = true;
            }

            if (ability.CurrentElement != element)
            {
                ability.ApplyElement(element);
            }
        }

        private static void RestoreElement()
        {
            if (!_elementChanged)
            {
                return;
            }

            _elementChanged = false;
            if (_elementTarget != null && NetworkServer.active && _elementTarget.CurrentElement != _originalElement)
            {
                _elementTarget.ApplyElement(_originalElement);
            }

            _elementTarget = null;
        }

        // ── 일반 방 샷 대본 도우미 ─────────────────────────────────

        private static MonsterStub FirstSpawnedMob()
        {
            foreach (GameObject go in _montageSpawned)
            {
                if (go != null)
                {
                    return go.GetComponent<MonsterStub>();
                }
            }

            return null;
        }

        // 잡몹은 살아 있을 때만 흡수된다(AbsorptionController.FindNearestAbsorbableMonster — 죽으면 바로 사라짐). 그래서 '쓰러질 때까지'는
        // 한 대 더 치면 쓰러지는 데까지 휘두르고(첫 타 오버킬 보호로 한 방이면 체력 1이 남음) 흡수한다. 넉백으로 밀렸으면 흡수 반경 안으로 걸어간다.
        private static IEnumerable BeatDownAndAbsorb(GameObject player)
        {
            MonsterStub mob = FirstSpawnedMob();
            if (mob == null)
            {
                Debug.LogWarning("[Trailer] 때릴 몹이 없다(소환 실패).");
                yield break;
            }

            var absorption = player.GetComponent<AbsorptionController>();
            int lastDrop = 0;
            for (int swing = 0; swing < MaxBeatSwings; swing++)
            {
                int before = mob.CurrentHealth;
                foreach (object step in Hold(TrailerVirtualInput.SetAttack, 0.1f)) yield return step;
                yield return new WaitGameSeconds(0.55f); // 스윙(호랑이 3연타 포함)이 끝나고 체력이 줄 시간
                if (mob == null || mob.IsDead)
                {
                    break;
                }

                int drop = before - mob.CurrentHealth;
                if (drop > 0)
                {
                    lastDrop = drop;
                }

                if (lastDrop > 0 && mob.CurrentHealth <= lastDrop)
                {
                    break; // 한 대 더 치면 쓰러진다 — 여기서 흡수
                }
            }

            if (mob == null || mob.IsDead)
            {
                Debug.LogWarning("[Trailer] 몹이 먼저 쓰러져 흡수 장면을 못 찍었다 — 테이크를 다시 찍는다.");
                yield break;
            }

            float range = absorption.AbsorbRange - 0.2f;
            float until = Time.time + 1.5f;
            while (mob != null && Time.time < until)
            {
                Vector2 toMob = (Vector2)mob.transform.position - (Vector2)player.transform.position;
                if (toMob.magnitude <= range)
                {
                    break;
                }

                TrailerVirtualInput.SetMove(toMob.normalized);
                yield return null;
            }

            TrailerVirtualInput.SetMove(Vector2.zero);
            yield return new WaitGameSeconds(0.1f);
            foreach (object step in Hold(TrailerVirtualInput.SetAbsorb, AbsorbHold)) yield return step;
            _absorbReadyTime = Time.time + AbsorbCooldownWait;
            yield return new WaitGameSeconds(0.2f);
            if (mob != null)
            {
                Debug.LogWarning("[Trailer] 흡수 홀드를 했는데 몹이 흡수되지 않았다(흡수 반경·방당 1회 제한·재사용 대기 확인).");
            }
        }

        // 3-2: 근접 몹이 와서 때려 특성이 벗겨지는 순간(DropTraitOnHit)을 기다린다. 벗겨지면 즉시 무적을 다시 켜고 몹을 없앤다.
        private static IEnumerable ReleaseScript(GameObject player)
        {
            var absorption = player.GetComponent<AbsorptionController>();
            yield return new WaitGameSeconds(0.3f);
            float until = Time.time + ReleaseTimeout;
            while (absorption.CurrentTrait != null && Time.time < until)
            {
                yield return null;
            }

            bool dropped = absorption.CurrentTrait == null;
            SetMontageInvincible(true);
            DebugMenuController.DestroySpawned(_montageSpawned);
            if (!dropped)
            {
                Debug.LogWarning($"[Trailer] 3-2: {ReleaseTimeout:0}초 안에 피격 특성 방출이 없었다 — 몹이 안 때렸는지 확인하고 다시 찍는다.");
            }

            yield return new WaitGameSeconds(1.2f); // 떨어진 특성·탈 벗는 연출 여운
        }

        // 5-2: 첫 타 → (남은 체력 − 다음 타 − 화상 2스택 전체 대미지 ≥ 3일 때만) 둘째 타. 몹이 화상으로 죽지 않게 한다.
        private static IEnumerable BurnScript()
        {
            yield return new WaitGameSeconds(0.8f);
            MonsterStub mob = FirstSpawnedMob();
            if (mob == null)
            {
                Debug.LogWarning("[Trailer] 5-2: 때릴 몹이 없다(소환 실패).");
                yield break;
            }

            int before = mob.CurrentHealth;
            foreach (object step in Hold(TrailerVirtualInput.SetAttack, 0.1f)) yield return step;
            yield return new WaitGameSeconds(0.7f);
            if (mob == null || mob.IsDead)
            {
                yield break;
            }

            int hit = Mathf.Max(1, before - mob.CurrentHealth); // 첫 타 + 그동안의 화상(크게 잡을수록 안전)
            float burn = FireBurnTotal(mob, 2);
            if (mob.CurrentHealth - hit - burn >= 3f)
            {
                foreach (object step in Hold(TrailerVirtualInput.SetAttack, 0.1f)) yield return step;
            }
        }

        // 화상 stacks스택이 지속 시간 내내 줄 대미지 — 몹 StatusEffectReceiver의 StatusEffectConfig 에셋 값(없으면 코드 기본값 4/초·3초).
        private static float FireBurnTotal(MonsterStub mob, int stacks)
        {
            float dps = 4f;
            float duration = 3f;
            var receiver = mob.GetComponent<StatusEffectReceiver>();
            UnityEngine.Object config = receiver != null ? new SerializedObject(receiver).FindProperty("statusConfig")?.objectReferenceValue : null;
            if (config != null)
            {
                var so = new SerializedObject(config);
                dps = so.FindProperty("fireDpsPerStack")?.floatValue ?? dps;
                duration = so.FindProperty("fireDuration")?.floatValue ?? duration;
            }

            return stacks * dps * duration;
        }

        // ── 실행 진입점 ───────────────────────────────────────────

        /// <summary>
        /// 2차 샷 하나를 takes번 촬영한다(MCP에서 리플렉션 없이 호출 가능). 일반 방 샷(1-1·1-2·3-1·3-2·5-2)은 시작 전에 촬영 준비를 한다.
        /// 6-5·7-1은 보스 처치·판 종료라 항상 1테이크.
        /// </summary>
        public static void RunExtra(Shot shot, int takes = DefaultTakes)
        {
            StartExtra(new[] { shot }, takes);
        }

        /// <summary>
        /// 2차 샷 전부: 촬영 준비 → 1-1 → 1-2/1-3 → 3-1 → 3-2 → 5-2 → 6-2 → 6-5 → 7-1. 보스·승리 샷은 그 시점에 못 찍으면(보스 이미 처치 등)
        /// 경고하고 건너뛴다. 7-1에서 판이 끝난다.
        /// </summary>
        public static void RunExtraAll(int takes = DefaultTakes)
        {
            StartExtra(ExtraShots, takes);
        }

        private static void StartExtra(Shot[] shots, int takes)
        {
            bool all = shots.Length > 1;
            foreach (Shot shot in shots)
            {
                if (!IsExtra(shot))
                {
                    Debug.LogError($"[Trailer] {ShotCode(shot)}은 2차 샷이 아니다 — Run·RunMontage를 쓴다.");
                    return;
                }

                string reason = CannotRunReason(shot);
                if (reason == null)
                {
                    continue;
                }

                if (all && !IsRoomShot(shot) && !IsRunning && !TrailerRecorder.IsRecording)
                {
                    Debug.LogWarning($"[Trailer] {ShotCode(shot)}은 지금 못 찍어 건너뛸 예정: {reason}");
                    continue;
                }

                Debug.LogError($"[Trailer] {ShotCode(shot)} 실행 불가: {reason}");
                return;
            }

            takes = Mathf.Clamp(takes, 1, 20);
            RunningName = shots.Length == 1
                ? $"{ShotCode(shots[0])} {ShotTitle(shots[0])} ×{takes}"
                : $"2차 샷 전부 ({shots.Length}샷 ×{takes})";
            Status = "시작";
            _current = null;
            _absorbReadyTime = 0f;
            _routine = ExtraSequence(shots, takes);
            Debug.Log($"[Trailer] 시퀀스 시작: {RunningName}");
        }

        private static IEnumerator ExtraSequence(Shot[] shots, int takes)
        {
            EnableInvincible();
            DebugMenuController.CloseIfOpen();
            if (!TrailerVirtualInput.Begin())
            {
                throw new InvalidOperationException("가상 입력을 붙이지 못했다.");
            }

            Vector2 origin = LocalPlayer.transform.position;
            if (Array.Exists(shots, IsRoomShot))
            {
                foreach (object step in CleanRoomSteps())
                {
                    yield return step;
                }

                origin = LocalPlayer.transform.position;
            }

            var files = new List<string>();
            foreach (Shot shot in shots)
            {
                string reason = ShotUnavailableReason(shot);
                if (reason != null)
                {
                    Debug.LogWarning($"[Trailer] {ShotCode(shot)} 건너뜀: {reason}");
                    continue;
                }

                int shotTakes = shot == Shot.BossFinish || shot == Shot.Victory ? 1 : takes;
                MontageSpec spec = IsRoomShot(shot) ? Spec(shot) : null;
                Vector2 forward = spec != null ? FindMontageDirection(origin, spec) : Vector2.right;
                for (int take = 1; take <= shotTakes; take++)
                {
                    Status = $"{ShotCode(shot)} 테이크 {take}/{shotTakes} 준비";
                    IEnumerable steps;
                    switch (shot)
                    {
                        case Shot.BossDodge: steps = BossDodgeTake(take, shotTakes); break;
                        case Shot.BossFinish: steps = BossFinishTake(); break;
                        case Shot.Victory: steps = VictoryTake(); break;
                        default: steps = MontageTake(shot, spec, take, shotTakes, origin, forward); break;
                    }

                    foreach (object step in steps)
                    {
                        yield return step;
                    }

                    files.Add(System.IO.Path.GetFileName(TrailerRecorder.LastFilePath));
                }
            }

            Debug.Log($"[Trailer] 2차 샷 파일 {files.Count}개: {string.Join(", ", files)}");
        }

        // ── 6-2 연속 돌진 회피 ──────────────────────────────────────

        private static IEnumerable BossDodgeTake(int take, int takes)
        {
            GameObject player = LocalPlayer;
            TrailerVirtualInput.ReleaseAll();
            foreach (object step in EnsureBoss())
            {
                yield return step;
            }

            SetMontageInvincible(true);
            BossBrain brain = DebugMenuController.FindAliveBossAttack();
            if (brain == null)
            {
                throw new InvalidOperationException("살아 있는 보스가 없다.");
            }

            Vector2 boss = brain.Body != null ? brain.Body.position : (Vector2)brain.transform.position;
            Vector2 spot = FindSafePoint(DebugMenuController.FindBossRoom(), boss, DodgeStartDistance);
            DebugMenuController.Teleport(player, spot);
            yield return new WaitGameSeconds(0.5f);
            TrailerVirtualInput.SetAimDirection(player, boss - spot);

            Status = $"6-2 테이크 {take}/{takes} 녹화";
            if (!TrailerRecorder.StartRecording(ShotCode(Shot.BossDodge), take))
            {
                throw new InvalidOperationException("녹화 시작 실패.");
            }

            yield return null;
            float start = Time.time;
            yield return new WaitGameSeconds(MontagePad);
            DebugMenuController.UseBossPattern("C"); // 쿨·페이즈 무시, 다른 패턴 중이면 다음 Idle에 시작
            foreach (object step in DodgeRush(brain, player))
            {
                yield return step;
            }

            float remaining = 3f + MontagePad * 2f - (Time.time - start);
            if (remaining > 0f)
            {
                yield return new WaitGameSeconds(remaining);
            }

            TrailerVirtualInput.ReleaseAll();
            TrailerRecorder.StopRecording();
            yield return new WaitGameSeconds(BetweenTakes);
        }

        // 돌진마다: 예고(첫 rushFirstTelegraphDuration · 재조준 rushReaimDuration — 패턴 에셋 값)가 시작되면 돌진 방향의 수직으로 걷기 시작 →
        // 예고가 끝나기 DodgeDashLead초 전에 대시 → 돌진이 끝날 때까지 기다림. rushCount번 반복.
        private static IEnumerable DodgeRush(BossBrain brain, GameObject player)
        {
            int rushIndex = brain.FindPatternIndex("C");
            if (rushIndex < 0)
            {
                throw new InvalidOperationException("보스 패턴 세트에 패턴 C(연속 돌진)가 없다.");
            }

            float firstTelegraph = ReadPatternProperty(brain, "C", "rushFirstTelegraphDuration").floatValue;
            float reaimTelegraph = ReadPatternProperty(brain, "C", "rushReaimDuration").floatValue;
            int count = ReadPatternProperty(brain, "C", "rushCount").intValue;
            for (int i = 0; i < count; i++)
            {
                int telegraph = i == 0 ? RushStepFirstAim : RushStepReaim;
                float deadline = Time.time + (i == 0 ? 4f : 2f);
                while (RushStep(brain, rushIndex) != telegraph && Time.time < deadline)
                {
                    yield return null;
                }

                if (RushStep(brain, rushIndex) != telegraph)
                {
                    Debug.LogWarning($"[Trailer] 6-2: 돌진 {i + 1}번째 예고를 찾지 못해 남은 회피를 건너뛴다.");
                    break;
                }

                float dashAt = Time.time + Mathf.Max(0.05f, (i == 0 ? firstTelegraph : reaimTelegraph) - DodgeDashLead);
                TrailerVirtualInput.SetMove(DodgeDirection(brain, player));
                while (Time.time < dashAt && RushStep(brain, rushIndex) == telegraph)
                {
                    yield return null;
                }

                TrailerVirtualInput.PressDash();
                deadline = Time.time + 1f;
                while (RushStep(brain, rushIndex) == telegraph && Time.time < deadline)
                {
                    yield return null; // 돌진 시작까지
                }

                yield return new WaitGameSeconds(0.15f);
                TrailerVirtualInput.SetMove(Vector2.zero);
                deadline = Time.time + 1.5f;
                while (RushStep(brain, rushIndex) == RushStepCharge && Time.time < deadline)
                {
                    yield return null; // 돌진 끝까지
                }
            }

            TrailerVirtualInput.SetMove(Vector2.zero);
        }

        // 보스가 패턴 C를 진행 중이면 그 단계(0 예고·1 돌진·2 재조준), 아니면 -1.
        private static int RushStep(BossBrain brain, int rushIndex)
        {
            if (BrainStateField == null || BrainIndexField == null || BrainStepField == null)
            {
                throw new InvalidOperationException("BossBrain 동기화 필드(state·patternIndex·patternStep)를 찾지 못해 6-2 회피 타이밍을 맞출 수 없다.");
            }

            if (brain == null)
            {
                return -1;
            }

            if (BrainStateField.GetValue(brain).ToString() != "Pattern" || Convert.ToInt32(BrainIndexField.GetValue(brain)) != rushIndex)
            {
                return -1;
            }

            return Convert.ToInt32(BrainStepField.GetValue(brain));
        }

        // 돌진 방향(BossBrain.Direction)의 수직 중 플레이어가 이미 치우친 쪽 — 그쪽에 벽이 있으면 반대쪽.
        private static Vector2 DodgeDirection(BossBrain brain, GameObject player)
        {
            Vector2 p = player.transform.position;
            Vector2 b = brain.Body != null ? brain.Body.position : (Vector2)brain.transform.position;
            Vector2 dir = brain.Direction.sqrMagnitude > 0.0001f ? brain.Direction.normalized : (p - b).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            if (Vector2.Dot(p - b, perp) < 0f)
            {
                perp = -perp;
            }

            Physics2D.SyncTransforms();
            if (IsPathBlocked(p, p + perp * DodgeClearance) && !IsPathBlocked(p, p - perp * DodgeClearance))
            {
                perp = -perp;
            }

            return perp;
        }

        // ── 6-5 보스 마지막 한 대 ───────────────────────────────────

        private static IEnumerable BossFinishTake()
        {
            GameObject player = LocalPlayer;
            TrailerVirtualInput.ReleaseAll();
            foreach (object step in EnsureBoss())
            {
                yield return step;
            }

            SetMontageInvincible(true);
            BossStub boss = DebugMenuController.FindAliveBoss();
            if (boss == null)
            {
                throw new InvalidOperationException("살아 있는 보스가 없다.");
            }

            Vector2 spot = FindBesideBoss(boss, player);
            DebugMenuController.Teleport(player, spot);
            yield return new WaitGameSeconds(0.4f);
            // 재생 상황 보스는 체력 1에서 바로 회복해 한 대로 안 쓰러진다 — 상황 특성을 없음으로 바꾼다.
            FloorProgression.Instance?.DebugSetBossSituation(BossSituation.None);
            boss.DebugSetHealthToOne();
            TrailerVirtualInput.SetAimDirection(player, (Vector2)boss.transform.position - spot);

            Status = "6-5 녹화";
            if (!TrailerRecorder.StartRecording(ShotCode(Shot.BossFinish), 1))
            {
                throw new InvalidOperationException("녹화 시작 실패.");
            }

            yield return null;
            float start = Time.time;
            yield return new WaitGameSeconds(MontagePad);
            TrailerVirtualInput.SetAimDirection(player, (Vector2)boss.transform.position - (Vector2)player.transform.position); // 그새 움직였을 수 있음
            TrailerVirtualInput.SetAttack(true);
            float until = Time.time + 1.5f;
            while (!boss.IsDefeated && Time.time < until)
            {
                yield return null;
            }

            TrailerVirtualInput.SetAttack(false);
            if (!boss.IsDefeated)
            {
                Debug.LogWarning("[Trailer] 6-5: 1.5초 동안 공격했는데 보스가 쓰러지지 않았다(사거리·조준 확인).");
            }

            // 길이 2초 + 앞뒤 여유 + 쓰러진 뒤 시체·출구 등장 여운 2초(흡수는 하지 않는다 — 흡수 선택 UI는 흡수 홀드 뒤에만 뜬다)
            float remaining = 2f + MontagePad * 2f + 2f - (Time.time - start);
            if (remaining > 0f)
            {
                yield return new WaitGameSeconds(remaining);
            }

            TrailerVirtualInput.ReleaseAll();
            TrailerRecorder.StopRecording();
        }

        // 보스 몸 콜라이더 가장자리에서 0.7칸 떨어진(대검 반경 1.6 안) 방 안 빈 지점. 플레이어가 있던 쪽부터 8방향.
        private static Vector2 FindBesideBoss(BossStub boss, GameObject player)
        {
            RoomClearController room = DebugMenuController.FindBossRoom();
            Physics2D.SyncTransforms();
            Vector2 b = boss.transform.position;
            float radius = 0.5f;
            foreach (Collider2D col in boss.GetComponents<Collider2D>())
            {
                if (!col.isTrigger)
                {
                    radius = Mathf.Max(radius, col.bounds.extents.x, col.bounds.extents.y);
                }
            }

            Vector2 from = (Vector2)player.transform.position - b;
            float baseAngle = from.sqrMagnitude > 0.0001f ? Mathf.Atan2(from.y, from.x) : 0f;
            for (int i = 0; i < 8; i++)
            {
                // 0, +45°, -45°, +90°, -90° … 순서로 플레이어 쪽에서 가까운 방향부터
                int k = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
                float angle = baseAngle + k * Mathf.PI / 4f;
                Vector2 p = b + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius + 0.7f);
                if ((room == null || room.RoomArea.OverlapPoint(p)) && !DebugMenuController.OverlapsSolid(p))
                {
                    return p;
                }
            }

            throw new InvalidOperationException("보스 옆에 설 빈 지점을 찾지 못했다.");
        }

        // ── 7-1 금빛 포탈 승리 ──────────────────────────────────────

        private static IEnumerable VictoryTake()
        {
            FloorProgression dungeon = FloorProgression.Instance;
            TrailerVirtualInput.ReleaseAll();
            while (!dungeon.IsLastFloor)
            {
                int before = dungeon.Floor;
                Status = $"7-1 준비: 다음 층 ({before} → {before + 1})";
                dungeon.AdvanceFloor();
                yield return new WaitCondition(() => dungeon.Floor != before && !DungeonGameManager.Instance.IsGenerating, GenerateTimeout,
                    "다음 층으로 넘어가지 않았다(층 생성 확인).");
                yield return new WaitSeconds(0.5f);
            }

            GameObject player = LocalPlayer;
            if (player == null)
            {
                throw new InvalidOperationException("로컬 플레이어가 없다.");
            }

            SetMontageInvincible(true);
            BossStub boss = DebugMenuController.FindAliveBoss();
            RoomClearController bossRoom = DebugMenuController.FindBossRoom();
            if (boss == null && (bossRoom == null || !bossRoom.IsCleared))
            {
                foreach (object step in EnsureBoss())
                {
                    yield return step;
                }

                boss = DebugMenuController.FindAliveBoss();
            }

            FloorExit exit = FindFloorExit();
            if (exit == null)
            {
                throw new InvalidOperationException("마지막 층 보스 방에서 출구(FloorExit)를 찾지 못했다.");
            }

            // 포탈이 열리는 순간 이미 반경 안에 서 있으면 녹화 전에 승리해 버리므로, 보스를 쓰러뜨리기 전에 포탈 앞(3.5칸)으로 옮긴다.
            Vector2 exitPos = exit.transform.position;
            Vector2 start = FindApproachPoint(exitPos, out Vector2 walkDir);
            DebugMenuController.Teleport(player, start);
            yield return new WaitGameSeconds(0.3f);

            if (boss != null && !boss.IsDefeated)
            {
                Status = "7-1 준비: 보스 처치";
                boss.DebugSetHealthToOne();
                boss.TakeDamage(1, player.GetComponent<NetworkIdentity>(), DamageSource.Weapon); // 기존 처치·시체·출구 흐름 그대로
            }

            var absorption = player.GetComponent<AbsorptionController>();
            Status = "7-1 준비: 금빛 포탈 열림 대기";
            yield return new WaitCondition(() => absorption.FloorExitReady, 6f, "보스를 쓰러뜨렸는데 금빛 포탈이 열리지 않았다(FloorExit 확인).");
            yield return new WaitGameSeconds(0.8f);
            TrailerVirtualInput.SetAimDirection(player, walkDir);

            Status = "7-1 녹화";
            if (!TrailerRecorder.StartRecording(ShotCode(Shot.Victory), 1))
            {
                throw new InvalidOperationException("녹화 시작 실패.");
            }

            yield return null;
            yield return new WaitGameSeconds(MontagePad);
            TrailerVirtualInput.SetMove(walkDir);
            float until = Time.time + 4f;
            while (!RunEndScreenController.IsShown && Time.time < until)
            {
                if (Vector2.Distance(player.transform.position, exitPos) <= 0.3f)
                {
                    TrailerVirtualInput.SetMove(Vector2.zero);
                }

                yield return null;
            }

            TrailerVirtualInput.SetMove(Vector2.zero);
            LogVictoryCondition(exitPos);
            if (!RunEndScreenController.IsShown)
            {
                Debug.LogWarning("[Trailer] 7-1: 포탈 위에 섰는데 승리 화면이 뜨지 않았다(살아 있는 전원이 반경 안인지·쓰러진 동료 확인).");
            }

            yield return new WaitGameSeconds(2.5f); // 승리 화면 시작 부분까지
            TrailerVirtualInput.ReleaseAll();
            TrailerRecorder.StopRecording();
        }

        // 마지막 층 보스 방의 출구. 보스 방 안의 것을 우선, 없으면 첫 번째.
        private static FloorExit FindFloorExit()
        {
            FloorExit[] exits = UnityEngine.Object.FindObjectsByType<FloorExit>(FindObjectsSortMode.None);
            RoomClearController room = DebugMenuController.FindBossRoom();
            foreach (FloorExit exit in exits)
            {
                if (room != null && room.RoomArea.OverlapPoint(exit.transform.position))
                {
                    return exit;
                }
            }

            return exits.Length > 0 ? exits[0] : null;
        }

        // 포탈에서 3.5칸(안 되면 3·2.6칸) 떨어진 8방향 점 중 방 안·벽 아님·포탈까지 걸어갈 길(벽·보스 몸)이 막히지 않은 첫 점.
        // 아래 → 왼쪽 → 오른쪽 → 위 → 대각선 순(아래에서 위로 걸어 들어가는 모습이 기본).
        private static Vector2 FindApproachPoint(Vector2 exit, out Vector2 walkDir)
        {
            RoomClearController room = DebugMenuController.FindBossRoom();
            Physics2D.SyncTransforms();
            Vector2[] directions =
            {
                Vector2.down, Vector2.left, Vector2.right, Vector2.up,
                new Vector2(-1f, -1f).normalized, new Vector2(1f, -1f).normalized,
                new Vector2(-1f, 1f).normalized, new Vector2(1f, 1f).normalized,
            };

            foreach (float distance in new[] { 3.5f, 3f, 2.6f })
            {
                foreach (Vector2 v in directions)
                {
                    Vector2 p = exit + v * distance;
                    if (room != null && !room.RoomArea.OverlapPoint(p))
                    {
                        continue;
                    }

                    if (DebugMenuController.OverlapsSolid(p) || IsPathBlocked(p, exit) || IsBlockedByBoss(p, exit))
                    {
                        continue;
                    }

                    walkDir = -v;
                    return p;
                }
            }

            throw new InvalidOperationException("금빛 포탈 앞에서 걸어 들어갈 빈 지점을 찾지 못했다.");
        }

        private static bool IsBlockedByBoss(Vector2 a, Vector2 b)
        {
            foreach (RaycastHit2D hit in Physics2D.LinecastAll(a, b))
            {
                if (hit.collider != null && !hit.collider.isTrigger && hit.collider.GetComponentInParent<BossStub>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        // FloorExit 승리 조건(살아 있는 전원이 출구 반경 안, 쓰러진 동료 없음)을 로그로 남긴다.
        private static void LogVictoryCondition(Vector2 exit)
        {
            int alive = 0;
            int standing = 0;
            foreach (PlayerController player in UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                PlayerHealth health = player.GetComponent<PlayerHealth>();
                if (health != null && health.IsDead)
                {
                    continue;
                }

                alive++;
                if (Vector2.Distance(player.transform.position, exit) <= ExitStandRadius)
                {
                    standing++;
                }
            }

            Debug.Log($"[Trailer] 7-1 승리 조건: 살아 있는 {alive}명 중 {standing}명이 포탈 반경 {ExitStandRadius} 안, 승리 화면 {(RunEndScreenController.IsShown ? "표시됨" : "없음")}.");
        }
    }
}
