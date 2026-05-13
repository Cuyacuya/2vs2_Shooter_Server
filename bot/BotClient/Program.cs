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

    while(true)
    {
        int read = await networkStream.ReadAsync(buf.AsMemory(writePos), ct);
        if(read == 0) return null;
        writePos += read;

        if(writePos < 6) continue;
        ushort len = BitConverter.ToUInt16(buf, 0);
        int total = 6 + len;
        if(writePos < total) continue;

        var ms = new MemoryStream(buf, 0, total);
        var br = new BinaryReader(ms);
        var(length, id, token) = PacketIO.ReadHeader(br);
        return (id, br, ms);
    }
}

  // 로그인 시도 → 응답 출력
static async Task LoginAndCheck(string nickname)
{
    Console.WriteLine($"\n=== Login as '{nickname}' ===");
    using var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();

    // 송신
    byte[] pkt = new C_Login { Nickname = nickname }.Serialize();
    await stream.WriteAsync(pkt);
    Console.WriteLine($"  → C_Login sent ({pkt.Length} bytes)");

    // 수신 (2초 타임아웃)
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var result = await ReadOnePacketAsync(stream, cts.Token);
    if (result is null)
    {
        Console.WriteLine("  ← (no response)");
        return;
    }
    var (id, br, ms) = result.Value;
    if ((PacketId)id == PacketId.S_LoginResult)
    {
        var r = S_LoginResult.Deserialize(br);
        Console.WriteLine($"  ← S_LoginResult: success={r.Success}, token={r.SessionToken}, reason='{r.Reason}'");
    }
    else
    {
        Console.WriteLine($"  ← unexpected packetId={id}");
    }
    ms.Dispose();
    br.Dispose();
}

// 시나리오 실행
await LoginAndCheck("cuya");           // 정상
await LoginAndCheck("alice");          // 정상 (토큰 증가 확인)
await LoginAndCheck("");               // 거부 (빈 닉네임)
await LoginAndCheck(new string('x', 20)); // 거부 (너무 김)

Console.WriteLine("\n[Bot] 종료");
