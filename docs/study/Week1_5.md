# Week1 Note 5 — 매칭 상태 브로드캐스트 + 자동 팀 배정 + 1주차 통합 (금요일)

> 작성일: 2026-05-15 (금)
> 학습 등급: 🟡 (보조) ~ 🔴 (직접). 청크 C는 🔴 → 🟢으로 강등 후 코드+재작성으로 학습.
> 진행 방식: 청크별로 잘라가며 → 빌드 → 통합 테스트 → 발견된 버그 수정

---

## 0. 오늘 한 작업 요약

WBS 금요일 작업 전 범위 + 통합 테스트 + 발견 버그 수정까지 완료.

| 청크 | 작업 | 등급 | 커밋 |
|---|---|---|---|
| A | `S_MatchingStatus` / `S_GameStart` 패킷 정의 | 🟡 | `93d29cf` |
| B | 매칭 큐 상태 브로드캐스트 | 🟡 | `6f8cfd1` |
| C | 자동 팀 배정 + `S_GameStart` 송신 | 🔴→🟢 | `7d96791` |
| D | 봇 클라에 패킷 수신 루프 | 🟡 | `1340563` |
| E + 수정 | 통합 테스트 + 송신 순서 버그 수정 | 🔴 | `c4500f3` |

**1주차 종료 기준 4항 전부 달성**:
- [x] 봇 4개 → 자동 매칭 → S_GameStart 수신 (MyTeam 사람별 다름)
- [x] 5번째 봇 거부 (MATCH_IN_PROGRESS)
- [x] 봇 끊김 시 N-1 갱신
- [x] 패킷 프레이밍 (쪼개짐/합쳐짐) 처리

---

## 1. 핵심 개념 ① — Snapshot 패턴

### 문제

매칭 큐(`_waiting`)는 여러 스레드가 동시에 만지는 공유 자원. 큐 변동 시 대기 중 모든 클라에게 `S_MatchingStatus`를 송신해야 함.

**순진한 접근**:
```csharp
lock (_lock) {
    _waiting.Add(session);
    foreach (var s in _waiting) {
        _ = s.SendAsync(bytes);  // I/O를 lock 안에서!
    }
}
```

**왜 안 되나**: lock 안에서 네트워크 I/O 발생 → 4명에게 송신하는 동안 다른 스레드의 `TryEnqueue`/`Remove`가 모두 대기. 한 명의 네트워크가 느리면 서버 전체가 마비.

### 해결 — Snapshot

```csharp
List<ClientSession>? snapshot = null;

lock (_lock) {
    _waiting.Add(session);
    snapshot = new List<ClientSession>(_waiting);  // 사본 떠서 빠져나옴
}

// lock 풀린 상태에서 송신
foreach (var s in snapshot) {
    _ = s.SendAsync(bytes);
}
```

**핵심**:
- `new List<>(_waiting)` = **얕은 복사**. 리스트 상자는 새로 만들고, 안의 ClientSession 참조는 공유. 즉 "그 순간 줄 서 있던 사람들"을 사진 찍는 것.
- lock 안에서는 **메모리 복사만** → 매우 짧음.
- 송신 도중 `_waiting`이 어떻게 바뀌든 snapshot은 안 변함 → 크래시 없음.

### 비유

`_waiting` = 카페 줄. `snapshot` = 그 순간 줄 사진. 사진 보고 안내방송하면 중간에 사람이 들고 나도 누락/중복 없음.

### 패턴 일반화

> **"공유 자원을 짧게 잡아서 사본 뜨고, 사본으로 작업해라."**

게임 서버, 이벤트 시스템, 옵저버 패턴 등 어디서나 쓰는 동시성 정석 패턴.

---

## 2. 핵심 개념 ② — Lock의 황금 규칙

| 규칙 | 이유 |
|---|---|
| 공유 자원 만지면 lock | race condition 방지 (List 내부 깨짐, Count 검사 race) |
| **lock은 짧게** | lock 잡힌 동안 다른 스레드 대기 → 처리량 하락 |
| **lock 안에서 I/O 금지** | I/O는 오래 걸림 → 서버 전체 마비 위험 |
| **lock 안에서 await 금지** | await 너머로 lock 안전성 보장 안 됨, 데드락 가능 |

### lock이 없으면?

- `_waiting.Add()` 자체가 스레드 안전 X → 내부 배열 깨짐, 데이터 손실
- `_matchInProgress` 체크가 race → 5번째 거부 가끔 뚫림
- 재현 안 됨 (타이밍 의존) → 데모 중에 터지는 게 무서운 점

---

## 3. 핵심 개념 ③ — 사람별 직렬화 (S_GameStart)

### S_MatchingStatus는 단순

