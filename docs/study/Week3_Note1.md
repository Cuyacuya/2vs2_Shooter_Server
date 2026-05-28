# Week3 Note 1 — UDP 도입 + 30Hz 틱 루프 + 풀 사이클 (월~금)

> 작성일: 2026-05-28
> 범위: 3주차 전체 — UDP 채널, 정식 틱 루프, 라운드 사이클, 매치 종료, TCP 끊김 처리
> 학습 등급: 🔴 직접 중심. 시간 압박으로 점프/hitscan/UDP/틱 루프 모두 🔴 → 🟡 강등.
> 🎯 **3주차 끝 = WBS 발표 가능 시점 도달**

---

## 0. 진행 요약

| 일 | 작업 | 등급 | 결과물 |
|---|---|---|---|
| 월 | UDP 채널 설계 + protocol.md 갱신 | 🟡 | Transport 분류, PacketId 표 + 신규 4종 |
| 월 | Shared UDP/라운드 패킷 4종 | 🟡 | C_UdpHello, S_RoundStart, S_RoundEnd, S_MatchEnd |
| 화 | UdpClient 7778 + 발신자 매칭 | 🔴→🟡 | `UdpServer.cs` 신규, `ClientSession.UdpEndPoint` |
| 수 | 30Hz 틱 루프 + 입력 큐잉 | 🔴→🟡 | `TickServer.cs` 신규, `SimulateOneTick`, `InputQueue` |
| 목 | 20Hz 스냅샷 다운샘플 | 🔴→🟡 | Bresenham 액큐뮬레이터 |
| 목 | 라운드 종료 판정 | 🔴→🟡 | `MatchManager.OnTickEnd` |
| 금 | 다음 라운드 시작 + 매치 종료 + TCP 끊김 | 🟡🔴→🟡 | 상태 머신 확장, `ResetForNewRound`, IsRoundLive 가드 |

**커밋**: `feature/HD-week3` 브랜치, 6커밋. `feature/HD-week3 → dev` PR 예정.

---

## 1. UDP 도입 — Transport 분류 결정

### 핵심 결정

| 패킷 | Transport | 이유 |
|---|---|---|
| C_Login / S_LoginResult | TCP | 한 번만, 누락 X |
| S_MatchingStatus / S_GameStart | TCP | 게임 진행 이벤트 |
| **C_Input** | **UDP** | 20Hz, 한두 개 누락 OK |
| **S_Snapshot** | **UDP** | 20Hz, 최신 1개만 의미 |
| **C_Fire / S_HitResult** | **TCP** | 빈도 낮음(<10/sec) + 신뢰성 압도적 |
| **C_UdpHello** (신규) | UDP | 핸드셰이크 |
| S_RoundStart/End, S_MatchEnd | TCP | 게임 진행 이벤트 |

### HitResult가 TCP인 이유 (자주 헷갈리는 부분)

| 비교 | S_Snapshot | S_HitResult |
|---|---|---|
| 빈도 | 20Hz × 항상 = 80/sec | 격렬 교전 시 ~10/sec |
| 누락 시 복구 | ✅ 50ms 후 다음 Snapshot | ❌ 영원히 사라짐 (이벤트는 한 번뿐) |
| 누락 영향 | 한 프레임 끊김 | 클라/서버 HP 불일치 ("유령 데미지") |
| 비유 | CCTV 영상 | 알람 벨 |

빈도가 Snapshot의 1/8이라 TCP throughput에 부담 없음. **신뢰성이 압도적으로 중요**.

### Snapshot 빈도 — 30Hz/20Hz의 산업적 위치

| 게임 | 시뮬/송신 |
|---|---|
| **우리** | 30Hz / 20Hz (학습용) |
| CS:GO 일반 | 64/64 |
| CS:GO 128 tick / Valorant | 128/128 |
| Overwatch | 60/60 |
| WoW | ~10Hz |

낮지만 학습엔 충분 + "받자마자 처리" 한계 체감 후 정식 도입 학습 흐름에 맞음.

---

## 2. UDP 핸드셰이크 — sessionToken ↔ Endpoint 매핑

