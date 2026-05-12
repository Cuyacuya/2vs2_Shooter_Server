# DAY3 — 1주차 복습 + 핵심 개념 정리

> 작성일: 2026-05-12 (수)
> 학습 방식: WBS 등급별 워크플로 정착 + 지금까지 만든 코드 전체 복습

---

## 0. 오늘의 메타: 작업 진행 방식 결정

WBS의 🔴/🟡/🟢 등급별 절차를 본격 도입.

| 등급 | Claude 역할 | 사용자 절차 |
|---|---|---|
| 🟢 적극 | 완성 코드 제공 | 한 줄씩 읽고 모르는 줄은 질문 → 커밋 |
| 🟡 보조 | 완성 코드 + 라인별 해설 | 읽고 → 가리고 → 재작성 → diff 비교 |
| 🔴 직접 | 개념/그림/함정만 설명 (코드 X) | 30분 타임박스 → 막히면 단계적 힌트 |

**원칙**: 한 작업씩 끊어가기. 1 스크립트 → 핵심 개념 학습 → 셀프 퀴즈/실험 → 커밋 → 다음.

→ `docs/WBS_HD_Server_v2.md` 의 "작업 진행 방식" 섹션에 박제.

---

## 1. 솔루션 구조 — 왜 3개 프로젝트?

```
Game.sln
├── server/GameServer/     → GameServer.exe (서버 실행 파일)
├── bot/BotClient/         → BotClient.exe  (봇 실행 파일)
└── shared/Shared/         → Shared.dll     (공통 라이브러리)
```

### 핵심
- **공통 패킷 코드는 한 곳(Shared)에 두고 서버/봇/유니티가 참조**해야 일관성 유지
- 따로 두면 한쪽 고치고 다른 쪽 깜빡 → 반드시 깨짐

### `.csproj` 파일 = C# 프로젝트의 설명서 (XML)
- `<OutputType>Exe</OutputType>` → 실행 파일. 생략하면 라이브러리(.dll)
- `<TargetFramework>net8.0</TargetFramework>` vs `netstandard2.1`
  - 서버/봇: net8.0 (최신 기능)
  - Shared: **netstandard2.1** (Unity 호환 필요해서)
- `<ProjectReference Include="...">` → 다른 프로젝트 참조 (`using Shared;` 가능)

### 외울 한 줄
> **`Exe` = 시작점 있는 프로그램, `Library`(.dll) = 누가 불러주는 부품.**

---

## 2. async / await / Task — 왜 비동기?

### 동기 vs 비동기

```csharp
// 동기 — 클라 1명 대기하는 동안 다른 클라 못 받음
TcpClient c = listener.AcceptTcpClient();

// 비동기 — 기다리는 동안 스레드 반납. 다른 일 가능
TcpClient c = await listener.AcceptTcpClientAsync();
```

### Task의 정체
| | 의미 |
|---|---|
| Thread | OS 단위. 만드는 비용 비쌈 (~1MB) |
| Task | "미래에 끝날 작업". ThreadPool의 스레드를 돌려가며 실행 |
| `async Task` 함수 | Task를 반환. 내부에서 `await` 가능 |
| `await someTask` | "끝날 때까지 함수 중단, 스레드 반납, 끝나면 이어서" |

### `_ =` 의 의미 (fire-and-forget)
- `async` 메서드는 항상 Task 반환 → 안 쓰면 컴파일러 경고 CS4014
- `_ =` = "이 Task 받긴 받는데 안 쓸 거야" (의도적 백그라운드 처리 표시)

```csharp
_ = session.RunAsync();         // ✅ 백그라운드로 던지기 (다음 줄로 즉시)
await session.RunAsync();       // ⚠️ 끝날 때까지 대기 (한 명씩만 처리됨)
```

### 외울 4가지
1. `AcceptTcpClientAsync()` 반환은 `Task<TcpClient>` → `await`로 풀어야 TcpClient 나옴
2. `async` 메서드는 항상 Task 반환 → 받든 버리든 처리
3. `_ = ...Async()` = fire-and-forget 관용구
4. `await` = 기다림 / `_ =` = 던지기

---

## 3. TcpListener / TcpClient / NetworkStream — 3층 구조

```
[1] TcpListener  ── 7777 포트 "문지기". Accept만 함
        ↓ AcceptTcpClientAsync 성공
[2] TcpClient    ── 클라 1명과의 "연결". 키 + Close 권한
        ↓ GetStream()
[3] NetworkStream── 실제 바이트 흐르는 "수도관". Read/Write
```

### 비유: 호텔
- TcpListener = 프론트 데스크 (체크인만)
- TcpClient = 객실 키
- NetworkStream = 객실 인터폰

