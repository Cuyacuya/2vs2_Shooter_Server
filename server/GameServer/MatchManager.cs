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

        //큐에 등록 시도.
        //매치 진행 중이면 false, 아니면 true
        public bool TryEnqueue(ClientSession session)
        {
            lock (_lock)
            {
                if (_matchInProgress)
                {
                    Console.WriteLine($"[Match] reject {session.Nickname}: match in progress");

                    return false;
                }

                _waiting.Add(session);
                Console.WriteLine($"[Match] {session.Nickname} joined queue({_waiting.Count}/{MaxPlayers})");

                if(_waiting.Count >= MaxPlayers)
                {
                    StartMatch();
                }
                return true;
            }
        }

        //대기 중 세션이 끊기면 큐에서 제거(매치 시작 후엔 무시)
        public void Remove(ClientSession session)
        {
            lock (_lock)
            {
                if (_waiting.Remove(session))
                {
                    Console.WriteLine($"[Match] {session.Nickname} left queue({_waiting.Count}/{MaxPlayers})");
                }
            }
        }

        //lock 안에서만 호출됨(private)
        private void StartMatch()
        {
            _matchInProgress = true;
            Console.WriteLine($"[Match] === MATCH STARTING ({_waiting.Count} players) ===");
            foreach(var s in _waiting)
            {
                Console.WriteLine($"  - {s.Nickname} (token={s.SessionToken})");
            }
            // TODO(금): 팀 배정 + S_GameStart 송신
        }
    }
}