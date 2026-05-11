// 7777포트에서 TCP 접속을 받아 각 클라마다 ClientSession을 만들어 위임한다.
using System.Net;
using System.Net.Sockets;
using GameServer;

const int Port = 7777;

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();
Console.WriteLine($"[Server] Listening on {Port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    var session = new ClientSession(client);
    Console.WriteLine($"[Server] Client connected: {session.Endpoint}");

    // 세션 수신 루프를 백그라운드로 (fire-and-forget)
    _ = session.RunAsync();
}