### 짚어둘 것
- **`IPAddress.Any`** = 0.0.0.0 (모든 인터페이스), **`IPAddress.Loopback`** = 127.0.0.1 (같은 PC만)
- `client.GetStream()`을 여러 번 호출해도 같은 객체 반환 (캐시됨)
- `_client.Close()` 하면 NetworkStream도 같이 닫힘 (중복 Close 불필요)
- `client.Client.RemoteEndPoint` = 내부 Socket의 상대방 주소 (로그용)

### 자주 만나는 SocketException
| 코드 | 의미 |
|---|---|
| **10061 ConnectionRefused** | 서버가 그 포트에서 안 듣고 있음 |
| 10060 TimedOut | 응답 없음 (방화벽/네트워크 막힘) |
| 10049 AddressNotAvailable | 잘못된 IP |
| 10048 AddressAlreadyInUse | 서버 측 포트 이미 점유 중 |

→ 봇이 본 `10061`은 서버 측 listener가 안 떠 있어서 OS가 RST 응답한 것.

---

## 4. BinaryWriter / BinaryReader — 직렬화 도구

> 이 토픽은 아직 완전히 이해 안 됨. 추후 다시 복습 필요.

### 타입 → 바이트 변환 도구
- `BinaryWriter`: Stream에 타입 단위로 바이트 기록
- `BinaryReader`: Stream에서 바이트 읽어 타입으로 해석
- `MemoryStream` = 메모리 위의 가짜 Stream (byte[] 만들 때 사용)

### 타입별 바이트 길이
| 타입 | 바이트 |
|---|---|
| bool | 1 |
| ushort | 2 |
| int | 4 |
| float | 4 |

