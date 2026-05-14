using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;

    private TcpClientConnector connector;

    [SerializeField] private MainUIManager mainUIManager;

    public ushort SessionToken { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            connector = new TcpClientConnector();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void ConnectToServer()
    {
        connector.Connect("127.0.0.1", 7777);
        Debug.Log("서버 연결 시도");
    }

    public void SendLogin(string nickname)
    {
        Debug.Log($"로그인 요청: {nickname}");

        // TODO:
        // 여기서 C_Login 패킷 만들어서 서버로 보내면 됨.
        // 예: connector.Send(packetBytes);
    }

    public void Disconnect()
    {
        connector.Disconnect();
        Debug.Log("서버 연결 종료");
    }

    public void HandleLoginResult(bool success, ushort sessionToken)
    {
        if (success)
        {
            SessionToken = sessionToken;
            Debug.Log($"로그인 성공 / SessionToken: {SessionToken}");

            mainUIManager.ShowMatchingPopup();
        }
        else
        {
            Debug.Log("로그인 실패");
        }
    }

    public void HandleMatchingStatus(int currentCount, int maxCount)
    {
        mainUIManager.UpdateMatchingCount(currentCount, maxCount);
    }
}