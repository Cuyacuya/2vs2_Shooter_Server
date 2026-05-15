# DAY4 — 로그인 처리 + MatchManager (목요일)

> 작성일: 2026-05-13 (목)
> 학습 등급: 🔴 (직접/학습 핵심)
> 진행 방식: 채팅으로 코드 받고 직접 타이핑

---

## 0. 오늘 한 작업 요약

WBS 목요일 작업 2개 모두 완료:

| 작업 | 등급 | 커밋 |
|---|---|---|
| 로그인 처리 + sessionToken 발급 + 닉네임 저장 | 🔴 | `811aea7` |
| MatchManager — 자동 매칭 큐 (FIFO, 단일 매치, 5번째 거부) | 🔴 | `47fd063` |

1주차 종료 기준 4항 중 3항 달성:
- [x] 봇 4개 띄우면 자동 매칭 → MATCH STARTING (S_GameStart는 금요일)
- [x] 5번째 봇 거부 (MATCH_IN_PROGRESS)
- [ ] 1번 봇 끊으면 S_MatchingStatus N-1 갱신 (금요일, 패킷 정의 후)
- [x] 패킷 프레이밍 비정상 케이스 처리 (어제까지 완료)

---

## 1. 로그인 처리 (HandleLogin)

### 동작 흐름

```
[봇]                          [서버]
"cuya로 로그인" (C_Login) ──→ 닉네임 검증
                              토큰 발급 (Interlocked)
                              세션에 cuya/1 저장
                              MatchManager에 큐 등록
              ←──────────── "성공, token=1" (S_LoginResult)
```

### 핵심 코드 (ClientSession.HandleLogin)

```csharp
private async Task HandleLogin(C_Login pkt)
{
    // [1] 닉네임 검증
    if (string.IsNullOrWhiteSpace(pkt.Nickname) || pkt.Nickname.Length > 16) {
        var fail = new S_LoginResult { Success = false, Reason = "INVALID_NICKNAME" };
        await SendAsync(fail.Serialize());
        return;
    }

    // [2] 토큰 발급 (스레드 안전)
    ushort newToken = (ushort)Interlocked.Increment(ref _nextToken);

    // [3] 세션 상태 업데이트
    SessionToken = newToken;
    Nickname = pkt.Nickname;

    // [4] 매칭 큐 등록
    bool joined = MatchManager.Instance.TryEnqueue(this);
    if (!joined) {
        var fail = new S_LoginResult { Success = false, Reason = "MATCH_IN_PROGRESS" };
        await SendAsync(fail.Serialize());
        return;
    }

    // [5] 성공 응답
    await SendAsync(new S_LoginResult { Success = true, SessionToken = newToken }.Serialize());
}
```

### 4가지 케이스 (봇 검증)
| 입력 | 결과 |
|---|---|
| `"cuya"` | success=True, token=1 |
| `"alice"` (이어서) | success=True, token=2 (Interlocked로 증가) |
| `""` (빈 닉네임) | success=False, reason='INVALID_NICKNAME' |
| 20자 닉네임 | success=False, reason='INVALID_NICKNAME' |

### 로그인 성공/실패 시 세션 상태 변화

| | 성공 | 실패 |
|---|---|---|
| `SessionToken` | 0 → 새 토큰 | 그대로 0 |
| `Nickname` | "" → 받은 닉네임 | 그대로 "" |
| `Interlocked.Increment` | 호출됨 | 호출 안 됨 |
| 세션 끊김 | 안 끊김 | 안 끊김 |

핵심: 실패 시에도 **세션은 살아있음**. 응답만 보내고 끝.

---

## 2. MatchManager — 자동 매칭 큐

### 동작
```
p1 로그인 → 큐 [p1]              (1/4)
p2 로그인 → 큐 [p1, p2]          (2/4)
p3 로그인 → 큐 [p1, p2, p3]      (3/4)
p4 로그인 → 큐 [p1, p2, p3, p4]  (4/4) → MATCH STARTING
p5 로그인 → MATCH_IN_PROGRESS 거부
```

