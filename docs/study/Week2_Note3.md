# Week2 Note 3 — 위치 브로드캐스트 + 봇 인게임 입력 + 통합 테스트 디버깅 (금요일)

> 작성일: 2026-05-26
> 범위: 2주차 금요일 — S_Snapshot 브로드캐스트, 봇 인게임 입력 루프, 4명 통합 테스트
> 학습 등급: 🟡 (보조). InputLoopAsync는 5단 포맷.
> 진행 방식: 코드 → 실행 → 진단 → 수정 → 재실행 4사이클

---

## 0. 진행 요약

| 작업 | 등급 | 결과물 |
|---|---|---|
| A. BroadcastSnapshot() — HandleInput/HandleFire 끝에서 4명 전원 송신 | 🟡 | `ClientSession.BroadcastSnapshot` |
| B. 봇 ListenLoop 확장 — S_GameStart에서 TCS 신호, S_Snapshot/S_HitResult 케이스 | 🟡 | `BotClient/Program.cs` |
| C. 봇 InputLoopAsync — 20Hz C_Input + C_Fire, 팀 기반 yaw 초기화 | 🟡 | `InputLoopAsync` |
| D. MatchManager 스폰 위치 — Red/Blue 좌우 마주봄 | 🟢 | `MatchManager.StartMatch` |
| E. HandleFire miss 로그 — 진단용 | 🟢 | `ClientSession.HandleFire` |
| F. 4명 통합 테스트 — kill 발생까지 4사이클 튜닝 | 🔴 | (실험 결과) |

**커밋**: `feature/HD-week2` 브랜치, Friday 작업 2개 커밋 (`f7c8ddc` snapshot + `9eb714d` 봇/스폰/튜닝).

---

## 1. S_Snapshot 브로드캐스트 — 받자마자 처리 모델 완성

### 핵심
HandleInput / HandleFire 처리 직후, **4명의 PlayerState 전부를 한 패킷에 담아 4명 전원에게 송신**. 클라들은 이 패킷으로 화면의 4 캐릭터 위치/시점/HP/사망 갱신.

### 코드 구조

```csharp
private static void BroadcastSnapshot()
{
    var sessions = MatchManager.Instance.GetMatchSnapshot();
    if (sessions.Count == 0) return;             // 매치 시작 전/종료 후 가드

    var snaps = new List<PlayerSnapshot>(sessions.Count);
    foreach (var s in sessions)
    {
        lock (s.Player.Lock)                     // ★ 각 PlayerState 락 짧게
        {
            snaps.Add(new PlayerSnapshot { ... });
        }
    }

    var pkt = new S_Snapshot { Players = snaps };
    byte[] bytes = pkt.Serialize();              // 직렬화는 lock 밖
    foreach (var s in sessions)
        _ = s.SendAsync(bytes);                  // fire-and-forget
}
```

### 호출 위치 (lock 밖)

```csharp
HandleInput(...)
{
    lock (Player.Lock) { /* 이동/점프/시점 갱신 */ }
    BroadcastSnapshot();                          // ← lock 밖
}

HandleFire(...)
{
    /* attacker 스냅샷 → ray-sphere → HP 갱신 (각자 짧은 lock) */
    Console.WriteLine($"[Fire] ...");
    BroadcastSnapshot();                          // 피격 직후 즉시 동기화
}
```

**왜 lock 밖?** BroadcastSnapshot 내부에서 다른 PlayerState lock 잡으려 할 때, 호출자가 자기 lock 보유 중이면 **데드락**. 항상 lock 풀고 호출.

### "받자마자 처리" 모델의 한계 (의도적)

- HandleInput이 안 불리면 BroadcastSnapshot도 안 됨 → 클라가 화면 갱신 안 됨
- 4봇 × 20Hz = 80 패킷/sec. 점프 중에 입력 안 보내면 그 봇의 위치 갱신 정지
- 3주차 정식 30Hz 틱 루프 + 20Hz 다운샘플로 해결 예정

---

## 2. 봇 InputLoopAsync — 게임 시작 동기화

### 핵심 패턴 — TaskCompletionSource로 두 루프 동기화