### Endian
- Little Endian: 낮은 자리 먼저 (`42` = `[0x2A, 0x00]`) — x86/x64, BinaryWriter 기본
- Big Endian: 높은 자리 먼저 — 네트워크 표준
- **우리는 Little Endian** (서버/유니티 둘 다 C#이라 변환 불필요)

### 문자열은 직접 만든 규칙
- BinaryWriter 기본 string은 가변 길이 prefix → 다른 언어 호환 어려움
- 우리: `ushort(2) 길이 + UTF-8 bytes`

### `using` 왜 붙이나?
- `IDisposable` 객체를 스코프 끝에서 자동 `Dispose()` 호출
- `BinaryWriter`는 내부 버퍼링 → Dispose가 Flush까지 보장 (안 그러면 잔여 바이트 누락 가능)

---

## 5. TCP Framing — 이번 주의 핵심 ⭐

### 문제
**TCP는 "메시지"가 아니라 "바이트 스트림".** 송신 측이 `WriteAsync` 1번 호출해도 받는 측은:
- 한 번에 다 받기 (합쳐짐)
- 절반만 받고 또 절반 (쪼개짐)
- 1바이트씩 N번

→ **받는 쪽이 직접 패킷 경계를 찾아야 함.**

### 해결: Length-Prefixed Framing
모든 패킷 맨 앞에 "내 크기" 적어두기.
```
[length=7][id=1][token=0][payload 7바이트]
                          ↑
                          payload = 헤더를 뺀 실제 내용 (예: 닉네임)
```

### 알고리즘 (`ClientSession.ParsePackets`)
```
[1] ReadAsync → 누적 버퍼 뒤에 이어붙임 (_writePos 전진)
[2] 가능한 만큼 패킷 잘라내기 (while 루프)
       a. _writePos - readPos < 6   → 헤더도 안 모임 → break
       b. length 읽기 (BitConverter.ToUInt16, peek만)
       c. < 6+length → payload 미완 → break
       d. 1패킷 잘라서 HandlePacket(dispatch)
       e. readPos += totalSize
[3] Buffer.BlockCopy로 소비한 부분 앞으로 당기기
다시 [1]로
```

### 핵심 변수 2개
- `_writePos`: 누적 버퍼에 "지금까지 쌓인 끝" 위치
- `readPos`: 이번 ParsePackets에서 "소비한" 위치
- `_writePos - readPos` = 아직 처리 안 한 바이트 수

### 외울 5가지
1. **TCP는 메시지 경계 없음** → 받는 쪽이 직접 자름
2. **누적 버퍼 + length prefix + while 루프** = 표준 패턴
3. **`while` 필수** (합쳐짐 케이스 위해, `if`로 짜면 두 번째 패킷이 잠김)
4. **`BitConverter.ToUInt16` = peek** (BinaryReader.ReadUInt16과 달리 위치 안 움직임)
5. **`read == 0` = 상대 끊음** (정상 종료. 안 처리하면 좀비 세션)

### 봇 검증 결과 (커밋 fe8f6a3)
| 케이스 | bytes 전송 | 서버 [Recv] |
|---|---|---|
| Test1 (정상 Alice) | 13 | 1줄 ✓ |
| Test2 (Bob+Charlie 합쳐짐) | 26 | **2줄** ✓ |
| Test3 (Dave 1바이트씩) | 12 | 1줄 ✓ |

---

## 6. Payload가 뭐냐

**Payload = 패킷의 "실제 내용물". 헤더(메타데이터)를 뺀 나머지.**

### 비유: 택배
- 운송장(헤더): 무게(length), 종류(packetId), 받는사람(sessionToken)
- 상자 내용물(payload): 실제 전달할 데이터

### 우리 패킷의 payload
| 패킷 | payload |
|---|---|
| C_Login | 닉네임 (ushort 길이 + UTF-8 바이트) |
| S_LoginResult | success(bool 1) + sessionToken(ushort 2) + reason(string) |

### 헤더와 payload는 우리가 직접 설계
→ `docs/protocol.md` 에 결정 사항 + 선택 근거 박제 완료.

---

## 7. ClientSession — 세션 객체화

### 함수 시절의 문제
한 클라 상태(buffer, writePos, NetworkStream, 닉네임, 토큰, HP...)가 함수 로컬 변수로 흩어짐. 외부에서 "그 클라한테 보내" 불가능.

### 객체로 묶으면
```csharp
public class ClientSession {
    private byte[] _buffer;
    private int _writePos;
    private NetworkStream _stream;
    public ushort SessionToken { get; set; }
    public string Nickname { get; set; }
    
    public async Task RunAsync() { ... }     // 수신 루프
    public async Task SendAsync(byte[] p) { ... }   // 송신
}
```
- 외부에서 `session.Nickname = "cuya"` 가능
- 매니저(MatchManager 등)가 `List<ClientSession>` 들고 다닐 수 있음

### SemaphoreSlim — 비동기 lock
- `lock { await ... }` 불가 (lock 안에서 await 못 씀)
- 비동기 메서드 안에서 mutex 같은 효과 = `SemaphoreSlim(1, 1)`
- 송신은 여러 곳에서 동시 호출 가능 → 직렬화 필수 (안 그러면 바이트 인터리브)
- 수신은 한 스레드(RunAsync)만 → 락 불필요

---

## 8. 데이터 vs 행동의 분리

### 왜 Shared에는 패킷 정의만 있고, HandlePacket은 GameServer에 있나?

| | 위치 | 누가 알아야 |
|---|---|---|
| 패킷 정의 (직렬화/필드) | **Shared** | 서버/봇/유니티 **모두** 같아야 |
| 패킷 받았을 때 서버 행동 | **GameServer** | 서버만 |
| 패킷 받았을 때 클라 행동 | **GameClient (Unity)** | 클라만 |

### 핵심 원칙
> **"데이터"는 공유, "행동"은 분리.**

만약 서버 처리 로직이 Shared에 있으면:
- 클라에 서버 코드 끌려 들어감 (보안 문제, 치트 만들기 쉬워짐)
- 컴파일 안 됨 (서버 의존성 필요)

### 비유: 편지
- 봉투 형식 = 국가 표준 (모두 따라야)
- 편지 받고 답장할지 버릴지 = 개인 행동 (각자)

---

## 9. 전체 흐름: "cuya" 닉네임 로그인 추적

```
[봇]
new C_Login { Nickname = "cuya" }
  ↓ Serialize() → 12바이트
  ↓   ├─ PacketIO.WriteString → payload [04 00][63 75 79 61]
  ↓   └─ PacketIO.WriteHeader → 헤더 [06 00][01 00][00 00]
  ↓
stream.WriteAsync(pkt) → TCP로 12바이트 전송

═══════════════ TCP ════════════════→

[서버]
listener.AcceptTcpClientAsync() → TcpClient 받음
  ↓
new ClientSession(client) → _stream = client.GetStream()
  ↓
_ = session.RunAsync()                    ← fire-and-forget
  ↓
stream.ReadAsync → 누적 버퍼에 12바이트
  ↓
ParsePackets()
  ├─ length=6, totalSize=12 확인
  └─ HandlePacket(buffer, 0, 12)
       ├─ PacketIO.ReadHeader → (len=6, id=1, token=0)
       │   (br 위치는 헤더 뒤로 전진)
       └─ switch (id=1) → case C_Login:
            └─ C_Login.Deserialize(br)
                 └─ PacketIO.ReadString(br) → "cuya"
       
       결과: pkt.Nickname == "cuya"
       콘솔 출력: [Session 127.0.0.1:xxxxx] C_Login nickname=cuya
```

→ 이 흐름이 **모든 패킷 처리의 기본 골격.** 앞으로 패킷 종류만 늘어남.

---

## 10. 핵심 개념 — GC / Dispose / using

| | GC | Dispose |
|---|---|---|
| 대상 | 관리되는 메모리 (C# 객체) | 관리되지 않는 자원 (파일/소켓/DB) |
| 시점 | 불확실 (런타임이 알아서) | 즉시 (호출 순간) |
| 호출 | 자동 | 명시적 (`using` 또는 직접) |

### 둘 다 필요한 이유
GC는 메모리는 정리하지만 **OS 자원(파일 핸들/소켓)은 모름.** 언제 정리될지 불확실 → 그 사이 다른 곳에서 같은 자원 못 씀.

→ `using`으로 즉시 Dispose = 확정적 시점에 자원 닫음.

비유: GC = 청소기, Dispose = 수도꼭지 잠그기. 수도꼭지를 청소기가 잠가주길 기다리면 안 됨.

### `using`은 C#만의 기능?
- GC: 많은 언어에 있음 (Java, Python, Go...)
- using + IDisposable: C# 특유의 깔끔한 패턴. 유사한 게:
  - Python: `with` 문
  - Java: try-with-resources (Java 7+)
  - Rust: Drop trait
  - C++: RAII

---

## 11. 새 패킷 추가 절차 (체크리스트)

목요일부터 자주 쓸 표준 작업:

1. ✅ `shared/Shared/PacketId.cs` — enum에 새 id 추가
2. ✅ `shared/Shared/Packets/{Name}.cs` — 패킷 클래스 (필드 + Serialize + Deserialize)
3. ✅ `server/GameServer/ClientSession.cs` — `HandlePacket` switch에 `case` 추가
4. ✅ `docs/protocol.md` 갱신

---

## 12. 오늘 커밋 이력

| 커밋 | 내용 |
|---|---|
| `fe8f6a3` | TCP length-prefixed 프레이밍 구현 및 봇 검증 시나리오 추가 |
| `a713111` | ClientSession 클래스 및 C_Login/S_LoginResult 패킷 추가 |
| `778dbf4` | WBS에 작업 진행 방식(등급별 절차/한 작업씩/셀프 퀴즈) 추가 |
| `1d01457` | protocol.md를 현재 코드 기준으로 갱신 및 선택 근거 추가 |

---

## 13. 다음 학습 시 우선순위

### 약한 부분 (다시 복습 필요)
- [ ] **토픽 4 BinaryWriter/Reader** — Endian, 타입별 바이트, MemoryStream 흐름이 흐릿함
- [ ] 토픽 5 셀프 퀴즈 답변 검증 (`while` vs `if`, BlockCopy 의미 등)

### 토픽 5 셀프 퀴즈 (답해보기)
1. `while (true)` 대신 `if`로 짜면 합쳐짐 케이스에서 두 번째 패킷은?
2. `_writePos - readPos < 6` 체크 빼면 버퍼 3바이트일 때 어떻게 되나?
3. `BitConverter.ToUInt16(buffer, 0)` 으로 바꾸면 합쳐짐 케이스 어떻게?
4. Test3 (1바이트 13번)에서 ParsePackets while 루프 몇 번 도나?
5. `Buffer.BlockCopy` 빼면 장기적으로?
6. `_buffer` 크기를 16으로 줄이면 어떤 케이스 깨지나?

---

## 14. 내일(목요일) 할 일 — 등급 🔴

- [ ] **로그인 처리**: C_Login 받으면 sessionToken 발급 + 닉네임 저장 + S_LoginResult 응답
- [ ] **MatchManager** (FIFO 자동 매칭 큐) 시작

→ 방식 A 진행: Claude는 개념/설계/함정만, 코드는 직접 작성 (30분 타임박스).

---

## 핵심 외울 키워드 (요약)

- **3 프로젝트**: server(Exe) / bot(Exe) / shared(Library, netstandard2.1)
- **`_ =`** = fire-and-forget, **`await`** = 기다림
- **소켓 3층**: Listener(문지기) → Client(키) → Stream(인터폰)
- **payload** = 헤더 뺀 실제 내용
- **TCP는 스트림**, 받는 쪽이 자름 = length-prefixed framing
- **`_writePos` / `readPos`** = 누적 버퍼의 끝 / 소비한 위치
- **GC ≠ Dispose**, using = IDisposable 자동 정리
- **데이터(Shared) ≠ 행동(GameServer)**
- **패킷 추가 = 3개 파일** (PacketId, 패킷 클래스, HandlePacket)
