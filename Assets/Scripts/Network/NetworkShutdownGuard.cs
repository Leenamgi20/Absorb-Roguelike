#if !UNITY_WEBGL
using EpicTransport;
#endif
using Mirror;
using UnityEngine;

namespace AbsorbRoguelike.Network
{
    /// <summary>
    /// Mirror의 StopHost는 "클라이언트 종료 → 서버 종료" 순서라, 앞 단계에서 예외가 나면(예: 호스트 시작이
    /// 반쯤 실패한 상태에서 Play 종료) 서버 종료가 건너뛰어져 UDP 포트(7777)가 에디터에 붙잡힌 채 남는다.
    /// 그러면 다음 Play부터 "각 소켓 주소는 하나만 사용할 수 있습니다" SocketException이 난다(2026-09-23 발견).
    /// 종료 시점에 서버와 트랜스포트를 한 번 더, 예외를 삼키며 확실히 닫는다. NetworkManager와 같은 오브젝트에 둔다.
    /// EOS(eos-spike): 전송이 EOSTransport이고 로비에 들어가 있으면 NetworkServer.Shutdown보다 먼저 LeaveLobby를 부른다 —
    /// LeaveLobby는 NetworkServer.active일 때만 로비를 파괴하고, Transport.Shutdown은 로비를 정리하지 않는다.
    /// 요청은 비동기라 창을 바로 닫으면 EOS까지 못 갈 수 있다(메뉴 '종료' 버튼은 최대 2초 기다린 뒤 닫는다).
    /// </summary>
    [DefaultExecutionOrder(-1000)] // Mirror NetworkManager.OnApplicationQuit보다 먼저 — 그쪽이 예외로 끊겨도 전송은 이미 닫혀 있게(QA #56)
    public class NetworkShutdownGuard : MonoBehaviour
    {
        private Transport[] _transports; // Transport.active가 Mirror 정리 중 바뀌어도 이 오브젝트의 전송(KCP·EOS)을 직접 닫는다

        private void Awake()
        {
            _transports = GetComponentsInChildren<Transport>(true);
        }

        private void OnApplicationQuit()
        {
#if !UNITY_WEBGL // 브라우저판: EOS 어셈블리 없음 — 서버·전송 종료만 한다
            try
            {
                if (Transport.active is EOSTransport && EOSTransport.instance != null && EOSTransport.ConnectedToLobby)
                {
                    EOSTransport.LeaveLobby();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[NetworkShutdownGuard] EOSTransport.LeaveLobby failed: {e.GetType().Name}");
            }
#endif

            try
            {
                if (NetworkServer.active)
                {
                    NetworkServer.Shutdown();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[NetworkShutdownGuard] NetworkServer.Shutdown failed: {e.Message}");
            }

            try
            {
                if (Transport.active != null)
                {
                    Transport.active.Shutdown();
                }

                foreach (Transport t in _transports ?? System.Array.Empty<Transport>())
                {
                    if (t != null && t != Transport.active && (t.ServerActive() || t.ClientConnected()))
                    {
                        t.Shutdown();
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[NetworkShutdownGuard] Transport.Shutdown failed: {e.Message}");
            }
        }
    }
}
