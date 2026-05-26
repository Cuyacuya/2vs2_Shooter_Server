using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shared;
using System.Diagnostics; //Stopwatch

namespace GameServer
{
    //세션 객체화 하는 스크립트
    // TCP 클라 1개 = ClientSession 1개.
    // - 수신 루프 (누적 버퍼 + length-prefixed 프레이밍)
    // - 송신 (SendAsync, 동시 호출 직렬화)
    // - 세션 상태 (토큰, 닉네임 등)
    public class ClientSession
    {
        private static int _nextToken = 0; //모든 세션이 공유하는 토큰 카운터(순차 발급)
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly byte[] _buffer = new byte[8192]; //누적 버퍼
        private int _writePos = 0; //이터레이터

        // 송신은 여러 곳에서 동시에 호출될 수 있으므로 직렬화 (인터리브 방지)
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public string Endpoint { get; }
        public ushort SessionToken { get; set; } = 0;
        public string Nickname { get; set; } = "";
        public byte Team { get; set; } = 0;  // 0=Red, 1=Blue. StartMatch에서 세팅
        public PlayerState Player { get; } = new();

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
                    //누적 버퍼에 바이트 쌓기
                    int read = await _stream.ReadAsync(_buffer, _writePos, _buffer.Length - _writePos);
                    if (read == 0)
                    {
                        Console.WriteLine($"[Session {Endpoint}] closed by peer");
                        break;
                    }
                    _writePos += read;

