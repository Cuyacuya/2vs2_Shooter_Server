using System.Net.Sockets;
using Shared;

namespace GameServer
{
    // TCP 클라 1개 = ClientSession 1개.
    // - 수신 루프 (누적 버퍼 + length-prefixed 프레이밍)
    // - 송신 (SendAsync, 동시 호출 직렬화)
    // - 세션 상태 (토큰, 닉네임 등)
    public class ClientSession
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly byte[] _buffer = new byte[8192];
        private int _writePos = 0;

        // 송신은 여러 곳에서 동시에 호출될 수 있으므로 직렬화 (인터리브 방지)
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public string Endpoint { get; }
        public ushort SessionToken { get; set; } = 0;
        public string Nickname { get; set; } = "";

        public ClientSession(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
            Endpoint = client.Client.RemoteEndPoint?.ToString() ?? "?";
        }

        // 수신 루프. fire-and-forget으로 Program.cs에서 _ = RunAsync() 호출.
        public async Task RunAsync()
        {
            try
            {
                while (true)
                {
                    int read = await _stream.ReadAsync(_buffer, _writePos, _buffer.Length - _writePos);
                    if (read == 0)
                    {
                        Console.WriteLine($"[Session {Endpoint}] closed by peer");
                        break;
                    }
                    _writePos += read;

                    ParsePackets();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {Endpoint}] error: {ex.Message}");
            }
            finally
            {
                _client.Close();
                Console.WriteLine($"[Session {Endpoint}] disconnected");
            }
        }

        // 누적 버퍼에서 완성된 패킷을 가능한 만큼 잘라 디스패치.
        private void ParsePackets()
        {
            int readPos = 0;
            while (true)
            {
                if (_writePos - readPos < 6) break;

                ushort length = BitConverter.ToUInt16(_buffer, readPos);
                int totalSize = 6 + length;

                if (totalSize > _buffer.Length)
                {
                    Console.WriteLine($"[Session {Endpoint}] packet too large: {totalSize}");
                    _client.Close();
                    return;
                }

                if (_writePos - readPos < totalSize) break;

                HandlePacket(_buffer, readPos, totalSize);
                readPos += totalSize;
            }

            // 소비한 만큼 앞으로 당김
            if (readPos > 0)
            {
                int remain = _writePos - readPos;
                if (remain > 0)
                    Buffer.BlockCopy(_buffer, readPos, _buffer, 0, remain);
                _writePos = remain;
            }
        }

        // PacketId 디스패치 (지금은 로그만, 목요일에 실제 핸들러 채움)
        private void HandlePacket(byte[] buffer, int offset, int totalSize)
        {
            using var ms = new MemoryStream(buffer, offset, totalSize);
            using var br = new BinaryReader(ms);
            var (len, id, token) = PacketIO.ReadHeader(br);

            switch ((PacketId)id)
            {
                case PacketId.C_Login:
                {
                    var pkt = C_Login.Deserialize(br);
                    Console.WriteLine($"[Session {Endpoint}] C_Login nickname={pkt.Nickname}");
                    // TODO(목): sessionToken 발급 + S_LoginResult 응답
                    break;
                }
                default:
                    Console.WriteLine($"[Session {Endpoint}] unknown packetId={id}");
                    break;
            }
        }

        // 외부에서 이 세션으로 패킷 보낼 때 호출.
        public async Task SendAsync(byte[] packetBytes)
        {
            await _sendLock.WaitAsync();
            try
            {
                await _stream.WriteAsync(packetBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {Endpoint}] send error: {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
