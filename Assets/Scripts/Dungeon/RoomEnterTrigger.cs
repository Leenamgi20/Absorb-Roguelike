using AbsorbRoguelike.Player;
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Dungeon
{
    /// <summary>
    /// 방 템플릿 프리팹 루트에 배치하는 트리거. 방 전체를 덮는 Collider2D(IsTrigger)를 붙여두면,
    /// 플레이어가 새 방에 들어올 때 AbsorptionController.ResetRoomAbsorption()을 호출해 "방당 1마리
    /// 흡수 제한"을 초기화한다. GDD/AbsorptionController에 미리 마련해둔 자리를 실제로 연결하는 부분.
    /// 판정은 서버(호스트) 권위로만 처리한다(ADR-003) — 클라이언트에서 온 트리거 이벤트는 무시.
    /// 2026-09-30: 원격 클라이언트도 레벨을 로컬 생성하므로 이 트리거가 원격 화면에도 존재한다. 첫 줄 NetworkServer.active 가드로
    /// 원격 쪽에서는 흡수 초기화·방 잠금(OnPlayerEntered)이 절대 돌지 않는다(서버가 원격 플레이어의 서버 사본으로 판정).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class RoomEnterTrigger : MonoBehaviour
    {
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            var absorption = other.GetComponent<AbsorptionController>();
            // QA #28: 층 이동 직후 원격 플레이어의 서버 사본은 이전 층 좌표일 수 있다 — 새 시작 위치 도착 확인 전 입장은 무시
            if (absorption != null && RoomLockSync.Instance != null && RoomLockSync.Instance.IsAwaitingFloorArrival(other.gameObject))
            {
                return;
            }

            if (absorption != null)
            {
                absorption.ResetRoomAbsorption();

                var roomClear = GetComponentInParent<RoomClearController>();
                if (roomClear != null)
                {
                    roomClear.OnPlayerEntered();
                }
            }
        }
    }
}
