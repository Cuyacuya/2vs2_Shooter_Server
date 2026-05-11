// 프레이밍 + C_Login 검증 봇
using System.Net.Sockets;
using Shared;

const string Host = "127.0.0.1";
const int Port = 7777;

// ─── Test1: C_Login 정상 1개 ──────────────────────
{
    Console.WriteLine("\n=== [Test1] C_Login 1개 ===");
    using var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();

    var login = new C_Login { Nickname = "Alice" };
    byte[] pkt = login.Serialize();
    await stream.WriteAsync(pkt);
    Console.WriteLine($"  sent {pkt.Length} bytes");
    await Task.Delay(300);
}

// ─── Test2: 합쳐짐 (C_Login 2개) ──────────────────
{
    Console.WriteLine("\n=== [Test2] 합쳐짐 (2개) ===");
    using var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();

    byte[] a = new C_Login { Nickname = "Bob" }.Serialize();
    byte[] b = new C_Login { Nickname = "Charlie" }.Serialize();
    byte[] merged = new byte[a.Length + b.Length];
    Buffer.BlockCopy(a, 0, merged, 0, a.Length);
    Buffer.BlockCopy(b, 0, merged, a.Length, b.Length);

    await stream.WriteAsync(merged);
    Console.WriteLine($"  sent {merged.Length} bytes");
    await Task.Delay(300);
}

// ─── Test3: 쪼개짐 (1바이트씩) ────────────────────
{
    Console.WriteLine("\n=== [Test3] 쪼개짐 ===");
    using var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();

    byte[] pkt = new C_Login { Nickname = "Dave" }.Serialize();
    for (int i = 0; i < pkt.Length; i++)
    {
        await stream.WriteAsync(pkt.AsMemory(i, 1));
        await Task.Delay(20);
    }
    Console.WriteLine($"  sent {pkt.Length} bytes (1 byte at a time)");
    await Task.Delay(300);
}

Console.WriteLine("\n[Bot] 종료");