### 핵심 코드 (MatchManager.cs)

```csharp
public class MatchManager
{
    private static readonly MatchManager _instance = new();
    public static MatchManager Instance => _instance;

    private const int MaxPlayers = 4;
    private readonly List<ClientSession> _waiting = new();
    private readonly object _lock = new();
    private bool _matchInProgress = false;

    private MatchManager() { }   // 외부 new 금지

    public bool TryEnqueue(ClientSession session)
    {
        lock (_lock) {
            if (_matchInProgress) return false;
            _waiting.Add(session);
            if (_waiting.Count >= MaxPlayers) StartMatch();
            return true;
        }
    }

    public void Remove(ClientSession session)
    {
        lock (_lock) { _waiting.Remove(session); }
    }

    private void StartMatch()
    {
        _matchInProgress = true;
        // TODO(금): 팀 배정 + S_GameStart 송신
    }
}
```

### 핵심 설계 결정 6가지

#### ① 싱글톤 패턴
```csharp
private static readonly MatchManager _instance = new();
public static MatchManager Instance => _instance;
private MatchManager() { }
```
- `_instance`는 `static readonly` → 클래스 로드 시 1번 생성
- 생성자 `private` → 외부 `new` 금지
- 어디서든 `MatchManager.Instance` 접근

#### ② `lock` — 동시성 보호
- `_lock`은 잠금용 더미 `object`. 객체 내용 안 쓰고 식별자만 사용
- 여러 세션이 동시 호출 시 race condition 방지
- **절대 `lock(this)` 또는 `lock(typeof(...))` 쓰지 말 것** (외부와 충돌)

#### ③ `List<ClientSession>` 사용 (Queue 아님)
- FIFO지만 중간 제거(끊김)도 필요 → List가 유연
- `.Add` / `.Remove` / `.Count`

#### ④ `_matchInProgress` 플래그
- 4명 차는 순간 `true` → 추가 접속 거부
- 1주차엔 단방향. 3주차 매치 종료 시 `false`로 복귀

#### ⑤ `Remove`를 `RunAsync` finally에서
- 봇 끊기면 자동으로 큐에서 빠짐
- 안 하면 좀비 세션 누적

#### ⑥ 등록 순서 — 닉네임/토큰 먼저 → TryEnqueue
- MatchManager 로그에 닉네임 보이게 하기 위해

---

## 3. 핵심 개념 — async / await / Task

### async all the way
```
HandleLogin (async Task)
   ↑ await
HandlePacket (async Task)
   ↑ await
ParsePackets (async Task)
   ↑ await
RunAsync (async Task)
```
- 한 곳에서 `await` 쓰면 호출 체인 전체가 `async Task`로 전파
- 끊으면 fire-and-forget → **예외 사라짐 + 동시성 버그**

### `_ =` (discard) vs `await`
| 표현 | 의미 |
|---|---|
| `await foo()` | 끝날 때까지 대기 |
| `_ = foo()` | fire-and-forget, 즉시 다음 줄 |
| `foo()` (그냥 호출) | 컴파일 경고 CS4014 |

### Task의 정체
- `Task` = "미래에 끝날 작업"의 추상화
- `Task<T>` = 끝나면 T 값을 줌
- `async` 메서드는 항상 Task 반환

---

## 4. lock vs SemaphoreSlim — 동시성 도구 비교

### 한 줄 결론
> **`await` 있으면 `SemaphoreSlim`, 없으면 `lock`.**

### 비교표

| 항목 | `lock` | `SemaphoreSlim` |
|---|---|---|
| 종류 | C# 언어 키워드 | .NET 클래스 |
| 동기/비동기 | 동기만 | 동기 + 비동기 |
| `await` 내부 사용 | ❌ 불가 | ✅ 가능 (핵심 차이) |
| 카운트 | 항상 1개 | N개 설정 가능 |
| 성능 | 매우 빠름 | 약간 무거움 |
| 사용법 | `lock(x) { ... }` | `try/finally`로 Release |
| Dispose 필요 | ❌ | ✅ |
| 타임아웃/취소 | ❌ | ✅ |

