using System.Collections.Generic;
using AbsorbRoguelike.Monster;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 방 클리어 조건(GDD "방 클리어 조건: 유한 스폰 + 처치 마릿수"): 플레이어가 몬스터가 남은 방에 들어오면
    /// 연결된 복도의 문을 닫고, 그 방 몬스터가 전부 죽거나 흡수되면(보스는 쓰러지면) 문을 연다.
    /// 한 번 클리어한 방은 다시 잠기지 않는다. 판정은 서버에서만 한다(ADR-003).
    /// 2026-09-30 2인 검증 1차 후속: 원격 클라이언트도 같은 시드로 레벨을 로컬 생성하고(LevelGenerator), 이 컴포넌트도
    /// 적 목록 없이 문 목록만 가진 채 붙는다. 잠김/해제는 서버가 <see cref="LockedChanged"/>로 알리고 RoomLockSync가
    /// SyncList(lockedRoomIndices)로 보내면, 클라이언트는 <see cref="ApplyLockedVisual"/>로 문 표시·콜라이더만 맞춘다.
    /// 클라이언트 쪽 인스턴스에서는 서버 로직(입장 판정·스폰·클리어)이 돌지 않는다 — Update·SpawnEnemiesOnce·DebugKillAllEnemies는
    /// NetworkServer.active 가드, OnPlayerEntered는 호출부(RoomEnterTrigger)가 서버 가드다.
    /// </summary>
    public class RoomClearController : MonoBehaviour
    {
        private const float EscapeCheckInterval = 1f;
        private const float StuckLogInterval = 5f;

        private readonly List<GameObject> _enemies = new List<GameObject>();
        private readonly List<GameObject> _doors = new List<GameObject>();
        private bool _locked;
        private bool _cleared;
        private bool _spawned;
        private Collider2D _roomArea;
        private float _escapeCheckTimer;
        private float _lockedTime;
        private float _nextStuckLog = StuckLogInterval;

        /// <summary>이 방이 클리어됐는지(서버 판정). 외부에서 읽기 전용으로 쓴다.</summary>
        public bool IsCleared => _cleared;

        /// <summary>방이 잠겨 있는지(서버 판정 — 적이 살아 있는 전투 중, 보스전 포함). Tab 지도 순간이동의 '전투 중' 거부 판정용(읽기 전용).</summary>
        public bool IsLocked => _locked;

        /// <summary>클리어되는 순간 한 번 발생한다(서버). 몬스터 없는 방의 즉시 클리어 경로 포함.</summary>
        public event System.Action Cleared;

        /// <summary>잠김(true)/해제(false)가 바뀔 때마다 발생한다(서버). RoomLockSync가 원격 클라이언트 문 동기화용으로 구독한다.</summary>
        public event System.Action<bool> LockedChanged;

        /// <summary>이 방 몬스터 중 보스가 있는지(보스 방). 디버그 메뉴 '보스 방으로' 이동용.</summary>
        public bool IsBossRoom
        {
            get
            {
                foreach (var enemy in _enemies)
                {
                    if (enemy != null && enemy.GetComponent<BossStub>() != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>방 입장 트리거 콜라이더(방 바닥 전체를 덮음). 없으면 null.</summary>
        public Collider2D RoomArea => _roomArea;

        /// <summary>이 방의 첫 보스 위치(비활성 대기 중이어도 배치 좌표는 있음). 보스 방이 아니면 null.</summary>
        public Vector2? BossPosition
        {
            get
            {
                foreach (var enemy in _enemies)
                {
                    if (enemy != null && enemy.GetComponent<BossStub>() != null)
                    {
                        return enemy.transform.position;
                    }
                }

                return null;
            }
        }

        /// <summary>점이 방 입장 트리거 안에 있는지. 디버그 메뉴 '현재 방 몬스터 전멸'이 플레이어가 있는 방을 찾을 때 쓴다.</summary>
        public bool ContainsPoint(Vector2 point)
        {
            return _roomArea != null && _roomArea.OverlapPoint(point);
        }

        /// <summary>
        /// 디버그 메뉴(F1): 이 방에 살아 있는 잡몹·정예몹을 전부 처치한다(서버). 정예는 기존 규칙대로 시체로 남고,
        /// 보스는 제외한다('보스 체력 1로' 버튼 전용). 클리어 판정·문 열기·Cleared 이벤트는 Update가 평소처럼 처리한다.
        /// </summary>
        public void DebugKillAllEnemies()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            int killed = 0;
            foreach (var enemy in _enemies)
            {
                if (enemy == null || !enemy.activeInHierarchy)
                {
                    continue; // 아직 입장 전(비활성 대기)인 몬스터는 건드리지 않는다
                }

                var monster = enemy.GetComponent<MonsterStub>();
                if (monster != null && !monster.IsDead)
                {
                    monster.TakeDamage(monster.CurrentHealth);
                    if (monster != null && !monster.IsDead)
                    {
                        // 잡몹 첫 타 오버킬 보호(체력 1 남김) 규칙은 그대로 두고, 한 번 더 쳐서 처치한다.
                        monster.TakeDamage(monster.CurrentHealth);
                    }

                    killed++;
                }
            }

            Debug.Log($"[RoomClear] {name}: debug killed {killed} enemies");
        }

        public void Initialize(IEnumerable<GameObject> enemies, IEnumerable<GameObject> doors)
        {
            _enemies.AddRange(enemies);
            _doors.AddRange(doors);

            var trigger = GetComponentInChildren<RoomEnterTrigger>();
            _roomArea = trigger != null ? trigger.GetComponent<Collider2D>() : null;
        }

        public void OnPlayerEntered()
        {
            if (_cleared || _locked)
            {
                return;
            }

            SpawnEnemiesOnce();

            if (!AnyEnemyAlive())
            {
                MarkCleared();
                return;
            }

            SetDoorsClosed(true);
            _locked = true;
            _lockedTime = 0f;
            _nextStuckLog = StuckLogInterval;
            ReturnEscapedEnemies();
            AggroAllEnemies();
            LockedChanged?.Invoke(true);
        }

        /// <summary>
        /// 클라이언트 전용(2인 검증 1차 후속): 서버가 보낸 잠김 상태를 로컬 레벨의 문 표시·콜라이더에만 반영한다.
        /// 서버 상태(_locked·_cleared·스폰)는 건드리지 않는다 — 호스트(서버)에서는 아무것도 하지 않는다(서버가 직접 문을 여닫음).
        /// </summary>
        public void ApplyLockedVisual(bool locked)
        {
            if (NetworkServer.active)
            {
                return;
            }

            SetDoorsClosed(locked);
        }

        private void Update()
        {
            if (!_locked || !NetworkServer.active)
            {
                return;
            }

            if (AnyEnemyAlive())
            {
                _lockedTime += Time.deltaTime;
                _escapeCheckTimer -= Time.deltaTime;
                if (_escapeCheckTimer <= 0f)
                {
                    _escapeCheckTimer = EscapeCheckInterval;
                    ReturnEscapedEnemies();
                }

                if (_lockedTime >= _nextStuckLog)
                {
                    _nextStuckLog += StuckLogInterval;
                    LogAliveEnemies();
                }

                return;
            }

            SetDoorsClosed(false);
            _locked = false;
            LockedChanged?.Invoke(false);
            MarkCleared();
            Debug.Log($"[RoomClear] {name} cleared");
        }

        private void MarkCleared()
        {
            if (_cleared)
            {
                return;
            }

            _cleared = true;
            Cleared?.Invoke();
        }

        // 플레이어가 처음 들어온 순간에만 이 방 몬스터를 활성화·네트워크 스폰한다. 한 번 스폰한 방은 다시 안 한다.
        // 방이 잠기는 순간(입장) 방 안 몬스터 전원이 플레이어를 쫓는다(2026-10-16 사용자 요청: 방이 넓어 몬스터를 찾으러 다니지 않게).
        // 발견 거리(detectRange)를 기다리지 않고 바로 넓은 추적 거리(aggroRange)로 들어간다. 서버만.
        private void AggroAllEnemies()
        {
            foreach (var enemy in _enemies)
            {
                if (enemy == null)
                {
                    continue;
                }

                var behavior = enemy.GetComponent<AbsorbRoguelike.Monster.MonsterBehaviorController>();
                if (behavior != null)
                {
                    behavior.Aggro();
                }
            }
        }

        private void SpawnEnemiesOnce()
        {
            if (_spawned || !NetworkServer.active)
            {
                return;
            }

            _spawned = true;
            foreach (var enemy in _enemies)
            {
                if (enemy == null)
                {
                    continue;
                }

                // 방 템플릿 자식인 채로 스폰하면 Mirror가 부모 기준 로컬 좌표를 보내 참가자 화면에서 몬스터가 엉뚱한 곳(원점 근처)에
                // 생긴다(2026-09-30 2인 검증: '호스트가 아니면 적이 안 보이고 대미지만 들어옴'). 월드 루트로 떼어낸 뒤 스폰한다 —
                // 다음 층 정리는 AdvanceFloor의 스폰 오브젝트 파괴가 담당.
                enemy.transform.SetParent(null, true);
                enemy.SetActive(true);
                var identity = enemy.GetComponent<NetworkIdentity>();
                if (identity != null && !identity.isServer)
                {
                    NetworkServer.Spawn(enemy);
                }
            }
        }

        // 문이 닫히기 전에 플레이어를 쫓아 복도로 나갔거나 넉백으로 밀려난 몬스터가 문 밖에 갇혀 방이 영원히
        // 클리어되지 않던 문제(2026-09-23 발견) — 방 바깥에 있는 이 방 몬스터를 방 안쪽 가장자리로 되돌린다.
        private void ReturnEscapedEnemies()
        {
            if (_roomArea == null)
            {
                return;
            }

            Bounds area = _roomArea.bounds;
            Vector2 center = area.center;
            foreach (var enemy in _enemies)
            {
                if (!IsAlive(enemy))
                {
                    continue;
                }

                Vector2 pos = enemy.transform.position;
                if (area.Contains(new Vector3(pos.x, pos.y, area.center.z)))
                {
                    continue;
                }

                Vector2 edge = area.ClosestPoint(new Vector3(pos.x, pos.y, area.center.z));
                Vector2 inside = edge + (center - edge).normalized * 1f;
                var body = enemy.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.position = inside;
                    body.linearVelocity = Vector2.zero;
                }

                enemy.transform.position = inside;
                Debug.Log($"[RoomClear] {name}: returned escaped {enemy.name} from {pos} to {inside}");
            }
        }

        private void LogAliveEnemies()
        {
            var alive = new System.Text.StringBuilder();
            foreach (var enemy in _enemies)
            {
                if (IsAlive(enemy))
                {
                    alive.Append($"{enemy.name}@{(Vector2)enemy.transform.position} ");
                }
            }

            Debug.LogWarning($"[RoomClear] {name} locked for {_lockedTime:0}s — still alive: {alive}");
        }

        private static bool IsAlive(GameObject enemy)
        {
            if (enemy == null)
            {
                return false;
            }

            var monster = enemy.GetComponent<MonsterStub>();
            if (monster != null)
            {
                return !monster.IsDead;
            }

            var boss = enemy.GetComponent<BossStub>();
            return boss != null && !boss.IsDefeated;
        }

        private bool AnyEnemyAlive()
        {
            foreach (var enemy in _enemies)
            {
                if (enemy == null)
                {
                    continue; // 처치/흡수되면 NetworkServer.Destroy로 사라진다
                }

                var monster = enemy.GetComponent<MonsterStub>();
                if (monster != null && !monster.IsDead)
                {
                    return true;
                }

                var boss = enemy.GetComponent<BossStub>();
                if (boss != null && !boss.IsDefeated)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetDoorsClosed(bool closed)
        {
            foreach (var door in _doors)
            {
                if (door != null)
                {
                    door.SetActive(closed);
                }
            }

            PlayDoorSound(closed);
        }

        // 효과음(2026-10-14): 문이 실제로 닫히거나 열리는 순간만(같은 상태 재적용은 무시). 호스트는 서버 경로(Update/입장),
        // 원격은 ApplyLockedVisual(RoomLockSync SyncList 콜백)에서만 여기로 오므로 화면마다 1회. 보스 방이 잠기면 보스 포효(등장)도.
        private bool _doorsShownClosed;

        private void PlayDoorSound(bool closed)
        {
            if (closed == _doorsShownClosed)
            {
                return;
            }

            _doorsShownClosed = closed;
            if (closed)
            {
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.DoorLock);
                if (IsBossRoom)
                {
                    AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.BossRoar);
                }
            }
            else
            {
                AbsorbRoguelike.Audio.Sfx.Play(AbsorbRoguelike.Audio.SfxId.RoomClear);
            }
        }
    }
}