### 문제 (TCP에 없음)

UDP는 비연결 → 서버 입장에선:
- 받은 datagram의 발신지 = `IP:port` 만 보임
- "이 IP:port가 누구의 sessionToken인가" 알 길 X
- 또한 서버가 클라에 S_Snapshot 보내려면 UDP 포트를 알아야 하는데, C_Login은 TCP라 TCP 포트만 앎

### 해결 — C_UdpHello

**패킷 구조** (6B, payload 0):
```
┌─────────┬──────────┬──────────────┐
│ length  │ packetId │ sessionToken │
│ = 0     │ = 9      │ = 1 (p1)     │   ← 헤더에 토큰
└─────────┴──────────┴──────────────┘
```

**핸드셰이크 흐름**:
```
[클라]                              [서버]
  │── TCP C_Login → token=1 ────────→│
  │←── TCP S_LoginResult ─────────────│
  │←── TCP S_GameStart ───────────────│
  │── UDP C_UdpHello(token=1) ──────→│
  │     (sender = 127.0.0.1:60177)   │   session.UdpEndPoint = sender
  │── UDP C_Input(token=1) ─────────→│
  │←── UDP S_Snapshot ────────────────│
```

### 매핑 저장 위치 — 분산 저장

별도 매핑 테이블 X. **각 ClientSession이 자기 UdpEndPoint 보유**:
```csharp
public class ClientSession {
    public ushort SessionToken { get; set; }
    public IPEndPoint? UdpEndPoint { get; set; }   // ★ 매핑 정보
}
```

조회는 `MatchManager._waiting` List 순회로 O(n). 4명 매치는 충분, MMO/배틀로얄이면 `Dictionary<token, session>`로 O(1).

### 위조 방어

```csharp
if (session.UdpEndPoint == null || !session.UdpEndPoint.Equals(sender)) return;
```

첫 hello로 등록한 endpoint와 다른 IP에서 같은 sessionToken으로 오면 무시.

### 메타 학습 — "Sessionization"

UDP 위에 가상 연결 개념 얹는 게 모든 실시간 게임의 표준:
- Counter-Strike: Steamworks Session
- Overwatch: 32-bit 토큰 헤더
- LoL: 자체 RTS 프로토콜
- Fortnite: Epic Online Services

**TCP의 연결 = UDP의 sessionToken 매핑**. 같은 역할.

---

## 3. 정식 30Hz 틱 루프 — 패러다임 전환

### "받자마자 처리" vs "틱 루프" 비교

| 항목 | 받자마자 처리 (2주차) | 틱 루프 (3주차) |
|---|---|---|
| 시간 진행 | 패킷 도착 시점 = 시뮬 시간 | **고정 33ms 간격** |
| dt | 가변 (패킷 간격) | **고정 1/30초** |
| 처리 순서 | 도착 순서 (네트워크 변덕) | **틱 안 일괄 정렬** |
| Snapshot 송신 | 입력당 1회 | **틱 끝 1회** |
| 점프 궤적 | 매번 다름 | **매번 동일** |
| 결정성 | X (재현 불가) | ✅ (예측·리플레이 가능) |
| 산업 표준 | (없음) | 99% 게임 채택 |

### "받자마자"의 진짜 손해 (재정정 — 이동거리 아님)

빠른 클라(50ms dt)도 느린 클라(100ms dt)도 wall-clock 시간 동안 같은 거리 진행. 진짜 문제는:

1. **상호작용 타이밍** — p1 사격 vs p2 회피 input의 도착 순서 차이로 5ms 사이에 운명 갈림
2. **결정성** — 같은 입력 → 다른 결과 (네트워크 운빨), 예측/리플레이/안티치트 불가
3. **Snapshot 일관성** — 한 명만 갱신된 상태가 송신됨 (다른 봇 떨림 — 2주차 통합 테스트 현상)

### 16ms 지연 추가 우려

평균 16.5ms 추가 — 사람 못 느낌 (모니터 자체 지연 5~10ms). 클라가 client-side prediction 도입하면 0ms 체감.

### 끊김 우려 — 클라 Lerp 보간