                    await ParsePackets();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session {Endpoint}] error: {ex.Message}");
            }
            finally
            {
                MatchManager.Instance.Remove(this);
                _client.Close();
                Console.WriteLine($"[Session {Endpoint}] disconnected");
            }
        }

        // 누적 버퍼에서 완성된 패킷을 가능한 만큼 잘라 디스패치.
        private async Task ParsePackets()
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

                await HandlePacket(_buffer, readPos, totalSize);
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
        private async Task HandlePacket(byte[] buffer, int offset, int totalSize)
        {
            using var ms = new MemoryStream(buffer, offset, totalSize);
            using var br = new BinaryReader(ms);
            var (len, id, token) = PacketIO.ReadHeader(br);

            switch ((PacketId)id)
            {
                case PacketId.C_Login:
                    {
                        var pkt = C_Login.Deserialize(br);
                        await HandleLogin(pkt);
                        break;
                    }
                case PacketId.C_Input:
                    {
                        var pkt = C_Input.Deserialize(br);
                        HandleInput(pkt);
                        break;
                    }
                default:
                    Console.WriteLine($"[Session {Endpoint}] unknown packetId={id}");
                    break;
            }
        }

        private async Task HandleLogin(C_Login pkt)
        {
            //닉네임 검증(빈 문자열 or 너무 긴 닉네임)
            if (string.IsNullOrWhiteSpace(pkt.Nickname) || pkt.Nickname.Length > 16)
            {
                var fail = new S_LoginResult
                {
                    Success = false,
                    SessionToken = 0,
                    Reason = "INVALID_NICKNAME",
                };
                await SendAsync(fail.Serialize());
                Console.WriteLine($"[Session {Endpoint}] login rejected : invalid nickname='{pkt.Nickname}'");
                return;
            }

            //토큰 발급(스레드 안전)
            ushort newToken = (ushort)Interlocked.Increment(ref _nextToken);

            //세션 상태 업데이트
            SessionToken = newToken;
            Nickname = pkt.Nickname;

            //매칭 큐 등록 시도 (브로드캐스트는 아직 X — snapshot만 받음)
            bool joined = MatchManager.Instance.TryEnqueue(this, out var statusSnap, out var gameStartSnap);
            if (!joined)
            {
                var fail = new S_LoginResult
                {
                    Success = false,
                    SessionToken = 0,
                    Reason = "MATCH_IN_PROGRESS",
                };
                await SendAsync(fail.Serialize());
                Console.WriteLine($"[Session {Endpoint}] login rejected : match in progress");
                return;
            }

            //성공 응답 — S_LoginResult를 먼저 보낸 뒤에야 매칭 상태/게임 시작 패킷 송신
            var ok = new S_LoginResult
            {
                Success = true,
                SessionToken = newToken,
                Reason = "",
            };
            await SendAsync(ok.Serialize());
            Console.WriteLine($"[Session {Endpoint}] login ok : nickname={Nickname}, token={newToken}");

            //응답 이후 브로드캐스트 (순서 보장: S_LoginResult → S_MatchingStatus 또는 S_GameStart)
            if (statusSnap != null)
                MatchManager.Instance.BroadcastStatusNow(statusSnap);
            if (gameStartSnap != null)
                MatchManager.Instance.BroadcastGameStartNow(gameStartSnap);
        }

        private void HandleInput(C_Input pkt)
        {
            // 밸런스값(이동속도, pitch 범위) = balance.json에서 로드. 디자이너 튜닝 대상.
            // 수학 상수(DEG2RAD) = 코드 const. 절대 안 바뀜.
            float speed = Balance.Current.Player.MoveSpeed;
            float pitchMin = Balance.Current.Player.PitchMinDeg;
            float pitchMax = Balance.Current.Player.PitchMaxDeg;
            const float DEG2RAD = MathF.PI / 180f;

            lock (Player.Lock)
            {
                if (Player.IsDead) return;

                //dt 계산
                long nowTicks = Stopwatch.GetTimestamp();
                float dt;
                if (Player.LastInputTicks == 0)
                {
                    dt = 0f;
                }
                else
                {
                    dt = (float)((nowTicks - Player.LastInputTicks) / (double)Stopwatch.Frequency);
                    if (dt > 0.1f) dt = 0.1f; // 비정상적 큰 dt 방어 (네트워크끊김 등)
                }
                Player.LastInputTicks = nowTicks;

                // 비트마스크 풀기 (WASD만, 점프는 수요일 작업)
                bool w = (pkt.InputBits & (1 << 0)) != 0;
                bool s = (pkt.InputBits & (1 << 1)) != 0;
                bool a = (pkt.InputBits & (1 << 2)) != 0;
                bool d = (pkt.InputBits & (1 << 3)) != 0;

                // raw 이동 벡터 (캐릭터 로컬 좌표)
                float moveX = (d ? 1f : 0f) + (a ? -1f : 0f);
                float moveZ = (w ? 1f : 0f) + (s ? -1f : 0f);

                // 대각선 정규화 (W+D 동시에 √2 빨라지지 않도록)
                float len = MathF.Sqrt(moveX * moveX + moveZ * moveZ);
                if (len > 0f)
                {
                    moveX /= len;
                    moveZ /= len;
                }

                // 시점 갱신 (pitch 서버측 클램프 검증)
                Player.Yaw = pkt.Yaw;
                Player.Pitch = Math.Clamp(pkt.Pitch, pitchMin, pitchMax);

                // yaw 회전 적용 → 월드 좌표 이동량
                float yawRad = Player.Yaw * DEG2RAD;
                float cosY = MathF.Cos(yawRad);
                float sinY = MathF.Sin(yawRad);
                float worldX = moveX * cosY + moveZ * sinY;
                float worldZ = -moveX * sinY + moveZ * cosY;

                // 위치 갱신 (Y축 = 점프/중력은 수요일)
                Player.PosX += worldX * speed * dt;
                Player.PosZ += worldZ * speed * dt;

                // 점프 비트(bit4)
                bool jump = (pkt.InputBits & (1 << 4)) != 0;

                // 점프 입력: 지면에 있을 때만 한 번 위로 가속
                if (jump && Player.IsGrounded)
                {
                    Player.VelocityY = Balance.Current.Player.JumpVelocity;
                    Player.IsGrounded = false;
                }

                // 중력 적분 → Y 위치 적분
                Player.VelocityY += Balance.Current.Physics.Gravity * dt;
                Player.PosY += Player.VelocityY * dt;

                // 지면 클램프
                float groundY = Balance.Current.Physics.GroundY;
                if (Player.PosY <= groundY)
                {
                    Player.PosY = groundY;
                    Player.VelocityY = 0f;
                    Player.IsGrounded = true;
                }
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