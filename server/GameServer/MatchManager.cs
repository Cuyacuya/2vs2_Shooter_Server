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

        //대기 중 세션이 끊기면 큐에서 제거(매치 시작 후엔 무시)
        public void Remove(ClientSession session)
        {
            List<ClientSession>? snapshot = null;

            lock (_lock)
            {
                if (_waiting.Remove(session))
                {
                    Console.WriteLine($"[Match] {session.Nickname} left queue({_waiting.Count}/{MaxPlayers})");
                    if (!_matchInProgress)
                        snapshot = new List<ClientSession>(_waiting);
                }
            }

            if (snapshot != null)
                BroadcastStatus(snapshot);
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