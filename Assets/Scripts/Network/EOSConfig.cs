// 브라우저판(WebGL): EOSTransport/Epic.OnlineServices asmdef가 WebGL을 제외하므로 이 파일 전체를 WebGL에서 뺀다.
// 씬의 EOSManager/EOSTransport 컴포넌트는 WebGL 빌드에서 'missing script'로만 남는다(네이티브 라이브러리 로드 코드 자체가 컴파일되지 않음).
#if !UNITY_WEBGL
using EpicTransport;
using UnityEngine;

namespace AbsorbRoguelike.Network
{
    /// <summary>
    /// Epic Online Services 접속 설정(ADR-005). Epic 개발자 포털의 "SDK 다운로드 및 크리덴셜" 화면 값을
    /// 유저가 Inspector에 직접 붙여 넣는다(Live 샌드박스 + 그 안의 배포 + Peer2Peer 정책 클라이언트).
    /// 로그인은 Connect 인터페이스의 기기 ID만 쓴다 — 플레이어 Epic 계정·브랜드 심사 불필요.
    /// 실제 값은 git 제외 폴더 Assets/_Local/Resources/EOSConfig.asset에만 둔다(결정 ㉞A, 견본: Assets/Data/EOSConfig.template.asset).
    /// 게임 클라이언트용 값이라 빌드에는 포함된다. 빌드 패치 툴(BPT) 자격 증명은 넣지 말 것.
    /// </summary>
    [CreateAssetMenu(menuName = "Absorb/EOS Config", fileName = "EOSConfig")]
    public class EOSConfig : ScriptableObject
    {
        [Header("Epic 개발자 포털 → SDK 다운로드 및 크리덴셜")]
        public string productName = "Absorb";
        public string productId;
        [Tooltip("Live 샌드박스 ID")]
        public string sandboxId;
        [Tooltip("Live 샌드박스 안의 배포(Deployment) ID")]
        public string deploymentId;
        [Tooltip("Peer2Peer 정책으로 만든 클라이언트의 ID")]
        public string clientId;
        [Tooltip("Peer2Peer 정책으로 만든 클라이언트의 Secret")]
        public string clientSecret;

        [Header("자동 생성 — 바꾸지 않아도 됨")]
        [Tooltip("EOS 저장소 암호화용 64자리 16진수 키. 이 게임은 EOS 저장소를 쓰지 않지만 SDK가 필수로 요구한다.")]
        public string encryptionKey;

        public bool IsFilled =>
            !string.IsNullOrWhiteSpace(productId) && !string.IsNullOrWhiteSpace(sandboxId) && !string.IsNullOrWhiteSpace(deploymentId)
            && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret) && encryptionKey != null && encryptionKey.Length == 64;

        public TransportInitializeOptions ToOptions(string displayName)
        {
            return new TransportInitializeOptions
            {
                AuthInterfaceCredentialType = Epic.OnlineServices.Auth.LoginCredentialType.ExternalAuth, // Auth 인터페이스 안 씀
                ConnectInterfaceCredentialType = Epic.OnlineServices.ExternalCredentialType.DeviceidAccessToken,
                ProductName = productName,
                ProductId = productId.Trim(),
                SandboxId = sandboxId.Trim(),
                DeploymentId = deploymentId.Trim(),
                ClientId = clientId.Trim(),
                ClientSecret = clientSecret.Trim(),
                EncryptionKey = encryptionKey,
                DisplayName = displayName,
            };
        }
    }
}
#endif