20Hz 데이터(50ms 간격) → 클라가 60fps로 그릴 때 두 snapshot 사이 `Lerp(시작, 끝, 비율)` 직선 보간 → **화면은 부드러움**. CS:GO/Valorant 영상이 부드러운 비밀.

---

## 4. 틱 루프 구현 — Deadline 패턴

### Stopwatch.GetTimestamp 사용 이유

| API | 정밀도 | 단조성 |
|---|---|---|
| **Stopwatch.GetTimestamp** ✅ | ns | ✅ |
| DateTime.UtcNow | ~15ms (Windows) | ❌ (NTP 변경 시 점프) |
| Environment.TickCount | ms | ❌ (49.7일 overflow) |

### 잘못된 패턴 (드리프트 누적)

```csharp
while true:
    [틱 작업 5ms]
    await Task.Delay(33)        // 38ms 주기로 떨어짐 → 26Hz
```

### 올바른 패턴 (절대 deadline)

```csharp
long tickInterval = Stopwatch.Frequency / 30;
long nextDeadline = Stopwatch.GetTimestamp() + tickInterval;

while true:
    ProcessTick()
    long remain = nextDeadline - Stopwatch.GetTimestamp()
    if remain > 0: await Task.Delay(ms)
    nextDeadline += tickInterval     // ★ 절대 시각 기준
```

처리가 5ms 걸려도 다음 deadline은 변함없이 33ms 후 → 30Hz 평균 유지.

### Catch-up 클램프

GC 200ms 멈춤 같은 랙 스파이크 시:
- 단순 catch-up: 6틱 일괄 처리 → 화면 점프
- **3틱 이상 뒤처지면 deadline 리셋** → 화면 점프 방지, 정확도 일시 손실 감수

---

## 5. 입력 큐잉 — ConcurrentQueue

### 구조

```csharp
public class PlayerState {
    public readonly ConcurrentQueue<C_Input> InputQueue = new();
}
```

**락 무료**: UDP 수신 스레드 enqueue + 틱 스레드 dequeue 동시 가능. `ConcurrentQueue` 자체가 lock-free.

### "한 틱에 같은 사람 입력 여러 개" 처리

20Hz 클라 → 30Hz 서버 = 어떨 땐 0개, 어떨 땐 2개. 우리 정책: **마지막 1개만 사용**.

```csharp
C_Input? lastInput = null;
while (Player.InputQueue.TryDequeue(out var pkt)) lastInput = pkt;
```

→ 한 틱 안 키 상태 변화는 **최종 의도만 반영**. CS:GO/Valorant도 비슷한 접근.

### 물리 시뮬 — 입력 무관하게 매 틱

```csharp
if (lastInput != null) { /* WASD/yaw/jump trigger 적용 */ }
// 항상:
Player.VelocityY += Gravity * dt;
Player.PosY += VelocityY * dt;
// 지면 클램프
```

→ 클라가 입력 안 보내도 **공중에 멈춰있지 않음** (2주차 "받자마자" 한계 해결).

### dt 고정의 효과

`const float TickDt = 1f / 30f;`
- 점프 궤적: 매번 동일 (재현성)
- 적분 안정성: 누적 오차 X
- 클라 예측 가능 (4주차+): 같은 dt로 시뮬 → 결과 거의 일치

---

## 6. 20Hz 다운샘플 — Bresenham 액큐뮬레이터

### 패턴

```csharp
_snapshotAccum += 20;             // 매 틱 +SnapshotHz
if (_snapshotAccum >= 30) {       // TickHz 도달 시
    _snapshotAccum -= 30;
    BroadcastSnapshot();
}
```

10틱 추적:
| 틱 | accum | 송신? | 차감 후 |
|---|---|---|---|
| 1 | 20 | X | 20 |
| 2 | 40 | ✅ | 10 |
| 3 | 30 | ✅ | 0 |
| 4 | 20 | X | 20 |
| 5~6 | 40, 30 | ✅ ✅ | 10, 0 |

규칙: **3틱마다 "X → ✅ → ✅" 반복 = 정확히 20Hz**.