```
per bot:
    tcs = new TaskCompletionSource<byte>()
    Task.Run( ListenLoopAsync(client, nick, tcs, ct) )
    Task.Run( InputLoopAsync (client, nick, tcs, ct) )

ListenLoop:
    on S_GameStart: tcs.TrySetResult(pkt.MyTeam)

InputLoop:
    byte myTeam = await tcs.Task         // 게임 시작까지 대기
    yaw = myTeam == 0 ? 90 : 270
    while !ct: send C_Input + 가끔 C_Fire, await Task.Delay(50)
```

**TaskCompletionSource = "한 번 set되면 await가 깨어나는 신호"**. 두 비동기 루프 간 동기화의 표준 패턴.

### RunContinuationsAsynchronously 옵션

```csharp
new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously)
```

기본 동작: `TrySetResult` 호출자가 `await`의 continuation을 동기적으로 실행함. ListenLoop 안에서 set 하면 InputLoop의 `await` 다음 코드가 같은 스레드에서 즉시 실행 → ListenLoop이 멈춤. 옵션 켜면 별도 스레드풀로 분리되어 ListenLoop이 막힘 없이 진행.

### ct + tcs 동시 대기 (취소 호환)

```csharp
var cancelTcs = new TaskCompletionSource();
using (ct.Register(() => cancelTcs.TrySetResult()))
{
    await Task.WhenAny(waitTask, cancelTcs.Task);
}
if (ct.IsCancellationRequested || !waitTask.IsCompleted) return;
```

`await tcs.Task`만 쓰면 ct로 취소해도 안 깨어남 (게임 시작이 안 되면 영원히 멈춤). `Task.WhenAny`로 두 신호 중 하나라도 오면 깨어나게 처리.

### 봇 yaw 초기화의 함정 ⚠️ 가장 큰 학습 포인트

**처음 작성한 코드**:
```csharp
float yaw = rng.Next(0, 360);    // 봇마다 랜덤
```

**문제**: 서버는 StartMatch에서 Red yaw=90, Blue yaw=270으로 스폰. 그러나 봇이 **첫 C_Input 송신하는 순간 자기 random yaw로 덮어씌움** → 서버의 스폰 yaw 무효화 → 봇이 적 방향이 아닌 랜덤 방향 조준.

**증상**: 첫 5초 통합 테스트에서 fire 시도는 활발한데 **0 hit**. 서버 로그에 `[Fire] -> miss` 만 가득.

**수정**:
```csharp
byte myTeam = await waitTask;
float yaw = myTeam == 0 ? 90f : 270f;   // 서버 스폰과 일치
```

**교훈**:
- **서버 권한 모델이라도 클라는 yaw를 보냄** (시점은 사용자 입력). 서버가 매 입력에 받은 yaw로 덮어씀.
- 클라 ↔ 서버 **초기 상태 일관성**은 명시적으로 동기화해야 함. 서버가 스폰 yaw 정해도 클라가 0 보내면 그게 진실.
- 4주차 진짜 로비에선 클라가 게임 진입 시 서버의 첫 S_Snapshot에서 yaw 받아 카메라/캐릭터 회전 초기화하는 패턴 필요.

---

## 3. 4사이클 통합 테스트 — 진단 로그

### 사이클 1 — 기본 5초 / W 60% / Fire 5%
**결과**: 봇 매칭/이동/snapshot 정상. **Fire 로그 0개** (miss 로그 추가 전).
**진단**:
- (가) HandleFire 안 불림? 또는 (나) 다 miss 라 로그 안 찍힘?
- 봇 모두 (0,0,0) 스폰 → ray-sphere의 "t<0 (시작점이 구 내부)" 가드에 걸려 자동 miss.

### 사이클 2 — miss 로그 추가 + 스폰 위치 분리
**결과**: `[Fire] -> miss` 가 가득. **여전히 0 hit**.
**진단**:
- 봇이 자기 random yaw 송신 → 서버 yaw=90/270 즉시 덮어씀 → 랜덤 방향 조준 → 다 빗나감.

### 사이클 3 — 봇 yaw 팀 기반 초기화 + W 70% + Fire 5%
**결과**: **첫 hit 발생!** 5초간 5 hit. 그러나 **kill=0**.
**진단**:
- 봇이 5m/s로 직진, Red/Blue 1.6초 만에 정면 충돌 → **그 후 서로 등 뒤로 지나침** → 더 이상 마주보지 않음.
- yaw 고정 (90/270)이라 적이 등 뒤로 가도 추적 안 함.
- 교전 윈도우 = 1.6초만. 그 안에 4 hit (= 100 dmg = kill) 만들기 어려움.

