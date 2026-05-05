//7777포트에서 여러 클라의 TCP접속을 동시에 받는 서버 골격
using System.Net;          // IPAddress
using System.Net.Sockets;  // TcpListener, TcpClient

const int Port = 7777;

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();
Console.WriteLine($"[Server] Listening on {Port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    Console.WriteLine($"[Server] Client connected: {client.Client.RemoteEndPoint}");

    _ = HandleClientAsync(client); //_ =  : Task를 기다리지 않겠다
}

static async Task HandleClientAsync(TcpClient client)
{
    try
    {
        // 1주차 화/수요일에 여기를 NetworkStream.ReadAsync 루프로 교체 예정
        // 지금은 접속만 유지하는 골격
        await Task.Delay(Timeout.Infinite);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Server] Client error: {ex.Message}");
    }
    finally
    {
        client.Close();
        Console.WriteLine("[Server] Client disconnected");
        }
}


//Task : 미래에 끝날 작업
//Task + async/await는 "기다리는 동안 스레드를 반납
//   1. Task vs Thread 차이
//     - Thread: OS가 관리하는 실행 단위. 비쌈
//     - Task: "할 일"의 추상화. 여러 Task가 적은 수의 스레드를 공유 (ThreadPool)
//   2. async/await 문법 규칙
//     - async 붙은 함수는 보통 Task 또는 Task<T> 반환
//     - await는 async 함수 안에서만 사용 가능
//     - await는 "기다린다"가 아니라 "여기서 잠깐 함수를 중단하고, 끝나면 이어서 실행"
//   3. _ = SomeTaskAsync() (fire-and-forget)
//     - 결과 안 기다리고 백그라운드로 던짐
//     - 우리 코드에서 클라마다 이걸 씀
//   4. 예외 처리
//     - await 시점에 Task가 던진 예외가 다시 throw됨
//     - fire-and-forget Task는 예외를 못 잡으면 사라짐 → 우리가 try/catch 넣은 이유