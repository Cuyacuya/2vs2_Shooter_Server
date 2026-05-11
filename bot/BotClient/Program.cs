// 프레이밍 검증 봇: 정상 / 합쳐짐 / 쪼개짐 케이스 자동 테스트
using System.Net.Sockets;
using Shared;

const string Host = "127.0.0.1";
const int Port = 7777;

static byte[] MakePacket(ushort packetId, ushort sessionToken, string nickname)
{
    using var msPayload = new MemoryStream();
    using var bwPayload = new BinaryWriter(msPayload);
    PacketIO.WriteString(bwPayload, nickname);
    byte[] payload = msPayload.ToArray();

    using var msFull = new MemoryStream();
    using var bwFull = new BinaryWriter(msFull);
    PacketIO.WriteHeader(bwFull, packetId, sessionToken, (ushort)payload.Length);
    bwFull.Write(payload);
    return msFull.ToArray();
}

// ─── Test1: 정상 1개 ──────────────────────────────
{
    Console.WriteLine("\n=== [Test1] 정상 1개 패킷 ===");
    using var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();

    byte[] pkt = MakePacket(1, 0, "Alice");
    await stream.WriteAsync(pkt);
    Console.WriteLine($"  sent {pkt.Length} bytes");
    await Task.Delay(300);
}

// ─── Test2: 합쳐짐 — 2개를 한 번에 ────────────────
{
    Console.WriteLine("\n=== [Test2] 합쳐짐 (2패킷 1번에) ===");
    using var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();

    byte[] a = MakePacket(1, 0, "Bob");
    byte[] b = MakePacket(1, 0, "Charlie");
    byte[] merged = new byte[a.Length + b.Length];
    Buffer.BlockCopy(a, 0, merged, 0, a.Length);
    Buffer.BlockCopy(b, 0, merged, a.Length, b.Length);

    await stream.WriteAsync(merged);
    Console.WriteLine($"  sent {merged.Length} bytes (2 packets combined)");
    await Task.Delay(300);
}

// ─── Test3: 쪼개짐 — 1바이트씩 ────────────────────
{
    Console.WriteLine("\n=== [Test3] 쪼개짐 (1바이트씩) ===");
    using var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();

    byte[] pkt = MakePacket(1, 0, "Dave");
    for (int i = 0; i < pkt.Length; i++)
    {
        await stream.WriteAsync(pkt.AsMemory(i, 1));
        await Task.Delay(20);
    }
    Console.WriteLine($"  sent {pkt.Length} bytes (1 byte at a time)");
    await Task.Delay(300);
}

Console.WriteLine("\n[Bot] 종료");
