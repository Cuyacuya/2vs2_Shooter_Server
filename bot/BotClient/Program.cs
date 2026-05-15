// 프레이밍 + C_Login + 매칭 패킷 수신 검증 봇
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

// 로그인 이후 들어오는 매칭/게임 시작 패킷을 계속 수신해 콘솔에 찍는다.
// CancellationToken으로 종료. 연결 끊김(read==0)이나 예외도 종료 사유.
static async Task ListenLoopAsync(TcpClient client, string nickname, CancellationToken ct)
{
    var stream = client.GetStream();
    var buf = new byte[4096];
    int writePos = 0;

    try
    {
        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buf.AsMemory(writePos), ct);
            if (read == 0) break;
            writePos += read;

            int readPos = 0;
            while (writePos - readPos >= 6)
            {
                ushort len = BitConverter.ToUInt16(buf, readPos);
                int total = 6 + len;
                if (writePos - readPos < total) break;

                using var ms = new MemoryStream(buf, readPos, total);
                using var br = new BinaryReader(ms);
                var (length, id, token) = PacketIO.ReadHeader(br);

                switch ((PacketId)id)
                {
                    case PacketId.S_MatchingStatus:
                    {
                        var pkt = S_MatchingStatus.Deserialize(br);
                        Console.WriteLine($"  [{nickname}] <- S_MatchingStatus {pkt.CurrentCount}/{pkt.MaxCount}");
                        break;
                    }
                    case PacketId.S_GameStart:
                    {
                        var pkt = S_GameStart.Deserialize(br);
                        string myTeam = pkt.MyTeam == 0 ? "Red" : "Blue";
                        Console.WriteLine($"  [{nickname}] <- S_GameStart myTeam={myTeam}, players={pkt.Players.Count}");
                        foreach (var p in pkt.Players)
                        {
                            string t = p.Team == 0 ? "Red" : "Blue";
                            Console.WriteLine($"      - token={p.Token}, team={t}, nick={p.Nickname}");
                        }
                        break;
                    }
                    default:
                        Console.WriteLine($"  [{nickname}] <- unknown packetId={id}");
                        break;
                }

                readPos += total;
            }

            int remain = writePos - readPos;
            if (remain > 0) Buffer.BlockCopy(buf, readPos, buf, 0, remain);
            writePos = remain;
        }
    }
    catch (OperationCanceledException) { }
    catch (ObjectDisposedException) { }
    catch (Exception ex)
    {
        Console.WriteLine($"  [{nickname}] listen error: {ex.Message}");
    }
}

// 시나리오: 4명 차곡차곡 로그인 → 큐 채워 매치 시작
Console.WriteLine("=== Step 1: 1~4번 로그인 (큐 채우기) ===");
var clients = new List<(TcpClient client, string nick)>();
var listenCts = new CancellationTokenSource();
var listeners = new List<Task>();
for (int i = 1; i <= 4; i++)
{
    string nick = $"p{i}";
    var c = await LoginAsync(nick);
    if (c != null)
    {
        clients.Add((c, nick));
        listeners.Add(ListenLoopAsync(c, nick, listenCts.Token));
    }
    await Task.Delay(100); // 로그 순서 보기 좋게
}

// 5번째 — 매치 진행 중이라 거부 예상
await Task.Delay(500);
Console.WriteLine("\n=== Step 2: 5번째 (MATCH_IN_PROGRESS 거부 예상) ===");
var c5 = await LoginAsync("p5");
c5?.Dispose();

// 매칭/게임 시작 패킷 수신 시간 확보
await Task.Delay(500);
Console.WriteLine("\n[Bot] 정리 (모든 연결 종료)");
listenCts.Cancel();
foreach (var (c, _) in clients) c.Dispose();
try { await Task.WhenAll(listeners); } catch { }