### 우리 프로젝트의 선택
| 위치 | 선택 | 이유 |
|---|---|---|
| `MatchManager` | `lock` | 내부에 `await` 없음. 빠르고 간결 |
| `ClientSession.SendAsync` | `SemaphoreSlim` | `await _stream.WriteAsync(...)` 필요 |

### 왜 lock 안에서 await 안 되나
- `lock`은 "잡은 스레드 = 푸는 스레드" 보장 필요
- `await`는 다른 스레드에서 재개될 수 있음 → 위반
- C# 컴파일러가 컴파일 단계에서 차단 (CS1996)

### 비동기 락 대안 패턴
```csharp
// 옵션 1: 락 안에선 복사만, await는 밖에서
ClientSession[] snapshot;
lock (_lock) { snapshot = _waiting.ToArray(); }
foreach (var s in snapshot) await s.SendAsync(packet);

// 옵션 2: SemaphoreSlim 사용
await _sem.WaitAsync();
try { await SomethingAsync(); }
finally { _sem.Release(); }
```

---

## 5. `Interlocked` — 락 없는 안전한 연산

```csharp
ushort newToken = (ushort)Interlocked.Increment(ref _nextToken);
```

### 왜 필요한가
- 일반 `_nextToken++`는 사실 **3개 CPU 명령** (읽기/+1/쓰기)
- 두 스레드 동시 호출 시 같은 값 두 번 가능 → race condition

### `Interlocked`의 동작
- **1개 원자적 CPU 명령**으로 처리. 절대 겹치지 않음
- 단일 변수 증감/교환에 적합. `lock`보다 가벼움

### 주요 메서드
- `Interlocked.Increment(ref x)` — x = x + 1, 새 값 반환
- `Interlocked.Decrement(ref x)` — x = x - 1
- `Interlocked.Add(ref x, n)` — x += n
- `Interlocked.Exchange(ref x, newVal)` — x = newVal, 이전 값 반환
- `Interlocked.CompareExchange(ref x, newVal, expected)` — x가 expected이면 newVal로 교체 (CAS)

### wrap-around 인지
- `Interlocked.Increment`의 반환 타입은 `int`(4바이트)
- `(ushort)`로 캐스팅 → 65535 넘으면 0으로 wrap
- 1주차엔 4명 제한이라 문제없음

---

## 6. `this` 키워드

```csharp
// ClientSession.HandleLogin 안에서
bool joined = MatchManager.Instance.TryEnqueue(this);
```

**`this`** = 현재 메서드를 실행 중인 객체 (자기 자신)

### 풀어쓰면
```csharp
// 봇 p1 접속 → ClientSession 객체 sessionA 생성
// sessionA.HandleLogin 호출
// 그 안에서 this == sessionA

// 봇 p2 접속 → sessionB
// sessionB.HandleLogin 호출
// 그 안에서 this == sessionB
```

→ MatchManager._waiting 리스트엔 각각의 ClientSession 인스턴스 참조가 쌓임.

비유: 학생 A가 "**저(this)** 등록할게요" → 등록부에 A 추가. 호출자마다 다른 객체.

---

## 7. `_` 접두사 컨벤션

### 규칙 (Microsoft 권장)
| 무엇 | 이름 규칙 | 예 |
|---|---|---|
| 클래스의 `private` 필드 | `_camelCase` | `_nextToken`, `_buffer`, `_lock` |
| 메서드 매개변수 / 지역 변수 | `camelCase` | `client`, `read` |
| `public` 프로퍼티 / 메서드 | `PascalCase` | `Nickname`, `RunAsync` |
| 상수 / `static readonly` | `PascalCase` | `MaxPlayers` |

### 왜 붙이나
- 변수 종류가 한눈에 보임
- 필드 vs 매개변수 이름 충돌 해결
```csharp
public ClientSession(TcpClient client) {
    _client = client;   // 필드 vs 매개변수 명확
}
```

### ⚠️ 문법이 아닌 관례
컴파일러는 신경 안 씀. 안 붙여도 동작하지만 컨벤션 따르는 게 협업에 좋음.

