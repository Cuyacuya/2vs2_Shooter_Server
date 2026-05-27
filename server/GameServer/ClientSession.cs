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

        // 클라가 C_UdpHello 보낸 시점에 기록되는 UDP 발신 endpoint.
        // null이면 UDP 핸드셰이크 아직 안 됨 → UdpServer.SendTo 가 송신 무시.
        public System.Net.IPEndPoint? UdpEndPoint { get; set; }

        // UdpServer가 C_Input 받으면 호출. 3주차부터는 즉시 처리 X, 큐에 쌓기만.
        // TickServer가 30Hz로 큐를 비우며 일괄 시뮬레이션.
        public void HandleInputFromUdp(C_Input pkt) => Player.InputQueue.Enqueue(pkt);

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
                        // 정상 경로는 UDP. TCP로 와도 큐잉 (방어적, 거의 안 옴)
                        var pkt = C_Input.Deserialize(br);
                        Player.InputQueue.Enqueue(pkt);
                        break;
                    }
                case PacketId.C_Fire:
                    {
                        var pkt = C_Fire.Deserialize(br);
                        HandleFire(pkt);
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

        // TickServer가 30Hz로 호출. 큐의 마지막 입력만 적용 + 물리 시뮬은 매 틱 무조건.
        // dt는 고정 (1/30초) — 점프 궤적 재현성 보장.
        public void SimulateOneTick(float dt)
        {
            // 1) 큐 비우기 — 마지막 1개만 유지 (한 틱 안 키 상태 변동은 최종 의도만 반영)
            C_Input? lastInput = null;
            while (Player.InputQueue.TryDequeue(out var pkt)) lastInput = pkt;

            float speed    = Balance.Current.Player.MoveSpeed;
            float pitchMin = Balance.Current.Player.PitchMinDeg;
            float pitchMax = Balance.Current.Player.PitchMaxDeg;
            const float DEG2RAD = MathF.PI / 180f;

            lock (Player.Lock)
            {
                if (Player.IsDead) return;

                bool jump = false;

                // 2) 입력 적용 (있을 때만)
                if (lastInput != null)
                {
                    bool w = (lastInput.InputBits & (1 << 0)) != 0;
                    bool s = (lastInput.InputBits & (1 << 1)) != 0;
                    bool a = (lastInput.InputBits & (1 << 2)) != 0;
                    bool d = (lastInput.InputBits & (1 << 3)) != 0;
                    jump   = (lastInput.InputBits & (1 << 4)) != 0;

                    // 시점
                    Player.Yaw   = lastInput.Yaw;
                    Player.Pitch = Math.Clamp(lastInput.Pitch, pitchMin, pitchMax);

                    // 이동 벡터 + 대각선 정규화
                    float moveX = (d ? 1f : 0f) + (a ? -1f : 0f);
                    float moveZ = (w ? 1f : 0f) + (s ? -1f : 0f);
                    float len = MathF.Sqrt(moveX * moveX + moveZ * moveZ);
                    if (len > 0f) { moveX /= len; moveZ /= len; }

                    // yaw 회전 → 월드 좌표
                    float yawRad = Player.Yaw * DEG2RAD;
                    float cosY = MathF.Cos(yawRad);
                    float sinY = MathF.Sin(yawRad);
                    float worldX =  moveX * cosY + moveZ * sinY;
                    float worldZ = -moveX * sinY + moveZ * cosY;

                    Player.PosX += worldX * speed * dt;
                    Player.PosZ += worldZ * speed * dt;
                }

                // 3) 점프 트리거 (지면 위 + 점프 비트)
                if (jump && Player.IsGrounded)
                {
                    Player.VelocityY = Balance.Current.Player.JumpVelocity;
                    Player.IsGrounded = false;
                }

                // 4) 물리 시뮬 (입력 유무 관계없이 매 틱)
                Player.VelocityY += Balance.Current.Physics.Gravity * dt;
                Player.PosY      += Player.VelocityY * dt;

                // 5) 지면 클램프
                float groundY = Balance.Current.Physics.GroundY;
                if (Player.PosY <= groundY)
                {
                    Player.PosY = groundY;
                    Player.VelocityY = 0f;
                    Player.IsGrounded = true;
                }
            }
            // BroadcastSnapshot은 TickServer가 틱 끝에 1회만 호출 (수요일은 30Hz)
        }

        private void HandleFire(C_Fire pkt)
        {
            const float DEG2RAD = MathF.PI / 180f;
            float radius = Balance.Current.Weapon.HitscanSphereRadius;
            byte damage  = Balance.Current.Weapon.Damage;

            // 1) attacker 위치/시점 스냅샷 (lock 짧게)
            float ox, oy, oz, yaw, pitch;
            bool attackerDead;
            lock (Player.Lock)
            {
                attackerDead = Player.IsDead;
                ox = Player.PosX; oy = Player.PosY; oz = Player.PosZ;
                yaw = Player.Yaw; pitch = Player.Pitch;
            }
            if (attackerDead) return;

            // 2) yaw/pitch → forward 단위벡터
            float yawR = yaw * DEG2RAD;
            float pitR = pitch * DEG2RAD;
            float dx = MathF.Sin(yawR) * MathF.Cos(pitR);
            float dy = MathF.Sin(pitR);
            float dz = MathF.Cos(yawR) * MathF.Cos(pitR);

            // 3) 적팀 중 ray-sphere로 가장 가까운 1명
            var sessions = MatchManager.Instance.GetMatchSnapshot();
            ClientSession? hitSession = null;
            float bestT = float.MaxValue;
            float rSq = radius * radius;

            foreach (var enemy in sessions)
            {
                if (enemy == this) continue;
                if (enemy.Team == this.Team) continue;

                float ex, ey, ez;
                bool eDead;
                lock (enemy.Player.Lock)
                {
                    eDead = enemy.Player.IsDead;
                    ex = enemy.Player.PosX; ey = enemy.Player.PosY; ez = enemy.Player.PosZ;
                }
                if (eDead) continue;

                float lx = ex - ox, ly = ey - oy, lz = ez - oz;
                float tca = lx * dx + ly * dy + lz * dz;
                if (tca < 0) continue;                       // ray 뒤쪽

                float dSq = (lx * lx + ly * ly + lz * lz) - tca * tca;
                if (dSq > rSq) continue;                     // 빗나감

                float t = tca - MathF.Sqrt(rSq - dSq);
                if (t < 0) continue;                         // 시작점이 구 내부
                if (t < bestT) { bestT = t; hitSession = enemy; }
            }

            if (hitSession == null)
            {
                Console.WriteLine($"[Fire] {Nickname} -> miss");
                return;
            }

            // 4) HP 감소 + 사망 판정
            byte hpAfter; byte isKill;
            lock (hitSession.Player.Lock)
            {
                int newHp = hitSession.Player.Hp - damage;
                if (newHp <= 0)
                {
                    hitSession.Player.Hp = 0;
                    hitSession.Player.IsDead = true;
                    isKill = 1;
                }
                else
                {
                    hitSession.Player.Hp = (byte)newHp;
                    isKill = 0;
                }
                hpAfter = hitSession.Player.Hp;
            }

            // 5) S_HitResult 4명 전원 브로드캐스트
            var hit = new S_HitResult
            {
                AttackerToken = this.SessionToken,
                VictimToken   = hitSession.SessionToken,
                Damage        = damage,
                VictimHpAfter = hpAfter,
                IsKill        = isKill,
            };
            byte[] bytes = hit.Serialize();
            foreach (var s in sessions)
                _ = s.SendAsync(bytes);

            Console.WriteLine($"[Fire] {Nickname} -> {hitSession.Nickname} dmg={damage} hp={hpAfter} kill={isKill}");

            // 피격 직후 HP/사망 상태 즉시 동기화
            BroadcastSnapshot();
        }

        // 인게임 4명의 현재 상태를 한 패킷에 모아 전원에게 송신.
        // 3주차부터는 TickServer가 틱 끝에 1회 호출. HandleFire(피격 시)에서도 즉시 호출.
        public static void BroadcastSnapshot()
        {
            var sessions = MatchManager.Instance.GetMatchSnapshot();
            if (sessions.Count == 0) return;

            var snaps = new List<PlayerSnapshot>(sessions.Count);
            foreach (var s in sessions)
            {
                lock (s.Player.Lock)
                {
                    snaps.Add(new PlayerSnapshot
                    {
                        Token     = s.SessionToken,
                        PosX      = s.Player.PosX,
                        PosY      = s.Player.PosY,
                        PosZ      = s.Player.PosZ,
                        Yaw       = s.Player.Yaw,
                        Pitch     = s.Player.Pitch,
                        Hp        = s.Player.Hp,
                        StateBits = (byte)(s.Player.IsDead ? 1 : 0),
                    });
                }
            }

            var pkt = new S_Snapshot { Players = snaps };
            byte[] bytes = pkt.Serialize();
            foreach (var s in sessions)
                _ = s.SendAsync(bytes);
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