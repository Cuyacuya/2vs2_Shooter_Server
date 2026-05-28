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

// 로그인 후 응답 받고 연결을 유지한 채 (TcpClient, sessionToken) 반환
// (연결을 끊으면 서버가 큐에서 빼버려서 매치 못 채움 → 유지 필요)
static async Task<(TcpClient client, ushort token)?> LoginAsync(string nickname)
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
    ushort token = 0;
    if ((PacketId)id == PacketId.S_LoginResult)
    {
        var r = S_LoginResult.Deserialize(br);
        token = r.SessionToken;
        Console.WriteLine($"  [{nickname}] success={r.Success}, token={r.SessionToken},reason = '{r.Reason}'");
    }
    ms.Dispose();
    return (client, token);
}

// 로그인 이후 들어오는 매칭/게임 시작 패킷을 계속 수신해 콘솔에 찍는다.
// CancellationToken으로 종료. 연결 끊김(read==0)이나 예외도 종료 사유.
// gameStarted: S_GameStart 수신 시 TrySetResult(true)로 InputLoopAsync에 신호.
static async Task ListenLoopAsync(TcpClient client, string nickname,
                                  TaskCompletionSource<byte> gameStarted,
                                  CancellationToken ct)
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
                        gameStarted.TrySetResult(pkt.MyTeam);    // 입력 루프 깨움 + 팀 전달
                        break;
                    }
                    case PacketId.S_Snapshot:
                    {
                        var pkt = S_Snapshot.Deserialize(br);
                        // 로그 폭주 방지: 봇 1번만 가끔 찍기
                        if (nickname == "p1" && Random.Shared.Next(20) == 0)
                            Console.WriteLine($"  [{nickname}] <- S_Snapshot {pkt.Players.Count}명");
                        break;
                    }
                    case PacketId.S_HitResult:
                    {
                        var pkt = S_HitResult.Deserialize(br);
                        Console.WriteLine($"  [{nickname}] <- S_HitResult atk={pkt.AttackerToken} vic={pkt.VictimToken} dmg={pkt.Damage} hp={pkt.VictimHpAfter} kill={pkt.IsKill}");
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

// 게임 시작 후 20Hz로 랜덤 입력/사격 송신. 통합 테스트용.
// C_UdpHello + C_Input은 UDP, C_Fire는 TCP (헤더 sessionToken 0 → 매핑된 stream 으로 구분).
static async Task InputLoopAsync(TcpClient client, string nickname, ushort token,
                                 TaskCompletionSource<byte> gameStarted,
                                 CancellationToken ct)
{
    System.Net.Sockets.UdpClient? udp = null;
    try
    {
        // S_GameStart 도착까지 대기. ct 취소되면 즉시 탈출.
        var waitTask = gameStarted.Task;
        var cancelTcs = new TaskCompletionSource();
        using (ct.Register(() => cancelTcs.TrySetResult()))
        {
            await Task.WhenAny(waitTask, cancelTcs.Task);
        }
        if (ct.IsCancellationRequested || !waitTask.IsCompleted) return;

        byte myTeam = await waitTask;
        var stream = client.GetStream();
        var rng = new Random(nickname.GetHashCode());

        // 적팀 향한 yaw로 시작 (Red=90→+X, Blue=270→-X). 서버 스폰과 일치.
        float yaw = myTeam == 0 ? 90f : 270f;

        // UDP 채널 준비 — ephemeral 포트로 바인딩하여 서버 7778에 송신
        udp = new System.Net.Sockets.UdpClient(0);
        var serverUdpEp = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(Host), 7778);

        // C_UdpHello 송신 (1~2회 재시도해도 OK, 일단 1회)
        byte[] helloBytes = new C_UdpHello().Serialize(token);
        await udp.SendAsync(helloBytes, helloBytes.Length, serverUdpEp);
        await Task.Delay(50, ct);   // 서버가 endpoint 매핑할 시간

        while (!ct.IsCancellationRequested)
        {
            byte bits = 0;
            if (rng.NextDouble() < 0.2) bits |= 1 << 0;           // W 20%
            if (rng.NextDouble() < 0.02) bits |= 1 << 4;          // Jump 2%

            yaw += (float)((rng.NextDouble() - 0.5) * 4.0);
            if (yaw < 0)   yaw += 360;
            if (yaw > 360) yaw -= 360;

            // C_Input → UDP
            byte[] inputBytes = new C_Input { InputBits = bits, Yaw = yaw, Pitch = 0f }.Serialize(token);
            await udp.SendAsync(inputBytes, inputBytes.Length, serverUdpEp);

            // C_Fire → TCP (사격은 신뢰성 필요)
            if (rng.NextDouble() < 0.25)
            {
                byte[] fireBytes = new C_Fire().Serialize();
                await stream.WriteAsync(fireBytes, ct);
            }

            await Task.Delay(50, ct);                             // 20Hz
        }
    }
    catch (OperationCanceledException) { }
    catch (ObjectDisposedException) { }
    catch (Exception ex)
    {
        Console.WriteLine($"  [{nickname}] input error: {ex.Message}");
    }
    finally
    {
        udp?.Dispose();
    }
}

// 시나리오: 4명 차곡차곡 로그인 → 큐 채워 매치 시작
Console.WriteLine("=== Step 1: 1~4번 로그인 (큐 채우기) ===");
var clients = new List<(TcpClient client, string nick)>();
var listenCts = new CancellationTokenSource();
var listeners = new List<Task>();
var inputers = new List<Task>();
for (int i = 1; i <= 4; i++)
{
    string nick = $"p{i}";
    var login = await LoginAsync(nick);
    if (login != null)
    {
        var (c, token) = login.Value;
        clients.Add((c, nick));
        var tcs = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
        listeners.Add(ListenLoopAsync(c, nick, tcs, listenCts.Token));
        inputers.Add (InputLoopAsync (c, nick, token, tcs, listenCts.Token));
    }
    await Task.Delay(100); // 로그 순서 보기 좋게
}

// 5번째 — 매치 진행 중이라 거부 예상
await Task.Delay(500);
Console.WriteLine("\n=== Step 2: 5번째 (MATCH_IN_PROGRESS 거부 예상) ===");
var login5 = await LoginAsync("p5");
login5?.client.Dispose();

// 매칭/게임 시작 후 10초간 인게임 시뮬 (입력/사격 송신 + S_Snapshot/S_HitResult 수신)
// 풀 사이클 검증용: best-of-3 + 라운드 사이 3초 대기 → 최대 ~30초
Console.WriteLine("\n=== Step 3: 인게임 시뮬 30초 (풀 사이클) ===");
await Task.Delay(30000);

Console.WriteLine("\n[Bot] 정리 (모든 연결 종료)");
listenCts.Cancel();
foreach (var (c, _) in clients) c.Dispose();
try { await Task.WhenAll(listeners); } catch { }
try { await Task.WhenAll(inputers);  } catch { }