### 1965년 Bresenham 알고리즘 — 화면에 직선 그리기

원래 화면에 임의 기울기 직선 그릴 때 부동소수 없이 픽셀 결정하는 알고리즘. 본질은 "정수로 비율 누적". 음악 sample rate 변환, 애니메이션 frame rate 변환, 우리 송신율 다운샘플 — 모두 동일 패턴.

### 대안 비교

| 방법 | 정확도 | 문제 |
|---|---|---|
| **액큐뮬레이터** ✅ | 정확 | — |
| `tickCount % 3` | TickHz/SnapHz 비율이 정수일 때만 | 30/22 같은 비율 깨짐 |
| 시간 기반 (`now - last >= 50ms`) | 부동소수 오차 누적 | 19.8Hz 같은 드리프트 |
| 별도 Timer | OS ~15ms 정밀도 | 정확도 부족 |

### 트래픽 절감 계산

```
이전(30Hz): 30 패킷/sec × 4 수신자 = 120 패킷/sec
현재(20Hz): 20 패킷/sec × 4 수신자 = 80 패킷/sec
감소율: (120-80)/120 = 1/3 = 33.3%
```

코드로 보장되어 측정 없이도 확정. 4명 게임은 미미하지만 100명 게임이면 시간당 ~$3 AWS 트래픽 비용 차이.

---

## 7. 라운드 사이클 + 매치 종료

### 상태 머신

```
[Match Idle]
   ↓ 4명 모이면 StartMatch
[Round InProgress] (한 라운드 진행 중)
   ↓ 한 팀 전원 사망 (OnTickEnd 매 틱 감지)
[PostRound Wait] (3초 대기)
   ↓ deadline 도달
   ├ 점수 < 2 → [Round InProgress] 다음 라운드 (위치/HP 리셋 + S_RoundStart)
   └ 점수 >= 2 → [Match Ended] (S_MatchEnd, 추가 처리 안 함)
```

### OnTickEnd 2단 분기

```csharp
public void OnTickEnd()
{
    // 분기 1: 진행 중 → 종료 감지
    lock (_lock) {
        if (!_roundEnded) {
            팀별 IsDead 카운트
            if (한 팀 0명) {
                점수++
                _roundEnded = true
                if (점수 >= 2) {
                    _matchEnded = true
                    [S_MatchEnd 송신 데이터 준비]
                } else {
                    _nextRoundDeadline = now + 3sec
                    [S_RoundEnd 송신 데이터 준비]
                }
            }
        }
    }
    [lock 밖에서 송신]

    // 분기 2: PostRound → 다음 라운드
    lock (_lock) {
        if (_roundEnded && !_matchEnded && now >= _nextRoundDeadline) {
            _roundIndex++
            _roundEnded = false
            ResetForNewRound()  // 4명 위치/HP/사망/큐 리셋
            [S_RoundStart 송신 데이터 준비]
        }
    }
    [lock 밖에서 송신]
}
```

### 핵심 패턴 — "데이터 준비는 lock 안, I/O는 lock 밖"

```csharp
List<ClientSession>? targets = null;
lock (_lock) {
    /* 상태 변경 + 데이터 스냅샷 */
    targets = new List<ClientSession>(_waiting);
}
if (targets != null) {
    foreach (var s in targets) _ = s.SendAsync(bytes);   // I/O는 lock 밖
}
```

이유:
- SendAsync 내부에 자체 SemaphoreSlim 있음 → 락 중첩 위험
- 데드락 회피의 표준 패턴

### IsRoundLive 게이트

```csharp
public bool IsRoundLive {
    get { lock (_lock) return _matchInProgress && !_roundEnded && !_matchEnded; }
}
```

`HandleFire` 첫줄에서 가드 → PostRound 3초 대기 / 매치 종료 후엔 사격 거부. **미스 스팸 방지**.

### `_roundEnded` 플래그 — 중복 송신 방지

라운드 끝나면 매 틱 OnTickEnd 또 호출됨. 플래그 없으면 무한 점수 갱신 + 무한 S_RoundEnd. 한 번만 송신 보장.

---

## 8. TCP 끊김 → 사망 처리

