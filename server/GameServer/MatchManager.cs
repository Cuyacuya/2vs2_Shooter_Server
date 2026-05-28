using System.Diagnostics;
using Shared;

namespace GameServer
{
    //자동 매칭 큐(싱글톤, FIFO, 단일 매치)
    public class MatchManager
    {
        //싱글톤
        private static readonly MatchManager _instance = new();
        public static MatchManager Instance => _instance;

        private const int MaxPlayers = 4;

        private readonly List<ClientSession> _waiting = new();
        private readonly object _lock = new();
        private bool _matchInProgress = false;

        // 3주차 목~금: 라운드 사이클 + 매치 종료
        private byte _redScore = 0;
        private byte _blueScore = 0;
        private byte _roundIndex = 1;       // 진행 중 라운드 (1, 2, 3, ...)
        private bool _roundEnded = false;   // 같은 라운드 두 번 종료 처리 방지
        private long _nextRoundDeadlineTicks = 0;   // PostRound 대기 종료 시각
        private bool _matchEnded = false;   // 매치 끝나면 더 이상 라운드 시작 안 함
        private const int WinScore = 2;     // best-of-3 → 2승 선취
        private const double PostRoundDelaySec = 3.0;

        //외부에서 new 못 하게 막음 (싱글톤 강제)
        private MatchManager() { }

        //큐에 등록 시도. 매치 진행 중이면 false, 아니면 true.
        //브로드캐스트는 안 함. 호출자가 응답을 먼저 보낸 뒤 snapshot으로 BroadcastStatusNow/BroadcastGameStartNow 호출.
        public bool TryEnqueue(ClientSession session,
                               out List<ClientSession>? statusSnapshot,
                               out List<ClientSession>? gameStartSnapshot)
        {
            statusSnapshot = null;
            gameStartSnapshot = null;

            lock (_lock)
            {
                if (_matchInProgress)
                {
                    Console.WriteLine($"[Match] reject {session.Nickname}: match in progress");
                    return false;
                }

                _waiting.Add(session);
                Console.WriteLine($"[Match] {session.Nickname} joined queue({_waiting.Count}/{MaxPlayers})");

                if (_waiting.Count >= MaxPlayers)
                {
                    StartMatch();
                    gameStartSnapshot = new List<ClientSession>(_waiting);
                }
                else
                {
                    statusSnapshot = new List<ClientSession>(_waiting);
                }
                return true;
            }
        }

        // 호출자(HandleLogin)가 S_LoginResult 응답 후에 호출.
        public void BroadcastStatusNow(List<ClientSession> snapshot) => BroadcastStatus(snapshot);
        public void BroadcastGameStartNow(List<ClientSession> snapshot) => BroadcastGameStart(snapshot);

        // 세션 연결 종료 시 호출.
        // - 매칭 대기 중: 큐에서 제거 + S_MatchingStatus 갱신
        // - 인게임 중: 큐 유지 + IsDead=true (그 자리 시체 + 라운드 종료 판정에 반영)
        public void Remove(ClientSession session)
        {
            List<ClientSession>? statusSnap = null;

            lock (_lock)
            {
                if (_matchInProgress)
                {
                    // 인게임 끊김 — 사망 처리
                    if (_waiting.Contains(session))
                    {
                        lock (session.Player.Lock)
                        {
                            if (!session.Player.IsDead)
                            {
                                session.Player.IsDead = true;
                                session.Player.Hp = 0;
                                Console.WriteLine($"[Match] {session.Nickname} disconnected mid-game → 사망 처리");
                            }
                        }
                    }
                }
                else if (_waiting.Remove(session))
                {
                    Console.WriteLine($"[Match] {session.Nickname} left queue({_waiting.Count}/{MaxPlayers})");
                    statusSnap = new List<ClientSession>(_waiting);
                }
            }

            // 라운드 종료 판정은 다음 틱의 OnTickEnd가 자동으로 함 (IsDead 보고)
            if (statusSnap != null) BroadcastStatus(statusSnap);
        }

        // 대기 중 모두에게 현재 인원 알림. lock 밖에서만 호출.
        private void BroadcastStatus(List<ClientSession> sessions)
        {
            var pkt = new S_MatchingStatus
            {
                CurrentCount = (byte)sessions.Count,
                MaxCount = (byte)MaxPlayers,
            };
            byte[] bytes = pkt.Serialize();

            foreach (var s in sessions)
            {
                _ = s.SendAsync(bytes);
            }
        }

