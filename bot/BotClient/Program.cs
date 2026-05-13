// 프레이밍 + C_Login 검증 봇
using System.Net.Sockets;
using Shared;

const string Host = "127.0.0.1";
const int Port = 7777;

static async Task<(ushort id, BinaryReader br, MemoryStream ms)?>
ReadOnePacketAsync(NetworkStream networkStream, CancellationToken ct)
{
    byte[] buf = new byte[1024];
    int writePos = 0;

    while (true)
    {
        int read = await networkStream.ReadAsync(buf.AsMemory(writePos), ct);
        if (read == 0) return null;
        writePos += read;

        if (writePos < 6) continue;
        ushort len = BitConverter.ToUInt16(buf, 0);
        int total = 6 + len;
        if (writePos < total) continue;

        var ms = new MemoryStream(buf, 0, total);
        var br = new BinaryReader(ms);
        var (length, id, token) = PacketIO.ReadHeader(br);
        return (id, br, ms);
    }
}

// 로그인 후 응답 받고 연결을 유지한 채 TcpClient 반환
// (연결을 끊으면 서버가 큐에서 빼버려서 매치 못 채움 → 유지 필요)
static async Task<TcpClient?> LoginAsync(string nickname)
{
    var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();
    await stream.WriteAsync(new C_Login { Nickname = nickname }.Serialize());

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var result = await ReadOnePacketAsync(stream, cts.Token);
    if (result is null)
    {
        Console.WriteLine($"  [{nickname}] (no response)");
        client.Dispose();
        return null;
    }
    var (id, br, ms) = result.Value;
    if ((PacketId)id == PacketId.S_LoginResult)
    {
        var r = S_LoginResult.Deserialize(br);
        Console.WriteLine($"  [{nickname}] success={r.Success}, token={r.SessionToken},reason = '{r.Reason}'");
    }
    ms.Dispose();
    return client;
}
// 시나리오: 4명 차곡차곡 로그인 → 큐 채워 매치 시작
Console.WriteLine("=== Step 1: 1~4번 로그인 (큐 채우기) ===");
var clients = new List<TcpClient>();
for (int i = 1; i <= 4; i++)
{
    var c = await LoginAsync($"p{i}");
    if (c != null) clients.Add(c);
    await Task.Delay(100); // 로그 순서 보기 좋게
}

// 5번째 — 매치 진행 중이라 거부 예상
await Task.Delay(300);
Console.WriteLine("\n=== Step 2: 5번째 (MATCH_IN_PROGRESS 거부 예상) ===");
var c5 = await LoginAsync("p5");
c5?.Dispose();

await Task.Delay(500);
Console.WriteLine("\n[Bot] 정리 (모든 연결 종료)");
foreach (var c in clients) c.Dispose();