### Remove() 분기

```csharp
public void Remove(ClientSession session)
{
    lock (_lock) {
        if (_matchInProgress) {
            // 인게임 끊김 — 큐 유지 + IsDead=true
            session.Player.IsDead = true;
            session.Player.Hp = 0;
        } else {
            // 매칭 중 — 큐에서 제거 + S_MatchingStatus 갱신
            _waiting.Remove(session);
        }
    }
}
```

### 자동 라운드 종료 판정

다음 틱 OnTickEnd가 자동으로:
- IsDead 카운트에 반영
- 그 팀이 0명이 되면 S_RoundEnd 송신
- **별도 "끊김 → 즉시 매치 종료" 로직 불필요** — 사망 처리만 하면 기존 흐름이 알아서 처리

### 매칭 큐에서 제거 vs 인게임 자리 유지

| 시점 | 동작 | 이유 |
|---|---|---|
| 매칭 중 (큐 대기) | 제거 | 자리 비워줘야 다음 사람 큐 진입 |
| 매치 시작 후 | 자리 유지 (시체) | 라운드 종료 판정에 "0명 카운트"로 반영, 클라가 시체 표시 가능 |

---

## 9. 통합 테스트 — 풀 사이클 검증 결과

### 검증 시나리오 (2회 실행)

**1차 run**:
```
매칭 → S_GameStart
라운드 1: 5초 내 Blue 전원 사망
[Round] ended — winner=Red score=1:0 (next in 3s)
3초 대기
[Round] === START === round=2 score=1:0
라운드 2: Red 전원 사망 → 2:0 도달
[Match] === ENDED === winner=Blue final=0:2 ✅
```

**2차 run** (IsRoundLive 게이트 추가 후):
- 미스 스팸 사라짐
- 라운드 2가 봇 yaw 드리프트로 30초 안 종료 (시간 부족) — 봇 한계
- 시연 핵심은 1차 run에서 확인됨

### WBS 3주차 종료 기준 ✅

- [x] 위치 동기화가 UDP로 동작 (S_Snapshot UDP 송신 확인)
- [x] 30Hz 시뮬 / 20Hz 스냅샷 동작 (Bresenham 정확)
- [x] 라운드/매치 종료 = TCP 안정 (S_RoundEnd/Start/MatchEnd 정상)
- [x] 한 팀 2승 → 매치 종료 → 클라 메인 복귀 (S_MatchEnd 송신)
- [x] TCP 끊으면 그 플레이어 사망 처리 (Remove 분기)
- [x] **풀 사이클 완주** ✅

### "TCP는 부드럽지 않다 → UDP로 개선됨" 체감

- 2주차: 받자마자 처리 모델 — 봇이 텔레포트
- 3주차 끝: 30Hz 시뮬 + 20Hz Snapshot + UDP — 시뮬 일관, 송신 효율
- Unity 클라 통합 시 Lerp 보간 추가하면 완전 부드러움 (방학/4주차)

---

## 10. 디버깅 사이클 회고

### 화요일 — UDP 봇 yaw 함정 (재발)
- 증상: hello 도착, fire 처리, 그러나 0 hit
- 원인: 봇이 자기 random yaw 송신 → 서버 spawn yaw 즉시 덮음
- 수정: TCS<byte>로 myTeam 전달 → 봇이 team별 yaw 초기화

### 수요일 — 받자마자 → 틱 루프 전환
- 작업: HandleInput을 SimulateOneTick으로 리팩토링
- 핵심 결정:
  1. dt 고정 (1/30) — LastInputTicks 더 이상 안 씀
  2. 큐 마지막 입력만 적용
  3. 물리는 매 틱 무조건
- BroadcastSnapshot 위치 이동 — HandleInput 끝 → 틱 끝 1회

### 목요일 — 라운드 종료 무한 송신 함정
- 증상: 한 팀 0명 되면 매 틱 점수 +1
- 원인: 플래그 없이 매 틱 같은 조건 만족
- 수정: `_roundEnded` 플래그