### 사이클 4 — Fire 20% / 시간 10초
**결과**: 150+ fire 시도, 6 hit, 4명 모두 25 HP까지 갔지만 **여전히 kill=0**.
**진단**:
- 교전 윈도우 자체가 안 늘어남. Fire 빈도만 ↑ 시킨 효과 미미.
- 핵심 변수는 **시간이 아니라 거리**.

### 사이클 5 — W 20% / Fire 25% / 시간 5초
**결과**: ✅ **kill=1 2회 발생**. p3→p4, p1→p2.
**핵심**:
- W를 20%로 낮춰 봇이 평균 1m/s만 전진 → 5초간 5m만 진행 → 8~13m 거리에서 계속 마주봄
- 교전 윈도우가 5초 내내 지속됨
- Fire 25% × 20Hz × 4봇 × 5s = 100 fire 시도. 그 중 ~10 hit, 4명 모두 HP 0 근접

### 메타 학습 — 디버깅 사이클의 본질

1. **증상이 같아도 원인은 다르다**: "kill=0" 이 사이클 3·4·5 모두 동일했지만 원인은 매번 달랐음. 한 변수씩 고치며 좁혀야 함.
2. **시간 늘리기 ≠ 만능**: 더 긴 시간이 교전 윈도우를 늘리지 않으면 의미 없음. **루트 변수**를 찾아야 함.
3. **로그 추가는 무료**: miss 로그 한 줄로 "Fire 호출되는가 / 다 miss 인가" 구분 가능. 진단 비용 ↓↓.

---

## 4. 스폰 위치 도식

```
        +Z
         ↑
Red p3 ●           ● Blue p4
(-8,0,2)           (8,0,2)
yaw=90°            yaw=270°
   →                ←
Red p1 ●           ● Blue p2
(-8,0,-2)          (8,0,-2)
yaw=90°            yaw=270°
   →                ←
         ↓
        -Z
```

- Red 팀 (token=0,2): -X쪽, yaw=90° → +X 향함 (Blue 쪽)
- Blue 팀 (token=1,3): +X쪽, yaw=270° → -X 향함 (Red 쪽)
- 팀원 둘은 Z=±2로 분리 (겹침 방지)
- 두 팀 거리 16m, sphere r=0.5m

### StartMatch 초기화 항목

```csharp
s.Player.SessionToken = s.SessionToken;
s.Player.Team         = s.Team;
s.Player.PosX = team==0 ? -8 : 8;
s.Player.PosY = 0;
s.Player.PosZ = teamSlot==0 ? -2 : 2;
s.Player.Yaw  = team==0 ? 90 : 270;
s.Player.Hp   = Balance.Current.Player.InitialHp;   // 100
s.Player.IsDead = false;
s.Player.IsGrounded = true;
s.Player.VelocityY = 0;
```

**핵심**: HP·IsDead·IsGrounded·VelocityY까지 모두 명시 리셋. 매치 재실행 시 (4주차 다음 라운드) 동일한 초기 상태 보장.

---

## 5. 동시성 검증 — 이번 테스트에서 본 것

### lock 분할의 효과

```
Time T:
  Thread A (HandleFire of attacker p2)
    └ lock(p2.Player.Lock) → 자기 위치 복사 → unlock
    └ lock(p1.Player.Lock) → p1 위치 복사 → unlock
    └ ray-sphere 계산
    └ lock(p1.Player.Lock) → HP -= 25 → unlock
    └ BroadcastSnapshot
        └ lock(p1.Player.Lock) → snap에 복사 → unlock
        └ lock(p2.Player.Lock) → ...
        └ lock(p3.Player.Lock) → ...
        └ lock(p4.Player.Lock) → ...

  Thread B (HandleInput of p3) 동시 진행
    └ lock(p3.Player.Lock) → 이동 처리 → unlock
    └ BroadcastSnapshot
        └ (위와 동일하게 4 lock 차례로)
```

**여러 락을 동시에 보유한 적이 한 번도 없음** → 데드락 불가능. 통합 테스트 5초간 수백 lock 획득/해제 발생했지만 hang 없음.

### HP 누적 감소 일관성