```csharp
byte[] bytes = pkt.Serialize();  // 한 번만
foreach (var s in sessions) _ = s.SendAsync(bytes);  // 같은 byte[] 재사용
```

모두 같은 정보(`current/max`) → byte[] 한 번 만들고 전원에게 같은 걸 보냄.

### S_GameStart는 다름

```csharp
foreach (var receiver in sessions) {
    var pkt = new S_GameStart {
        MyTeam = receiver.Team,   // ← 받는 사람마다 다름!
        Players = playerInfos,    // ← 4명 모두 동일
    };
    _ = receiver.SendAsync(pkt.Serialize());  // 사람마다 Serialize 호출
}
```

**왜 다른가**: `MyTeam`이 "받는 사람의 자기 팀". byte[]를 한 번만 만들면 모두 같은 팀으로 인식 → 잘못된 정보.

**최적화 포인트**: `Players` 리스트는 4명 모두 동일하므로 한 번만 만들고 4개 패킷이 공유 (`Players = playerInfos`). 같은 List 참조해도 mutate 안 하면 안전.

### 일반 원칙

> **"수신자에 따라 다른 필드가 있으면 사람별 직렬화. 다 같으면 한 번만."**

---

## 4. 핵심 개념 ④ — Fire-and-forget vs await (송신 순서 race)

### 사건

봇 통합 테스트에서 발생한 버그:
- 봇이 `S_LoginResult` success 로그를 못 찍음
- `unknown packetId=2` 메시지가 봇마다 1번씩
- `S_MatchingStatus 1/4`가 누락

### 원인

`HandleLogin`의 호출 순서:

```csharp
bool joined = MatchManager.Instance.TryEnqueue(this);  // 내부에서 _ = s.SendAsync(matchingStatus)
                                                        // ← fire-and-forget 즉시 큐잉
...
await SendAsync(ok.Serialize());  // ← S_LoginResult, await
```

**`_sendLock` 경쟁**:
- fire-and-forget 쪽이 **호출 즉시** SemaphoreSlim.WaitAsync 시작
- await 쪽은 **그 다음 줄**에서야 WaitAsync
- 결과: fire-and-forget이 먼저 lock 잡음 → 와이어상 `S_MatchingStatus → S_LoginResult` 순서로 송신

### 교훈

> **fire-and-forget(`_ = ...`)은 호출 즉시 작업이 시작된다. await SendAsync 한 줄 위에 있으면 그게 먼저 큐잉되어 lock을 먼저 잡는다.**

이건 직관과 반대. 코드 순서로는 await가 나중이라 "당연히 fire-and-forget 다음"일 것 같지만, **두 작업 모두 비동기 큐에 동시에 들어가서 lock 경쟁**.

### 해결 — 응답 후 broadcast

`TryEnqueue`가 broadcast까지 책임지지 않고 snapshot만 반환:

```csharp
// Before
public bool TryEnqueue(ClientSession session) {
    lock (_lock) { ... }
    BroadcastStatus(snapshot);  // 호출자가 제어 못 함
    return true;
}

// After
public bool TryEnqueue(ClientSession session,
                       out List<ClientSession>? statusSnap,
                       out List<ClientSession>? gameStartSnap)
{
    lock (_lock) { ... snapshot만 채우고 return; }
}

// HandleLogin
bool joined = MatchManager.Instance.TryEnqueue(this, out var s1, out var s2);
if (!joined) { ... return; }
await SendAsync(ok.Serialize());           // ← 1) 응답 먼저
if (s1 != null) BroadcastStatusNow(s1);    // ← 2) 그 다음 broadcast
if (s2 != null) BroadcastGameStartNow(s2);
```

**일반 원칙**:
- **응답 타이밍이 중요할 땐 송신을 호출자가 제어**해야 함
- TryEnqueue가 너무 많은 책임을 가지면(큐 변경 + 응답 송신 + 브로드캐스트) 호출자가 순서 제어 불가
- "큐 변경(동기, lock)" / "송신(비동기, lock 밖)" / "응답 vs 브로드캐스트 순서" 분리

---

## 5. 핵심 개념 ⑤ — out 매개변수와 API 시그니처

### 왜 out을 썼나

`TryEnqueue`는 두 가지 정보를 반환해야 함:
- 큐 등록 성공 여부 (bool)
- 어떤 broadcast를 할지 (statusSnapshot 또는 gameStartSnapshot)

C#의 `out`은 다중 반환의 표준 방식. 튜플(`(bool, List?, List?)`)도 가능하지만 `Try*` 패턴은 관례상 `out`.

```csharp
if (Map.TryGetValue(key, out var value)) { ... }   // .NET 표준 패턴
if (TryEnqueue(session, out var s1, out var s2)) { ... }
```

### 학습 포인트