### 금요일 — 매치 종료 후 미스 스팸
- 증상: `[Match] === ENDED ===` 후에도 `[Fire] miss` 무한
- 원인: HandleFire가 match state 안 봄, 봇은 계속 사격
- 수정: `IsRoundLive` 게이트

### 금요일 — 라운드 2 봇 조준 드리프트 (해결 미룸)
- 증상: 라운드 2에서 봇 거의 못 맞춤 → 30초 안 종료
- 원인: 봇 yaw가 라운드 1 30초 동안 ±2°/tick 누적 드리프트
- 미해결: 봇이 S_RoundStart 안 처리 → yaw 리셋 못함
- 사용자 결정: "이미 풀 사이클 검증됨 → 그냥 진행"

---

## 11. 핵심 동시성 패턴 정리 (3주차에 자주 쓴 것)

### 패턴 1: 스냅샷 복사

```csharp
List<ClientSession>? targets = null;
lock (_lock) { targets = new List<ClientSession>(_waiting); }
foreach (var s in targets) ...   // lock 없이 순회
```

용도: GetMatchSnapshot, OnTickEnd의 송신 대상, BroadcastSnapshot 등 빈출.

### 패턴 2: 값 스냅샷

```csharp
float ox, oy, oz;
lock (s.Player.Lock) { ox = s.Player.PosX; ... }   // 빠르게 복사
// 이후 ray 계산은 lock 밖
```

용도: HandleFire의 attacker/enemy 위치 캡처.

### 패턴 3: ConcurrentQueue lock-free

```csharp
Player.InputQueue.Enqueue(pkt);     // UDP 스레드
while (Player.InputQueue.TryDequeue(out var pkt)) ...   // 틱 스레드
```

용도: 입력 큐.

### 패턴 4: TaskCompletionSource 동기화

```csharp
var tcs = new TaskCompletionSource<byte>(RunContinuationsAsynchronously);
// Loop A: tcs.TrySetResult(value);
// Loop B: byte v = await tcs.Task;
```

용도: 봇 ListenLoop → InputLoop 게임 시작 신호.

### 데드락 회피 — "I/O는 항상 lock 밖"

SendAsync 내부엔 SemaphoreSlim 있음 → 외부 락 보유 중 호출 시 데드락 위험. **데이터 준비는 lock 안, 실제 송신은 lock 밖** 으로 분리.

---

## 12. 함정 학습 (3주차 추가분)

| 함정 | 결과 | 대응 |
|---|---|---|
| UDP에 length-prefix 시도 | 헷갈림 (UDP는 datagram 경계 보존) | ReceiveAsync 결과 buffer = 1 패킷 |
| C_UdpHello 누락 → S_Snapshot 못 받음 | 봇 멈춘 듯 보임 | hello 송신 후 50ms 대기 (재시도는 안 함, 학습용) |
| 봇 random yaw 송신 → 서버 spawn yaw 덮임 | 0 hit | TCS<byte>로 myTeam 전달, team별 yaw 초기화 |
| `Task.Delay(33)` 만 사용 | 처리 시간 누적, 26Hz로 떨어짐 | 절대 deadline 패턴 |
| 단순 catch-up | 랙 스파이크 후 화면 점프 | 3틱 초과 뒤처지면 deadline 리셋 |
| `tickCount % 3` 으로 다운샘플 | TickHz/SnapHz가 정수배가 아니면 깨짐 | Bresenham 액큐뮬레이터 |
| `_roundEnded` 플래그 누락 | 매 틱 점수 +1 무한 | 종료 1회만 송신 |
| `IsRoundLive` 게이트 누락 | 매치 종료 후 미스 스팸 | HandleFire 첫줄 가드 |
| Remove()에서 인게임 끊김도 큐 제거 | 그 팀 1명 자동 사라짐 (시체 표시 X, 판정 누락) | 매치 중엔 IsDead=true만, 큐 유지 |
| SendAsync를 lock 안에서 호출 | 데드락 가능 | I/O는 항상 lock 밖 |
| Dictionary 매핑 vs List 순회 | 큰 N에서 성능 저하 | 4명 매치는 List OK, MMO면 Dictionary |

---