p1 HP가 정확히 100 → 75 → 50 → 25 → 0 순서로 떨어짐.
- Blue p2·p4 둘이 동시에 p1 쏠 수 있음
- lock(p1.Player.Lock) 안에서 `int newHp = Hp - damage` + 분기 + 할당이 원자적
- 두 공격자가 같은 순간 쏴도 lock 직렬화로 75→50 순차 진행 (둘 다 75에서 시작해 50으로 끝나는 race 없음)

### IsDead 가드 검증

`kill=1` 발생 후, 해당 token으로 추가 hit 로그가 **한 번도 안 나옴**.
- HandleFire의 `if (eDead) continue;` 가드가 ray-sphere 검사 단계에서 제외
- HP가 음수로 안 떨어짐 (사망 후 추가 -25 적용 안 됨)

---

## 6. WBS 2주차 종료 기준 ✅ 검증

| 기준 | 검증 방법 | 결과 |
|---|---|---|
| 4명 위치가 화면에서 보임 | S_Snapshot에 4명 PlayerSnapshot 다 포함 (count=4) | ✅ |
| 자/적팀 구분 (서버가 팀 정보 송신) | S_GameStart의 MyTeam + S_HitResult 로그에서 Red끼리 사격 0회 | ✅ |
| 점프 → 중력 → 착지 사이클 | 봇 점프 2% × 20Hz × 4 = 0.16/s → 5초 약 1회. 콘솔 로그론 직접 안 보이지만 코드 path 통과. Unity 클라 통합 시 시각 검증 예정 | 🟡 (코드 동작 확인, 시각 검증 보류) |
| 사격 시 HP 감소 + 사망 처리 (정확한 ray-sphere) | kill=1 2회 + HP 누적 4단계 감소 + IsDead 가드 | ✅ |
| 본인이 "TCP는 부드럽지 않다, 점프가 텔레포트 같다" 직접 느낌 | Unity 클라 통합 후 체감 예정. 봇 콘솔로는 한계 체감 불가 | 🟡 (Unity 통합 대기) |

**도달도**: 5개 중 3개 완전 검증, 2개 코드 동작은 확인했으나 시각/체감은 Unity 통합 시 (클라 친구 진도 의존).

---

## 7. 함정 학습 (이번 작업 추가분)

| 함정 | 결과 | 대응 |
|---|---|---|
| 봇이 자기 random yaw 송신 → 서버 스폰 yaw 즉시 덮임 | 봇이 랜덤 방향 조준, 0 hit | 봇이 자기 팀 받아 적쪽으로 yaw 초기화 |
| TCS<bool> 단방향 신호만 | 봇이 자기 팀 모름 | TCS<byte>로 myTeam 캐리어 |
| BroadcastSnapshot lock 안에서 호출 | 데드락 (자기 lock 잡고 또 자기 lock 재시도) | 항상 lock 풀고 호출 |
| HandleFire의 miss 시 무로그 | "Fire 안 호출됨" vs "다 miss"  구분 불가 | 진단용 miss 로그 (production에선 제거 또는 verbose 등급) |
| 봇 W=70%로 빠르게 직진 | 1.6초 교전 후 등 뒤로 지나침 → 추가 hit 불가 | W 빈도 낮춰 교전 윈도우 길게 |
| RunContinuationsAsynchronously 옵션 누락 | TrySetResult 호출자(ListenLoop)가 InputLoop의 await 이후 코드 동기 실행 → ListenLoop 막힘 | TCS 생성 시 옵션 명시 |
| ct 없이 `await tcs.Task` | 게임 시작 안 되면 영원히 await | Task.WhenAny + ct.Register |
| StartMatch에서 HP/IsDead 등 리셋 안 함 | 매치 재실행 시 이전 매치 상태 누수 (4주차 라운드 재시작 시 문제) | StartMatch에서 모든 필드 명시 리셋 |
| 모든 봇 같은 RNG 시드 | 같은 패턴으로 움직임 → 다양성 없음 | `new Random(nickname.GetHashCode())` |
| S_Snapshot 다 로깅 | 콘솔 로그 폭주 (20Hz × 4봇 × 4 receiver = 320 lines/s) | p1만 5% 확률로 샘플링 |

---

## 8. 학습 부채 — 5주차 회고 또는 방학으로 미룬 것

