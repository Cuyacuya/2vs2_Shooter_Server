# Day6 — 5/15(목) 트러블슈팅

> 날짜별 발생한 오류 기록 (각 항목: 문제 / 원인 / 해결 / 결과 / 배운점)

---

## #1 — S_LoginResult보다 S_MatchingStatus가 먼저 도착 (송신 순서 역전)

### 문제 사항
봇 4개로 통합 테스트 실행 시:
- 봇이 LoginAsync에서 success 로그를 출력하지 않음 (`[p1] success=...` 라인 누락)
- ListenLoop에서 `unknown packetId=2` (S_LoginResult) 메시지가 봇마다 1번씩 찍힘
- p1이 `S_MatchingStatus 1/4`를 받지 못하고 2/4부터 받음

### 원인 분석
`ClientSession.HandleLogin`의 호출 순서:

```csharp
bool joined = MatchManager.Instance.TryEnqueue(this);  // 1) 내부에서 S_MatchingStatus를 fire-and-forget 송신
...
await SendAsync(ok.Serialize());                        // 2) S_LoginResult 송신
```

- `TryEnqueue` 안의 `BroadcastStatus`가 `_ = s.SendAsync(bytes)` (fire-and-forget)로 큐잉
- 그 후 HandleLogin이 `await SendAsync(S_LoginResult)` 호출
- 둘 다 같은 `_sendLock`을 두고 경쟁하는데, **fire-and-forget 쪽이 먼저 시작됐기 때문에 lock을 먼저 잡음**
- 결과: 와이어상 **S_MatchingStatus → S_LoginResult** 순서

봇 쪽 파급:
- `LoginAsync.ReadOnePacketAsync`가 첫 패킷을 S_MatchingStatus로 받음 → `if (id == S_LoginResult)` 분기 못 탐 → success 로그 누락
- 같은 TCP read에 두 패킷이 묶여 들어왔을 때 첫 패킷만 파싱하고 로컬 buf 폐기 → 두 번째 패킷(S_LoginResult) 유실 또는 다음 ReadAsync에서 잡힘 → `unknown packetId=2`
- S_MatchingStatus(1/4)가 buf에 같이 들어왔다 폐기 → 1/4 누락

프로토콜 의미상으로도 클라가 인증 결과(토큰) 받기 **전에** 매칭 상태가 오는 건 잘못된 흐름.

### 해결 방법
**서버 사이드 재배치**: `MatchManager.TryEnqueue`가 broadcast까지 책임지지 않고 snapshot만 반환. HandleLogin이 S_LoginResult를 먼저 await 송신한 뒤 broadcast 호출.

```csharp
// Before
public bool TryEnqueue(ClientSession session) {
    lock (_lock) { ... StartMatch(); }
    if (snapshot != null) BroadcastStatus(snapshot);   // ← 응답 전에 송신
    return true;
}

// After
public bool TryEnqueue(ClientSession session,
                       out List<ClientSession>? statusSnapshot,
                       out List<ClientSession>? gameStartSnapshot) {
    lock (_lock) { ... }   // snapshot만 채우고 반환
}

// HandleLogin
bool joined = MatchManager.Instance.TryEnqueue(this, out var statusSnap, out var gameStartSnap);
if (!joined) { await SendAsync(fail); return; }
await SendAsync(ok);   // ← S_LoginResult 먼저
if (statusSnap != null) MatchManager.Instance.BroadcastStatusNow(statusSnap);
if (gameStartSnap != null) MatchManager.Instance.BroadcastGameStartNow(gameStartSnap);
```

### 결과
재테스트 후 봇 출력 순서가 `S_LoginResult → S_MatchingStatus → ... → S_GameStart`로 정상화.

### 배운점
- **fire-and-forget(`_ = SendAsync(...)`)과 await SendAsync가 같은 lock을 경쟁할 때 순서가 역전된다.**
  fire-and-forget은 호출 즉시 Task를 만들고 내부 작업을 시작 → await 쪽보다 lock 우선권을 가짐.
- **프로토콜 설계는 응답 순서까지 의미를 가진다.** "인증 → 상태"는 의미상 올바른 순서이고, 코드도 그렇게 보장해야 함.
- **TryEnqueue가 송신 책임까지 떠안으면 호출자가 응답 타이밍을 제어할 수 없다.** 큐 변경(lock 안)과 송신(lock 밖, await 가능)을 분리하면 호출자가 응답과 broadcast 사이 순서를 결정 가능.
- **봇 측 `ReadOnePacketAsync`가 한 패킷만 처리 후 로컬 buf를 버리는 구조는 같은 read에 묶여온 다음 패킷을 유실시킨다.** 누적 버퍼를 가진 stateful reader가 필요. ListenLoop는 이미 그렇게 작성됨.