---

## 8. ClientSession의 모든 *Async 메서드 — 누가 만든 거?

### 우리가 만든 것
| 메서드 | 역할 |
|---|---|
| `RunAsync()` | 한 세션의 수신 루프 (평생) |
| `SendAsync(byte[])` | 송신 (세마포어로 직렬화) |
| `ParsePackets()` | 누적 버퍼에서 패킷 잘라냄 |
| `HandlePacket(...)` | packetId로 디스패치 |
| `HandleLogin(C_Login)` | 로그인 처리 |

### .NET 라이브러리 (`System.Net.Sockets`)
| 메서드 | 역할 |
|---|---|
| `listener.AcceptTcpClientAsync()` | 새 접속 받기 |
| `client.ConnectAsync(host, port)` | 서버에 접속 (봇 측) |
| `_stream.ReadAsync(...)` | 바이트 읽기 |
| `_stream.WriteAsync(...)` | 바이트 쓰기 |

### .NET 라이브러리 (`System.Threading`)
| 메서드 | 역할 |
|---|---|
| `_sendLock.WaitAsync()` | 세마포어 대기 |
| `Task.Delay(ms)` | 비동기 N밀리초 대기 |
| `Task.WhenAll(...)` | 여러 Task 모두 끝날 때까지 대기 |
| `Interlocked.Increment(ref ...)` | 원자적 +1 |

### C# 언어 자체
| 표현 | 의미 |
|---|---|
| `async` 키워드 | "이 메서드는 비동기" |
| `await` 키워드 | "여기서 Task 대기" |
| `Task` / `Task<T>` 타입 | .NET 기본 타입, "미래 작업" |

### 한 줄
> **우리는 .NET이 제공하는 비동기 메커니즘 위에 얹어 게임 서버 로직을 만든 것.**

---

## 9. 핵심 패턴 5가지 (반복되는 구조)

### 패턴 1: fire-and-forget
```csharp
_ = session.RunAsync();
```
백그라운드로 던지고 즉시 다음 줄.

### 패턴 2: await 체인
```csharp
public async Task A() { await B(); }
public async Task B() { await C(); }
```
async가 호출 체인 전체로 전파.

### 패턴 3: try/finally로 자원 정리
```csharp
try { ... } catch { ... } finally { Close(); }
```
예외든 정상이든 무조건 정리.

### 패턴 4: 비동기 락
```csharp
await sem.WaitAsync();
try { await Something(); }
finally { sem.Release(); }
```

### 패턴 5: 누적 버퍼 + 비동기 읽기
```csharp
while (true) {
    int read = await stream.ReadAsync(buf, pos, len - pos);
    if (read == 0) break;
    pos += read;
}
```

---

## 10. 봇 측 변경 — LoginAsync + 연결 유지

### 핵심 변화
- 응답 받고 끊지 않음 (큐 채우려면 연결 유지 필요)
- `TcpClient`를 List에 보관 → 마지막에 한 번에 Dispose

```csharp
static async Task<TcpClient?> LoginAsync(string nickname)
{
    var client = new TcpClient();
    await client.ConnectAsync(Host, Port);
    var stream = client.GetStream();
    await stream.WriteAsync(new C_Login { Nickname = nickname }.Serialize());

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var result = await ReadOnePacketAsync(stream, cts.Token);
    // 응답 파싱 + 로그
    return client;   // 연결 유지 채로 반환
}

// 4명 채우기 + 5번째 거부 확인
var clients = new List<TcpClient>();
for (int i = 1; i <= 4; i++) clients.Add(await LoginAsync($"p{i}"));
var c5 = await LoginAsync("p5");   // 거부됨
```

---

## 11. 자주 만나는 함정

### 컴파일 에러
| 메시지 | 원인 | 대응 |
|---|---|---|
| `CS0120: 'Stream'에 개체 참조 필요` | 매개변수 이름 오타 (`Stream` vs `networkStream`) | 정확한 변수명 사용 |
| `CS4014: await 없이 호출` | async 메서드를 await 없이 호출 | `await` 추가 or `_ =` |
| `CS1996: lock 안에서 await 못 씀` | lock 블록 안에 await | SemaphoreSlim 또는 lock 밖으로 |