1. **ray-sphere 수학을 종이에 직접 유도** — Note2와 동일. 4주차 캡슐 hitscan 정밀화 작업에서 회수.
2. **점프 시뮬 콘솔 단위 테스트 분리 검증** — Unity 통합에서 점프 자연스러운지 체감 후 필요 시 콘솔 테스트로 격리.
3. **클라 ↔ 서버 yaw 초기 상태 동기화 패턴 정식화** — 4주차 진짜 로비에서 "S_RoundStart에 스폰 위치/yaw 명시 송신" 으로 해결 예정.
4. **"받자마자 처리"의 비효율 정량화** — 4봇 × 20Hz S_Snapshot = 80 packet/s, 패킷 ~100B → 8KB/s 송신. 3주차 20Hz 다운샘플과 비교 측정.

---

## 9. 통합 테스트 진단 사이클 메서드론 (메타-학습)

```
[증상 관찰] → [가설 N개] → [최소 변경으로 검증] → [반증/확인] → [다음 가설]
```

### 적용 예 (사이클 1 → 2)

| 단계 | 활동 |
|---|---|
| 증상 | Fire 로그 0개 (서버에) |
| 가설 A | HandleFire 안 불림 (디스패치 누락) |
| 가설 B | 봇이 C_Fire 안 보냄 |
| 가설 C | HandleFire 호출되지만 다 miss → 로그 안 찍힘 |
| 최소 변경 | C 검증: miss 로그 한 줄 추가 |
| 결과 | `[Fire] -> miss` 가득 → C 확정, A·B 폐기 |
| 다음 가설 | 왜 다 miss? → 봇 위치/yaw 잘못 → "스폰 위치" 가설 |

### 핵심 원칙
- **한 번에 한 변수만 바꾼다**: 한 사이클에 fire 25% + 시간 5초 + W 20% 동시 변경하면 어느 게 효과인지 모름. (이번엔 시간 압박으로 일부 다중 변경 — 부채)
- **로그는 가장 싼 도구**: 디버거 띄우기 전에 print 한 줄.
- **가설 폐기는 진전**: "A 아님" 도 정보. 거꾸로 보면 후보 1개 좁혀짐.

---

## 10. 다음 단계 — 토요일 + 3주차 준비

### 토요일 (버퍼)
- (선택) 점프/중력 콘솔 단위 테스트 분리 — Y축 적분만 dt 여러 값으로 시뮬
- (선택) Unity 클라와 통합 테스트 — 클라 친구 진도 확인 후
- (선택) `dev` → `main` 머지 + 2주차 회고 짧게

### 3주차 준비 ⭐ (가장 빡센 주차)
- UDP 도입 + 정식 30Hz 틱 + 20Hz 스냅샷 다운샘플
- "받자마자 처리" → "틱 루프 + 입력 큐잉" 마이그레이션
- 라운드/매치 종료 판정
- TCP 끊김 감지 → 사망 처리

**준비 자료**: `ch08_BackgroundIO.pdf`, `ch09_TCP_고급기법.pdf`, UDP는 별도 자료 필요.

---

## 11. AI 활용 회고 — 사이클 5의 진단 과정

### 진행 패턴
- 사용자가 매번 실행 결과 콘솔 로그 풀로 붙여넣음 → AI가 패턴 분석
- AI가 가설 + 최소 변경 제시 → 사용자가 빌드/실행 → 다음 사이클
- **AI가 발견한 root cause** (예: 봇 random yaw 덮어쓰기, 1.6초 교전 윈도우) 가 결정적
- 사용자도 능동적으로 기여 (사이클 5의 "5초/25% 시도" 제안)

### 효율적이었던 것
- **콘솔 로그 풀 공유** → AI가 통계/패턴 파악 가능
- **한 사이클 한 변경** 대체로 지킴
- **진단 로그 (miss) 임시 추가** 후 즉시 root cause 발견

### 개선 여지
- 사이클 4의 "fire 20% / 10초" 는 가설 검증 안 하고 brute force → 실패. 가설을 먼저 명시해야 함.
- "근본 변수가 시간이 아니라 거리"를 사이클 4 끝에야 인식. 사이클 3에서 "교전 윈도우" 개념 미리 식별했어야 함.

---

## 12. 한 줄 요약

> "프로토콜은 만들었으니 양쪽이 같은 약속 지키는지 통합 테스트가 검증한다. 디버깅은 한 변수씩, 로그는 무료, 가설은 명시적으로."
