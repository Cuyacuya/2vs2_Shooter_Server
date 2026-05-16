using System.Collections.Concurrent;
using System.Text;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;

    private TcpClientConnector connector;
    private PacketSender sender;
    private PacketReceiver receiver;

    private readonly ConcurrentQueue<ReceivedPacket> receiveQueue = new();

    [SerializeField] private MainUIManager mainUIManager;

    public ushort SessionToken { get; private set; }

    private const ushort C_Login = 1;
    private const ushort S_LoginResult = 2;
    private const ushort S_MatchingStatus = 3;
    private const ushort S_GameStart = 4;

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

    private void Update()
    {
        while (receiveQueue.TryDequeue(out ReceivedPacket packet))
        {
            HandlePacket(packet);
        }
    }

    public void ConnectToServer()
    {
        bool success = connector.Connect("127.0.0.1", 7777);

        if (!success)
            return;

        sender = new PacketSender(connector.Stream);
        receiver = new PacketReceiver(connector.Stream, receiveQueue);
        receiver.StartReceive();

        Debug.Log("패킷 송수신 준비 완료");
    }

    public async void SendLogin(string nickname)
    {
        if (sender == null)
        {
            Debug.LogError("서버에 연결되어 있지 않습니다.");
            return;
        }

        byte[] payload = Encoding.UTF8.GetBytes(nickname);

        await sender.SendAsync(C_Login, 0, payload);

        Debug.Log($"C_Login 전송: {nickname}");
    }

    private void HandlePacket(ReceivedPacket packet)
    {
        switch (packet.PacketId)
        {
            case S_LoginResult:
                HandleLoginResultPacket(packet);
                break;


            case S_MatchingStatus:
                HandleMatchingStatusPacket(packet);
                break;

            case S_GameStart:
                HandleGameStartPacket(packet);
                break;

            default:
                Debug.LogWarning($"알 수 없는 패킷 수신: {packet.PacketId}");
                break;
        }
    }

    private void HandleLoginResultPacket(ReceivedPacket packet)
    {
        if (packet.Payload.Length < 3)
        {
            Debug.LogError("S_LoginResult payload 길이가 잘못되었습니다.");
            return;
        }

        bool success = packet.Payload[0] == 1;
        ushort sessionToken = System.BitConverter.ToUInt16(packet.Payload, 1);

        HandleLoginResult(success, sessionToken);
    }

    private void HandleMatchingStatusPacket(ReceivedPacket packet)
    {
        if (packet.Payload.Length < 2)
        {
            Debug.LogError("S_MatchingStatus payload 길이가 잘못되었습니다.");
            return;
        }

        int currentCount = packet.Payload[0];
        int maxCount = packet.Payload[1];

        HandleMatchingStatus(currentCount, maxCount);
    }

    private void HandleGameStartPacket(ReceivedPacket packet)
    {
        Debug.Log("S_GameStart 수신");

        UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
    }

    public void Disconnect()
    {
        receiver?.StopReceive();
        connector.Disconnect();

        sender = null;
        receiver = null;
        SessionToken = 0;

        Debug.Log("네트워크 초기화 완료");
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