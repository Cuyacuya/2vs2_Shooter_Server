// 7777포트에서 여러 클라의 TCP접속을 동시에 받는 서버 골격
using System.Net;          // IPAddress
using System.Net.Sockets;  // TcpListener, TcpClient
using Shared;              // PacketIO

const int Port = 7777;

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();
Console.WriteLine($"[Server] Listening on {Port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    Console.WriteLine($"[Server] Client connected: {client.Client.RemoteEndPoint}");

    _ = HandleClientAsync(client); // _ = : Task를 기다리지 않겠다 (fire-and-forget)
}

static async Task HandleClientAsync(TcpClient client)
{
    var endpoint = client.Client.RemoteEndPoint;
    NetworkStream stream = client.GetStream();

    byte[] buffer = new byte[8192];   // 세션별 누적 버퍼
    int writePos = 0;                  // 지금까지 쌓인 바이트 수

    try
    {
        while (true)
        {
            // ─── [1] 수신: 버퍼 빈 공간에 이어붙임 ─────────────────
            int read = await stream.ReadAsync(buffer, writePos, buffer.Length - writePos);
            if (read == 0)
            {
                Console.WriteLine($"[Server] {endpoint} closed by peer");
                break;
            }
            writePos += read;

            // ─── [2] 가능한 만큼 패킷을 잘라낸다 (합쳐짐 대응) ─────
            int readPos = 0;
            while (true)
            {
                if (writePos - readPos < 6) break;

                ushort length = BitConverter.ToUInt16(buffer, readPos);
                int totalSize = 6 + length;

                if (totalSize > buffer.Length)
                {
                    Console.WriteLine($"[Server] {endpoint} packet too large: {totalSize}");
                    return;
                }

                if (writePos - readPos < totalSize) break;

                using var ms = new MemoryStream(buffer, readPos, totalSize);
                using var br = new BinaryReader(ms);
                var (len, id, token) = PacketIO.ReadHeader(br);
                Console.WriteLine($"[Recv] id={id}, len={len}, token={token}");

                readPos += totalSize;
            }

            // ─── [3] 소비한 만큼 버퍼 앞으로 당기기 ───────────────
            if (readPos > 0)
            {
                int remain = writePos - readPos;
                if (remain > 0)
                    Buffer.BlockCopy(buffer, readPos, buffer, 0, remain);
                writePos = remain;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Server] {endpoint} error: {ex.Message}");
    }
    finally
    {
        client.Close();
        Console.WriteLine($"[Server] {endpoint} disconnected");
    }
}