> **API 시그니처를 바꾸는 건 큰 결정.** 호출자 코드도 같이 바꿔야 함. 하지만 응답 순서 같은 본질적 문제는 시그니처 변경이 정답일 때가 많음.

---

## 6. 핵심 개념 ⑥ — 누적 버퍼 프레이밍 (봇 측)

### 단발성 vs 지속 수신

`LoginAsync.ReadOnePacketAsync`는 **한 패킷 받고 함수 종료** → 로컬 buf 폐기.
- 같은 read에 두 패킷 묶여 들어오면 두 번째는 유실 가능
- 응답 한 번 받고 끝나는 케이스에만 적합

`ListenLoopAsync`는 **지속 수신** → 누적 버퍼 유지:
```csharp
var buf = new byte[4096];
int writePos = 0;

while (...) {
    int read = await stream.ReadAsync(buf.AsMemory(writePos), ct);
    writePos += read;

    int readPos = 0;
    while (writePos - readPos >= 6) {           // 헤더 6바이트 있나?
        ushort len = BitConverter.ToUInt16(buf, readPos);
        int total = 6 + len;
        if (writePos - readPos < total) break;  // 완성 안 됨

        // 한 패킷 디스패치
        ...
        readPos += total;
    }

    // 소비한 만큼 앞으로 당김
    int remain = writePos - readPos;
    if (remain > 0) Buffer.BlockCopy(buf, readPos, buf, 0, remain);
    writePos = remain;
}
```

**핵심**:
- 한 번의 `ReadAsync`에 여러 패킷이 같이 와도 while 루프로 다 처리
- 한 패킷의 일부만 와도 break → 다음 ReadAsync에서 이어붙임
- 소비 후 남은 바이트는 `Buffer.BlockCopy`로 앞으로 당겨 다음 read에 이어 받음

이건 1주차 화요일 서버 측에서 한 것(`ClientSession.ParsePackets`)의 클라 버전.

### 일반 원칙

> **TCP는 메시지 경계가 없다.** 한 번의 read에 0개, 1개, 또는 여러 패킷이 와있을 수 있고, 한 패킷이 여러 read에 쪼개질 수도 있다. 누적 버퍼 + length-prefixed로 매번 경계를 직접 파악해야 한다.

---

## 7. 셀프 퀴즈 (오늘 배운 거 점검용)

1. `snapshot = _waiting` (참조 대입) vs `snapshot = new List<>(_waiting)` (사본). 전자가 깨지는 시나리오를 한 줄로 설명하라.
2. `S_GameStart`에서 4명 모두 같은 byte[]를 보내면 어떤 잘못된 정보가 클라에 전달되는가?
3. `_matchInProgress = true`를 `BroadcastGameStart` **이후**에 세팅하면 어떤 race가 생기는가?
4. fire-and-forget `_ = s.SendAsync(bytes)`와 다음 줄의 `await SendAsync(other)`. 두 호출 모두 같은 SemaphoreSlim을 쓴다. 어느 쪽이 먼저 lock을 잡는가?
5. 봇의 `LoginAsync.ReadOnePacketAsync`가 한 패킷 받고 함수 종료할 때, 같은 read에 묶여 들어온 두 번째 패킷의 운명은? 어떻게 손실되거나 살아남는가?
6. `TryEnqueue`가 broadcast까지 책임지던 구조를 snapshot 반환으로 바꾼 이유는?
7. 누적 버퍼 프레이밍에서 `if (writePos - readPos < total) break;`가 없으면 어떤 잘못된 일이 벌어지는가?

---

## 8. 다음 주차 예고 (2주차 — TCP 인게임 게임플레이)

- `C_Input` 받아 위치 갱신 (받자마자 처리, WASD 먼저)
- 점프 + 중력 시뮬레이션 (`v += a*dt`, isGrounded)
- 마우스 룩 (yaw/pitch) 동기화
- `C_Fire` → ray-sphere hitscan → HP 감소 → `S_HitResult`
- 봇에 랜덤 이동/점프/사격 추가
- **2주차의 함정**: 정식 틱 루프부터 짜려는 유혹 → 절대 금지. 받자마자 처리로 시작, 3주차에 정식 도입.

---

## 9. 오늘의 메타 학습

- **🔴 → 🟢으로 강등할 때**: 학습 손실이 있긴 하지만, 라인별 해설을 꼼꼼히 읽으면 절반은 회수됨. 다음에 비슷한 작업 만나면 본인이 짤 수 있을지가 진짜 점검 지표.
- **통합 테스트가 버그를 잡는다**: 단위로는 다 통과해도 합치면 깨지는 케이스(송신 순서 race)는 통합 테스트로만 보임.
- **트러블슈팅 노트는 자산**: DAY6 트러블슈팅이 오늘의 버그 원인/해결을 다 기록 → 다음 비슷한 케이스 만나면 재참조 가능.