        //lock 안에서만 호출됨(private)
        private void StartMatch()
        {
            _matchInProgress = true;

            // 팀 배정 + 스폰 위치 부여
            // Red(team=0): -X쪽 / Blue(team=1): +X쪽. 팀원 둘은 Z 오프셋으로 분리.
            // 두 팀 마주봄 → 직진 이동만 해도 적과 교전 가능
            for (int i = 0; i < _waiting.Count; i++)
            {
                var s = _waiting[i];
                s.Team = (byte)(i % 2);

                int teamSlot = i / 2;                          // 0 또는 1 (팀 내 순번)
                s.Player.SessionToken = s.SessionToken;
                s.Player.Team         = s.Team;
                s.Player.PosX = s.Team == 0 ? -8f : 8f;        // Red 왼쪽 / Blue 오른쪽
                s.Player.PosY = 0f;
                s.Player.PosZ = teamSlot == 0 ? -2f : 2f;
                s.Player.Yaw  = s.Team == 0 ? 90f : 270f;      // 서로 바라봄
                s.Player.Hp   = Balance.Current.Player.InitialHp;
                s.Player.IsDead = false;
                s.Player.IsGrounded = true;
                s.Player.VelocityY = 0f;
            }

            Console.WriteLine($"[Match] === MATCH STARTING ({_waiting.Count} players) ===");
            foreach (var s in _waiting)
            {
                string teamName = s.Team == 0 ? "Red" : "Blue";
                Console.WriteLine($"  - {s.Nickname} (token={s.SessionToken}, team={teamName})");
            }
        }

        // 사격/입력 게이트: 라운드 진행 중일 때만 true.
        // PostRound(3초 대기) / 매치 종료 후엔 false → HandleFire가 거부.
        public bool IsRoundLive
        {
            get { lock (_lock) { return _matchInProgress && !_roundEnded && !_matchEnded; } }
        }

        // sessionToken으로 ClientSession 찾기. UDP 패킷 발신자 매칭에 사용.
        public bool TryGetSession(ushort sessionToken, out ClientSession? found)
        {
            lock (_lock)
            {
                foreach (var s in _waiting)
                {
                    if (s.SessionToken == sessionToken)
                    {
                        found = s;
                        return true;
                    }
                }
            }
            found = null;
            return false;
        }

