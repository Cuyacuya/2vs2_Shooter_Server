using System.Diagnostics;

namespace GameServer
{
    // 정식 30Hz 틱 루프 (3주차 수요일).
    // Stopwatch deadline 패턴 — Thread.Sleep/Task.Delay의 정밀도 한계 안에서 평균 30Hz 유지.
    public class TickServer
    {
        private static readonly TickServer _instance = new();
        public static TickServer Instance => _instance;

        public const int TickHz = 30;
        public const int SnapshotHz = 20;
        public const float TickDt = 1f / TickHz;   // 고정 dt = 0.0333초

        // 20Hz 스냅샷 다운샘플용 액큐뮬레이터.
        // 매 틱 +SnapshotHz, TickHz 도달 시 송신 + 차감 → 30틱당 정확히 20회 송신.
        private int _snapshotAccum = 0;

        private TickServer() { }

        public void Start()
        {
            Console.WriteLine($"[Tick] starting {TickHz}Hz loop (dt={TickDt:F4}s), snapshot={SnapshotHz}Hz");
            _ = RunAsync();   // fire-and-forget
        }

        private async Task RunAsync()
        {
            long tickInterval = Stopwatch.Frequency / TickHz;
            long nextDeadline = Stopwatch.GetTimestamp() + tickInterval;

            while (true)
            {
                try
                {
                    ProcessTick();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Tick] error: {ex.Message}");
                }

                // 다음 deadline까지 대기 — 절대 시각 기준이라 처리 지연 누적 X
                long now = Stopwatch.GetTimestamp();
                long remain = nextDeadline - now;
                if (remain > 0)
                {
                    int ms = (int)(remain * 1000 / Stopwatch.Frequency);
                    if (ms > 0) await Task.Delay(ms);
                }
                else if (remain < -tickInterval * 3)
                {
                    // 3틱 이상 뒤처지면 catch-up 포기 (GC 멈춤 등). 화면 점프 방지.
                    Console.WriteLine($"[Tick] far behind, resetting deadline");
                    nextDeadline = Stopwatch.GetTimestamp();
                }

                nextDeadline += tickInterval;
            }
        }

        private void ProcessTick()
        {
            var sessions = MatchManager.Instance.GetMatchSnapshot();
            if (sessions.Count == 0) return;

            // 1) 4명 각자 큐 비우고 시뮬 1틱
            foreach (var s in sessions)
            {
                s.SimulateOneTick(TickDt);
            }

            // 2) 20Hz 다운샘플 — 30틱당 정확히 20회 송신
            _snapshotAccum += SnapshotHz;
            if (_snapshotAccum >= TickHz)
            {
                _snapshotAccum -= TickHz;
                ClientSession.BroadcastSnapshot();
            }

            // 3) 라운드 종료 판정 (한 팀 전원 사망 시 S_RoundEnd 송신)
            MatchManager.Instance.OnTickEnd();
        }
    }
}