### 런타임/로직 버그
1. **Remove 누락** → 큐에 좀비 세션
2. **싱글톤 public 생성자** → 인스턴스 두 개 가능 (큐 두 개 = 망함)
3. **HandlePacket await 누락** → 동시 실행 시 버퍼 덮어쓰기

---

## 12. 봇 검증 결과

### 기대 출력 (서버)
```
[Match] p1 joined queue (1/4)
[Session ...] login ok : nickname=p1, token=1
[Match] p2 joined queue (2/4)
...
[Match] p4 joined queue (4/4)
[Match] === MATCH STARTING (4 players) ===
  - p1 (token=1)
  - p2 (token=2)
  - p3 (token=3)
  - p4 (token=4)
[Match] reject p5: match in progress
[Session ...] login rejected : match in progress
```

### 검증 포인트
- ✅ 1/4 → 2/4 → 3/4 → 4/4
- ✅ MATCH STARTING 출력
- ✅ p5 거부 (MATCH_IN_PROGRESS)
- ✅ 끊김 시 큐에서 자동 제거

---

## 13. 셀프 퀴즈 (다음 학습 시 답해보기)

1. `MatchManager.Instance.TryEnqueue` 가 false 반환하는 조건은?
2. `_lock`이라는 변수에 들어 있는 값은 뭔가? 왜 `object`면 충분한가?
3. 만약 100명이 1초 안에 다 접속하면 어떻게 되나?
4. 봇이 로그인 직후 즉시 끊으면 `MatchManager._waiting`에서 어떻게 빠져나가나?
5. `lock` 안에서 `Console.WriteLine`을 하는데, 그게 락을 오래 잡지 않을까?
6. 매치 시작 후 `MatchManager.Instance.Remove(session)`을 호출하면?
7. `_matchInProgress`를 `false`로 다시 바꾸는 코드를 어디에 둘 거면 어디가 적절할까?

---

## 14. 오늘 커밋 이력

| 커밋 | 내용 |
|---|---|
| `811aea7` | 로그인 처리 구현 (sessionToken 발급 + 닉네임 검증 + S_LoginResult 응답) |
| `47fd063` | MatchManager 자동 매칭 큐 추가 (FIFO, 단일 매치, 5번째 거부) |

---

## 15. 내일(금요일) 할 일

WBS 인용:
- [Shared] S_MatchingStatus, S_GameStart 패킷 정의 (🟡)
- [서버] S_MatchingStatus 브로드캐스트 (대기 중인 모두에게 N/4) (🟡)
- [서버] 자동 팀 배정 (1,3=Red / 2,4=Blue) + S_GameStart 송신 (🔴)
- [봇] 봇 클라 골격 (🟡)
- [공동] 통합 테스트 (🔴)

→ 금요일 끝나면 1주차 종료 기준 4항 ✅ 달성 + `dev`로 PR.

---

## 핵심 외울 키워드 (요약)

- **`async all the way`** — async는 호출 체인 위로 전파됨
- **`Interlocked.Increment`** — 락 없는 원자적 +1. 단일 변수 증감의 정답
- **싱글톤 패턴** — `static readonly` + `private` 생성자. 서버 전체에 1개
- **`lock` vs `SemaphoreSlim`** — await 있으면 SemaphoreSlim, 없으면 lock
- **`lock(_lock)`** — `lock`은 키워드, `_lock`은 더미 object. 절대 `lock(this)` 금지
- **`this`** — 현재 메서드를 실행 중인 객체. 호출자마다 다름
- **`_` 접두사** — private 필드 표시 컨벤션 (문법 아님)
- **데이터(Shared) vs 행동(GameServer)** — 매니저는 행동이라 GameServer에
- **5번째 거부 = `_matchInProgress` 플래그**가 true가 되면 TryEnqueue가 false 반환
- **세션 객체화 + 매니저 싱글톤** — 게임 서버의 표준 골격
