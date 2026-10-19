using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Player
{
    /// <summary>
    /// 영구 재화(각인 트리용)를 위한 임시 카운터(결정 대기 체크리스트 E). 실제 세이브/영구 저장 시스템이
    /// 아직 없어 지금은 세션(런) 한정으로만 누적된다 — 각인/메타 진행 시스템을 붙일 때 여기 쌓인 값을
    /// 실제 영구 재화로 전환하는 로직을 연결한다.
    /// </summary>
    public class PlayerCurrency : NetworkBehaviour
    {
        [SyncVar] private int runCurrency;

        public int RunCurrency => runCurrency;

        public void AddCurrency(int amount)
        {
            if (!isServer)
            {
                return; // 서버(호스트) 권위 (ADR-003)
            }

            runCurrency += amount;
            Debug.Log($"[PlayerCurrency] {name} gained {amount} currency (total: {runCurrency})");
        }
    }
}