## 13. 학습 부채 누적 (방학 / 5주차 회고 대상)

WBS가 🔴로 잡았던 작업 5개 모두 시간 압박으로 코드 받음 (🔴→🟡 강등):
1. **ray-sphere 수학** — 2주차 부채, 4주차 캡슐 정밀화에서 회수 예정
2. **점프 시뮬 콘솔 단위 테스트** — 2주차 부채
3. **UDP 비동기 수신 루프 직접 작성** — 3주차 부채
4. **30Hz 틱 루프 직접 작성** — 3주차 부채 (가장 학습 가치 큼)
5. **20Hz 액큐뮬레이터 패턴 직접 유도** — 3주차 부채

### 회수 계획
- **방학**: C++ 리메이크 시작 시 위 5개 직접 구현 — 같은 사고 다시 거치면 깊이 정착
- **5주차 회고**: postmortem.md에 "다시 한다면 어떻게" 자세히 기록

---

## 14. 발표 가능 시점 도달 점검

WBS 3주차 끝 = 발표 가능. 점검:

- [x] 자동 매칭으로 4명 모임
- [x] 자/적팀 구분 시각 표시 가능 (Unity 통합 후)
- [x] 이동/점프/사격 동작 (30Hz 시뮬, UDP 입력)
- [x] HP 감소 + 사망 (정확한 ray-sphere)
- [x] 라운드 종료 → 점수 누적
- [x] 매치 종료 (best-of-3 2승 선취)
- [x] TCP 끊김 → 자동 사망 처리
- [ ] Unity 클라 통합 — 클라 친구 진도 의존
- [ ] 시연 영상 — Unity 통합 후

서버 단독으론 **발표 가능 상태** ✅. Unity 통합은 클라 담당 진도와 함께 결정.

---

## 15. 다음 단계 — 토요일 + 4주차 준비

### 토요일 (버퍼)
- (선택) UDP 패킷 손실 시뮬레이션 — 인위적 drop으로 견고성 테스트
- (선택) Unity 클라 통합 테스트 — 클라 친구 진도 확인 후
- (선택) `feature/HD-week3` → `dev` 머지 (오늘 진행 예정)

### 4주차 — 살 붙이기 (진짜 로비 + 탄약/재장전 + 봇 풀 기능)
- 진짜 로비/방 시스템 (1~3주차 자동 매칭 교체)
- 탄약 / 재장전 (R 키, 2초)
- 봇 풀 기능 (방 입장 + 준비 + 게임플레이)

### 준비 자료
- `ch08_BackgroundIO.pdf`, `ch09_TCP_고급기법.pdf` 복습
- "상태 머신 확장" 패턴 — Lobby + RoomWaiting + InGame 다중 상태

---

## 16. AI 활용 회고 — 3주차 메타-학습

### 강등 패턴 확정
WBS 🔴 등급 작업 모두 시간 압박으로 🟡(5단 포맷) 또는 🟢(즉시 코드)로 다운그레이드. 메모리 규칙 `feedback_coding_style.md` 이 명확해서 결정 빠름.

### 진단 사이클 패턴 (목~금)
- 사용자가 콘솔 로그 풀로 공유 → AI 가설 분석
- 가설 + 최소 변경 제시 → 사용자 빌드/실행 → 다음 사이클
- 평균 1~2 사이클로 root cause 도달

### 효율적 패턴
- "코드 핵심 / 코드 / 코드 설명" 3단 압축 포맷
- 사용자가 결과 즉시 공유 → 빠른 피드백 루프
- 옵션 A/B/C 제시 → 사용자 결정 → 진행

### 비효율 패턴 (개선 여지)
- 일부 작업에서 가설 명시 없이 brute force ("fire rate ↑", "시간 ↑")
- "근본 변수가 무엇인가" 명시적 사고가 가끔 빠짐

---

## 17. 한 줄 요약

> "받자마자 처리는 빠르지만 공정·정확하지 않다. 30Hz 시뮬 + 20Hz 송신 + UDP + 라운드 사이클로 진짜 멀티플레이 서버의 골격을 완성. 발표 가능 시점 도달."