        // TickServer가 매 틱 끝에 호출. 라운드 사이클 전체 관리.
        // [InProgress] 한 팀 전원 사망 → S_RoundEnd (또는 S_MatchEnd if 2승)
        // [PostRound]  3초 후 다음 라운드 시작 → 위치/HP 리셋 + S_RoundStart
        public void OnTickEnd()
        {
            // === 분기 1: 라운드 진행 중 → 종료 감지 ===
            List<ClientSession>? endTargets = null;
            byte endWinner = 0, endRed = 0, endBlue = 0;
            bool matchOver = false;

            lock (_lock)
            {
                if (!_matchInProgress || _matchEnded) return;

                if (!_roundEnded)
                {
                    int redAlive = 0, blueAlive = 0;
                    foreach (var s in _waiting)
                    {
                        bool dead;
                        lock (s.Player.Lock) dead = s.Player.IsDead;
                        if (!dead) { if (s.Team == 0) redAlive++; else blueAlive++; }
                    }

                    if (redAlive == 0 || blueAlive == 0)
                    {
                        endWinner = (byte)(redAlive == 0 ? 1 : 0);
                        if (endWinner == 0) _redScore++; else _blueScore++;
                        endRed = _redScore; endBlue = _blueScore;
                        _roundEnded = true;
                        endTargets = new List<ClientSession>(_waiting);

                        // 매치 종료 판정 (2승 선취)
                        if (_redScore >= WinScore || _blueScore >= WinScore)
                        {
                            _matchEnded = true;
                            matchOver = true;
                        }
                        else
                        {
                            // 다음 라운드 3초 후 시작
                            _nextRoundDeadlineTicks = Stopwatch.GetTimestamp()
                                + (long)(PostRoundDelaySec * Stopwatch.Frequency);
                        }
                    }
                }
            }

            if (endTargets != null)
            {
                string wName = endWinner == 0 ? "Red" : "Blue";
                if (matchOver)
                {
                    // 매치 종료: S_MatchEnd 1발만 송신 (라운드 결과는 점수로 자명)
                    var matchPkt = new S_MatchEnd
                    {
                        WinnerTeam = endWinner, RedScore = endRed, BlueScore = endBlue,
                    };
                    byte[] bytes = matchPkt.Serialize();
                    foreach (var s in endTargets) _ = s.SendAsync(bytes);
                    Console.WriteLine($"[Match] === ENDED === winner={wName} final={endRed}:{endBlue}");
                }
                else
                {
                    var roundPkt = new S_RoundEnd
                    {
                        WinnerTeam = endWinner, RedScore = endRed, BlueScore = endBlue,
                    };
                    byte[] bytes = roundPkt.Serialize();
                    foreach (var s in endTargets) _ = s.SendAsync(bytes);
                    Console.WriteLine($"[Round] ended — winner={wName} score={endRed}:{endBlue} (next in {PostRoundDelaySec}s)");
                }
            }

            // === 분기 2: 라운드 종료 후 대기 → 다음 라운드 시작 ===
            List<ClientSession>? startTargets = null;
            byte newRoundIdx = 0, snapRed = 0, snapBlue = 0;

            lock (_lock)
            {
                if (_matchInProgress && _roundEnded && !_matchEnded
                    && Stopwatch.GetTimestamp() >= _nextRoundDeadlineTicks)
                {
                    _roundIndex++;
                    _roundEnded = false;
                    ResetForNewRound();         // 위치/HP/사망 리셋 (lock 안)
                    newRoundIdx = _roundIndex;
                    snapRed = _redScore; snapBlue = _blueScore;
                    startTargets = new List<ClientSession>(_waiting);
                }
            }

            if (startTargets != null)
            {
                var startPkt = new S_RoundStart
                {
                    RoundIndex = newRoundIdx, RedScore = snapRed, BlueScore = snapBlue,
                };
                byte[] bytes = startPkt.Serialize();
                foreach (var s in startTargets) _ = s.SendAsync(bytes);
                Console.WriteLine($"[Round] === START === round={newRoundIdx} score={snapRed}:{snapBlue}");
            }
        }

        // 다음 라운드용 상태 리셋. _lock 안에서만 호출됨.
        private void ResetForNewRound()
        {
            for (int i = 0; i < _waiting.Count; i++)
            {
                var s = _waiting[i];
                int teamSlot = i / 2;
                lock (s.Player.Lock)
                {
                    s.Player.PosX = s.Team == 0 ? -8f : 8f;
                    s.Player.PosY = 0f;
                    s.Player.PosZ = teamSlot == 0 ? -2f : 2f;
                    s.Player.Yaw  = s.Team == 0 ? 90f : 270f;
                    s.Player.Pitch = 0f;
                    s.Player.Hp = Balance.Current.Player.InitialHp;
                    s.Player.IsDead = false;
                    s.Player.IsGrounded = true;
                    s.Player.VelocityY = 0f;
                    // 큐 비우기 (이전 라운드 잔여 입력 제거)
                    while (s.Player.InputQueue.TryDequeue(out _)) { }
                }
            }
        }

        // 인게임 4명 세션 스냅샷. lock 안에서 복사 → 호출자는 lock 없이 안전하게 순회.
        public List<ClientSession> GetMatchSnapshot()
        {
            lock (_lock)
            {
                return new List<ClientSession>(_waiting);
            }
        }

        private void BroadcastGameStart(List<ClientSession> sessions)
        {
            var playerInfos = new List<S_GameStart.PlayerInfo>(sessions.Count);
            foreach (var s in sessions)
            {
                playerInfos.Add(new S_GameStart.PlayerInfo
                {
                    Token = s.SessionToken,
                    Team = s.Team,
                    Nickname = s.Nickname,
                });
            }

            foreach (var receiver in sessions)
            {
                var pkt = new S_GameStart
                {
                    MyTeam = receiver.Team,
                    Players = playerInfos,
                };
                _ = receiver.SendAsync(pkt.Serialize());
            }
        }
    }
}