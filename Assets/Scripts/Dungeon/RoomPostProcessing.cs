using System.Collections.Generic;
using AbsorbRoguelike.Monster;
using Edgar.Unity;
using Edgar.Unity.Examples.Example1;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 방 템플릿 프리팹의 "Enemies" 자식 오브젝트 아래 미리 배치해둔 몬스터(MonsterStub, 기본
    /// 비활성)를 방 생성 시 활성화하고 네트워크에 스폰한다. Edgar 예제(Example1PostProcessing)의
    /// "Enemies 홀더를 찾아 자식을 활성화"하는 패턴을 그대로 따르되, Mirror 프로젝트라 활성화만으로는
    /// 원격 클라이언트에 전파되지 않으므로 NetworkServer.Spawn을 추가로 호출한다.
    /// 2026-09-30 2인 검증 1차 후속: 원격 클라이언트도 서버가 보낸 시드로 같은 레벨을 로컬 생성한다(LevelGenerator
    /// ClientGenerateCoroutine). 이때(NetworkServer.active가 아님)는 <see cref="RunClient"/>로 갈라져 시각·충돌 요소(복도 문 기본 열기,
    /// 문 목록만 가진 RoomClearController, 보스 방 오버레이)만 만들고, 방 템플릿 안의 NetworkIdentity 오브젝트(몬스터·보스 자리)는
    /// 지운다 — 몬스터는 서버가 Spawn한 진짜만 보여야 한다. 몬스터 교체·NetworkServer.Spawn은 건너뛴다.
    /// 후처리의 Random 호출 순서가 달라져도 레이아웃은 그 전에 이미 정해지므로(Edgar: 레이아웃 생성 → 후처리) 결과에 영향 없다.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/Dungeon/Room Post-Processing", fileName = "RoomPostProcessing")]
    public class RoomPostProcessing : DungeonGeneratorPostProcessingGrid2D
    {
        public override void Run(DungeonGeneratorLevelGrid2D level)
        {
            if (!NetworkServer.active)
            {
                RunClient(level);
                return;
            }

            // 복도 템플릿(Hor/Ver) 가운데에 Edgar 예제 문(Example1Door, 충돌체+스프라이트)이 하나씩 있다.
            // 예제의 "E로 열기" 상호작용은 우리 PlayerController로는 발동하지 않으므로 기본은 전부 열어두고,
            // 몬스터가 있는 방에 들어가면 RoomClearController가 그 방에 연결된 복도 문만 닫았다 연다.
            foreach (var roomInstance in level.RoomInstances)
            {
                foreach (var door in roomInstance.RoomTemplateInstance.GetComponentsInChildren<Example1Door>(true))
                {
                    door.gameObject.SetActive(false);
                }
            }

            foreach (var roomInstance in level.RoomInstances)
            {
                var enemiesHolder = roomInstance.RoomTemplateInstance.transform.Find("Enemies");
                if (enemiesHolder == null)
                {
                    continue;
                }

                ReplaceWithFloorAnimals(enemiesHolder);

                // 몬스터는 여기서 스폰하지 않고 비활성으로 대기시킨다 — 플레이어가 방에 처음 들어올 때
                // RoomClearController가 스폰한다(2026-09-23, 입장 전 몬스터가 입구까지 쫓아와 문 밖에 갇히던 문제 방지).
                var enemies = new List<GameObject>();
                foreach (Transform enemyTransform in enemiesHolder)
                {
                    enemyTransform.gameObject.SetActive(false);
                    enemies.Add(enemyTransform.gameObject);
                }

                if (enemies.Count == 0)
                {
                    continue;
                }

                var doors = new List<GameObject>();
                foreach (var doorInstance in roomInstance.Doors)
                {
                    var corridor = doorInstance.ConnectedRoomInstance;
                    if (corridor == null)
                    {
                        continue;
                    }

                    foreach (var door in corridor.RoomTemplateInstance.GetComponentsInChildren<Example1Door>(true))
                    {
                        doors.Add(door.gameObject);
                    }
                }

                var roomClear = roomInstance.RoomTemplateInstance.AddComponent<RoomClearController>();
                roomClear.Initialize(enemies, doors);

                // 보스 방: Edgar 예제 출구(Example1Exit — 예제 GameManager 전용이라 우리 씬에선 동작 안 함)를 끄고
                // 같은 자리에 다음 층 출구를 둔다.
                BossStub boss = null;
                foreach (var enemy in enemies)
                {
                    boss = boss != null ? boss : enemy.GetComponent<BossStub>();
                }

                if (boss != null)
                {
                    var legacyExit = roomInstance.RoomTemplateInstance.transform.Find("Exit");
                    Vector3 exitPosition = legacyExit != null ? legacyExit.position : roomInstance.RoomTemplateInstance.transform.position;
                    if (legacyExit != null)
                    {
                        legacyExit.gameObject.SetActive(false);
                    }

                    var exit = new GameObject("Floor Exit");
                    exit.transform.SetParent(roomInstance.RoomTemplateInstance.transform, false);
                    exit.transform.position = exitPosition;
                    exit.AddComponent<FloorExit>().Initialize(boss);
                    AddBossRoomOverlay(roomInstance.RoomTemplateInstance);
                }
            }
        }

        /// <summary>
        /// 원격 클라이언트 전용 후처리. 서버 Run과 같은 기준(Enemies 홀더 자식이 있는 방 = RoomClearController, 보스 자리가 있는 방 = 보스 방)으로
        /// 방을 고르되, 적 목록은 비워 둔다(판정은 서버). Floor Exit는 보스 참조 없이 붙이고(FloorExit가 스폰된 보스를 찾음), 출구 판정은 서버가 한다.
        /// </summary>
        private void RunClient(DungeonGeneratorLevelGrid2D level)
        {
            foreach (var roomInstance in level.RoomInstances)
            {
                foreach (var door in roomInstance.RoomTemplateInstance.GetComponentsInChildren<Example1Door>(true))
                {
                    door.gameObject.SetActive(false);
                }
            }

            foreach (var roomInstance in level.RoomInstances)
            {
                GameObject root = roomInstance.RoomTemplateInstance;
                var enemiesHolder = root.transform.Find("Enemies");
                bool hasEnemies = enemiesHolder != null && enemiesHolder.childCount > 0;
                bool isBossRoom = hasEnemies && enemiesHolder.GetComponentInChildren<BossStub>(true) != null;

                // 몬스터·보스 자리(네트워크 오브젝트)는 로컬 사본을 남기지 않는다 — 서버가 Spawn한 것만 보인다.
                foreach (var identity in root.GetComponentsInChildren<NetworkIdentity>(true))
                {
                    if (identity != null)
                    {
                        identity.gameObject.SetActive(false);
                        Object.Destroy(identity.gameObject);
                    }
                }

                if (!hasEnemies)
                {
                    continue;
                }

                var doors = new List<GameObject>();
                foreach (var doorInstance in roomInstance.Doors)
                {
                    var corridor = doorInstance.ConnectedRoomInstance;
                    if (corridor == null)
                    {
                        continue;
                    }

                    foreach (var door in corridor.RoomTemplateInstance.GetComponentsInChildren<Example1Door>(true))
                    {
                        doors.Add(door.gameObject);
                    }
                }

                root.AddComponent<RoomClearController>().Initialize(new List<GameObject>(), doors);

                if (isBossRoom)
                {
                    var legacyExit = root.transform.Find("Exit");
                    Vector3 exitPosition = legacyExit != null ? legacyExit.position : root.transform.position;
                    if (legacyExit != null)
                    {
                        legacyExit.gameObject.SetActive(false);
                    }

                    // 출구 표시(2026-09-30): 판정은 서버가 하지만 참가자도 출구가 보여야 올라설 수 있다. 보스 참조는 없으므로 null로 넘기고,
                    // FloorExit가 네트워크로 스폰된 보스를 스스로 찾는다. 위치는 서버와 같은 기준(템플릿 "Exit").
                    var exit = new GameObject("Floor Exit");
                    exit.transform.SetParent(root.transform, false);
                    exit.transform.position = exitPosition;
                    exit.AddComponent<FloorExit>().Initialize(null);
                    AddBossRoomOverlay(root);
                }
            }
        }

        /// <summary>
        /// 스테이지마다 무작위 동물 풀(GDD v2.14): 방 템플릿에 배치된 잡몹/정예몹 자리는 "여기에 몬스터 1마리"라는
        /// 위치 표시로만 쓰고, 이번 층 풀에서 무작위로 고른 동물로 바꿔 넣는다(정예몹 자리는 그 동물의 정예몹으로).
        /// 보스는 건드리지 않는다. 새로 넣는 몬스터는 Awake가 스폰 전에 돌지 않도록 홀더를 잠깐 꺼둔 채 만든다.
        /// </summary>
        // 보스 방 장식(2026-10-06, 붉은 반투명 덧칠 대체): 속성 타일 위에 속성 무관 장식(금·돌·청록 빛)을 얹는다.
        // 바닥 영역 = 방 입장 트리거(BoxCollider2D). 가운데 봉인 원(3×3칸), 안쪽 네 모서리 청록 화로(2프레임),
        // 바닥을 둘러싼 벽 줄 4면에 금 장식 띠 + 바깥 모서리 마감(2026-10-06 4면 확장). 시각 전용(충돌 없음), 서버·원격 클라이언트 모두 같은 자리에 만든다.
        // 정렬: 봉인 1(바닥 0 위, 아이템 6·캐릭터 10 아래), 벽 장식 3(벽 1~2 위), 화로 5. 스프라이트가 비어 있으면 그 장식은 건너뛴다.
        [Header("보스 방 장식 (2026-10-06)")]
        [SerializeField] private Sprite bossSeal;
        [SerializeField] private Sprite[] bossBrazierFrames;
        [SerializeField] private Sprite bossTrimLeft;
        [SerializeField] private Sprite bossTrimMid;
        [SerializeField] private Sprite bossTrimRight;
        [Tooltip("아래·왼쪽·오른쪽 벽 금 띠: 각각 [시작, 가운데, 끝] 3장(가로는 왼→오, 세로는 위→아래).")]
        [SerializeField] private Sprite[] bossTrimBottom;
        [SerializeField] private Sprite[] bossTrimLeftSide;
        [SerializeField] private Sprite[] bossTrimRightSide;
        [Tooltip("바깥 모서리 마감 [왼위, 오른위, 왼아래, 오른아래].")]
        [SerializeField] private Sprite[] bossTrimCaps;
        [Tooltip("화로 불꽃 프레임 간격(초).")]
        [SerializeField] private float bossBrazierFrameTime = 0.25f;

        private void AddBossRoomOverlay(GameObject room)
        {
            RoomPostProcessing src = this;
            var box = room.GetComponent<BoxCollider2D>();
            if (box == null)
            {
                return;
            }

            var holder = new GameObject("Boss Room Decor");
            holder.transform.SetParent(room.transform, false);
            holder.transform.localPosition = box.offset;
            Vector2 half = box.size * 0.5f;

            SpriteRenderer Place(string name, Sprite sprite, Vector2 local, int order)
            {
                var go = new GameObject(name);
                go.transform.SetParent(holder.transform, false);
                go.transform.localPosition = local;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = order;
                return sr;
            }

            if (src.bossSeal != null)
            {
                Place("Seal", src.bossSeal, Vector2.zero, 1);
            }

            if (src.bossBrazierFrames != null && src.bossBrazierFrames.Length > 0 && src.bossBrazierFrames[0] != null)
            {
                float inset = 1.5f;
                foreach (Vector2 corner in new[] { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, -1) })
                {
                    Vector2 pos = new Vector2(corner.x * (half.x - inset), corner.y * (half.y - inset));
                    SpriteRenderer sr = Place("Brazier", src.bossBrazierFrames[0], pos, 5);
                    if (src.bossBrazierFrames.Length > 1)
                    {
                        var flip = sr.gameObject.AddComponent<SpriteFlipbook>();
                        flip.Init(src.bossBrazierFrames, src.bossBrazierFrameTime, corner.x > 0 ? 1 : 0);
                    }
                }
            }

            if (src.bossTrimMid != null)
            {
                // 바닥 바로 위 벽 줄(한 칸 높이)에 칸마다 금 띠. 칸 중심 = 바닥 위 경계 + 0.5.
                int cells = Mathf.RoundToInt(box.size.x);
                float y = half.y + 0.5f;
                for (int i = 0; i < cells; i++)
                {
                    Sprite s = i == 0 && src.bossTrimLeft != null ? src.bossTrimLeft
                        : i == cells - 1 && src.bossTrimRight != null ? src.bossTrimRight
                        : src.bossTrimMid;
                    Place("Trim", s, new Vector2(-half.x + 0.5f + i, y), 3);
                }
            }

            Sprite Pick(Sprite[] set, int i, int count)
            {
                if (set == null || set.Length < 3)
                {
                    return null;
                }

                return i == 0 ? set[0] : i == count - 1 ? set[2] : set[1];
            }

            int w = Mathf.RoundToInt(box.size.x);
            int h = Mathf.RoundToInt(box.size.y);
            for (int i = 0; i < w; i++)
            {
                Sprite s = Pick(bossTrimBottom, i, w);
                if (s != null)
                {
                    Place("Trim Bottom", s, new Vector2(-half.x + 0.5f + i, -half.y - 0.5f), 3);
                }
            }

            for (int j = 0; j < h; j++)
            {
                float y = half.y - 0.5f - j; // 위 → 아래
                Sprite l = Pick(bossTrimLeftSide, j, h);
                if (l != null)
                {
                    Place("Trim Left", l, new Vector2(-half.x - 0.5f, y), 3);
                }

                Sprite r = Pick(bossTrimRightSide, j, h);
                if (r != null)
                {
                    Place("Trim Right", r, new Vector2(half.x + 0.5f, y), 3);
                }
            }

            if (bossTrimCaps != null && bossTrimCaps.Length >= 4)
            {
                Vector2[] caps = { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, -1) };
                for (int c = 0; c < 4; c++)
                {
                    if (bossTrimCaps[c] != null)
                    {
                        Place("Trim Cap", bossTrimCaps[c], new Vector2(caps[c].x * (half.x + 0.5f), caps[c].y * (half.y + 0.5f)), 3);
                    }
                }
            }
        }

        // 템플릿에 박힌 보스 인스턴스를 프리팹 원본에서 새로 만든 것으로 바꾼다 — assetId를 원본 프리팹에서 가져오게 해서,
        // 템플릿 중첩 인스턴스의 _assetId가 에디터 재임포트로 바뀌어도 호스트·참가자가 같은 보스를 스폰할 수 있게(2026-09-30).
        private static void ReplaceBossWithPrefab(Transform enemiesHolder, FloorProgression dungeon)
        {
            if (dungeon == null || dungeon.BossPrefab == null)
            {
                return;
            }

            var bosses = new List<Transform>();
            foreach (Transform child in enemiesHolder)
            {
                if (child.GetComponent<BossStub>() != null)
                {
                    bosses.Add(child);
                }
            }

            foreach (Transform slot in bosses)
            {
                GameObject replacement = Object.Instantiate(dungeon.BossPrefab, enemiesHolder);
                replacement.name = dungeon.BossPrefab.name;
                replacement.transform.localPosition = slot.localPosition;
                replacement.SetActive(false);
                slot.SetParent(null);
                Object.Destroy(slot.gameObject);
            }
        }

        // 난수는 Edgar 후처리 기반 클래스의 Random(던전 시드를 따름)을 쓴다 — 나중에 시드로 레이아웃을 맞출 때 같이 맞춰진다.
        private void ReplaceWithFloorAnimals(Transform enemiesHolder)
        {
            var dungeon = FloorProgression.Instance;
            ReplaceBossWithPrefab(enemiesHolder, dungeon);
            if (dungeon == null || dungeon.CurrentAnimalPool.Count == 0)
            {
                return;
            }

            var slots = new List<Transform>();
            foreach (Transform child in enemiesHolder)
            {
                if (child.GetComponent<MonsterStub>() != null)
                {
                    slots.Add(child);
                }
            }

            if (slots.Count == 0)
            {
                return;
            }

            bool holderWasActive = enemiesHolder.gameObject.activeSelf;
            enemiesHolder.gameObject.SetActive(false);

            foreach (Transform slot in slots)
            {
                var entry = dungeon.CurrentAnimalPool[Random.Next(dungeon.CurrentAnimalPool.Count)];
                GameObject prefab = slot.GetComponent<MonsterStub>().IsElite ? entry.elitePrefab : entry.normalPrefab;
                if (prefab == null)
                {
                    continue;
                }

                GameObject replacement = Object.Instantiate(prefab, enemiesHolder);
                replacement.name = prefab.name;
                replacement.transform.localPosition = slot.localPosition;
                replacement.SetActive(false);

                slot.SetParent(null);
                Object.Destroy(slot.gameObject);
            }

            enemiesHolder.gameObject.SetActive(holderWasActive);
        }
    }
}
