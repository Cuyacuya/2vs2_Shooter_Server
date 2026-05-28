using System.Net;
using System.Net.Sockets;
using Shared;

namespace GameServer
{
    // UDP 채널 (포트 7778). 싱글톤.
    // - 받기: 비동기 수신 루프. C_UdpHello로 endpoint 매핑, C_Input은 ClientSession로 위임.
    // - 보내기: SendTo(token, bytes)로 매핑된 endpoint에 datagram 송신.
    public class UdpServer
    {
        private static readonly UdpServer _instance = new();
        public static UdpServer Instance => _instance;

        private UdpClient? _udp;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private UdpServer() { }

        public void Start(int port)
        {
            _udp = new UdpClient(port);
            Console.WriteLine($"[Udp] Listening on {port}");
            _ = RunAsync();   // fire-and-forget
        }

        private async Task RunAsync()
        {
            if (_udp == null) return;
            try
            {
                while (true)
                {
                    var result = await _udp.ReceiveAsync();
                    HandleDatagram(result.Buffer, result.RemoteEndPoint);
                }
            }
            catch (ObjectDisposedException) { /* 정상 종료 */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[Udp] receive loop error: {ex.Message}");
            }
        }

        private void HandleDatagram(byte[] buf, IPEndPoint sender)
        {
            // 1) 헤더 검증
            if (buf.Length < 6) return;
            using var ms = new MemoryStream(buf);
            using var br = new BinaryReader(ms);
            var (length, id, token) = PacketIO.ReadHeader(br);
            if (buf.Length != 6 + length) return;   // 손상/불일치

            // 2) 세션 매칭
            if (!MatchManager.Instance.TryGetSession(token, out var session) || session == null)
                return;   // 모르는 sessionToken (위조 또는 매치 전)

            // 3) packetId 디스패치
            switch ((PacketId)id)
            {
                case PacketId.C_UdpHello:
                    session.UdpEndPoint = sender;
                    Console.WriteLine($"[Udp] hello from {session.Nickname} token={token} ep={sender}");
                    break;

                case PacketId.C_Input:
                    // endpoint 위조 거부 (첫 hello 이후 다른 IP로 오는 입력 무시)
                    if (session.UdpEndPoint == null || !session.UdpEndPoint.Equals(sender)) return;
                    var pkt = C_Input.Deserialize(br);
                    session.HandleInputFromUdp(pkt);
                    break;

                default:
                    // UDP로 와선 안 되는 패킷 (C_Login 등) → 무시
                    break;
            }
        }

        // 외부에서 sessionToken에게 UDP 송신. 매핑 안 된 세션은 무시.
        public async Task SendTo(ClientSession session, byte[] data)
        {
            if (_udp == null) return;
            var ep = session.UdpEndPoint;
            if (ep == null) return;

            await _sendLock.WaitAsync();
            try
            {
                await _udp.SendAsync(data, data.Length, ep);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Udp] send error to {session.Nickname}: {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
