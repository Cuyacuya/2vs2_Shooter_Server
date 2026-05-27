// 7777포트에서 TCP 접속을 받아 각 클라마다 ClientSession을 만들어 위임한다.
using System.Net;
using System.Net.Sockets;
using GameServer;
using Shared;

const int TcpPort = 7777;
const int UdpPort = 7778;

// 게임 밸런스 설정 로드 (서버·클라 공유).
// 실행 위치(bin/Debug/net8.0/)에서 위로 올라가며 Game.sln이 있는 폴더(=레포 루트) 탐색.
// "../../../../../" 같이 단계 수를 박는 방식은 빌드 출력 경로 바뀌면 깨지므로 안전한 패턴 채택.
string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("repo root (containing Game.sln) not found");
}
string configPath = Path.Combine(FindRepoRoot(), "config", "balance.json");
Console.WriteLine($"[Balance] loading from {configPath}");
BalanceLoader.LoadFromFile(configPath);
Console.WriteLine($"[Balance] loaded: MoveSpeed={Balance.Current.Player.MoveSpeed}, " +
                  $"Damage={Balance.Current.Weapon.Damage}, Gravity={Balance.Current.Physics.Gravity}");

var listener = new TcpListener(IPAddress.Any, TcpPort);
listener.Start();
Console.WriteLine($"[Server] Listening on {TcpPort}");

// UDP 채널 (3주차): C_UdpHello 핸드셰이크 후 C_Input/S_Snapshot이 UDP로 흐름
UdpServer.Instance.Start(UdpPort);

// 정식 30Hz 틱 루프 (3주차 수요일): 큐잉된 입력을 일괄 처리 + 매 틱 스냅샷 송신
TickServer.Instance.Start();

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    var session = new ClientSession(client);
    Console.WriteLine($"[Server] Client connected: {session.Endpoint}");

    // 세션 수신 루프를 백그라운드로 (fire-and-forget)
    _ = session.RunAsync();
}
