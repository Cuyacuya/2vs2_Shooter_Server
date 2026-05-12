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

## 4. BinaryWriter / BinaryReader — 직렬화 도구 (재정리)

> 처음엔 헷갈렸지만 "변환 도구 vs 저장 통" 비유로 정리됨.

### 핵심 한 줄
- **`MemoryStream` = 바이트가 실제로 쌓이는 "저장 통"**
- **`BinaryWriter` = 타입(int/ushort/string)을 바이트로 분해해서 Stream에 부어주는 "변환 도구"**
- 둘은 다른 일을 함. 그래서 분리됨.

### 비유: 정수기와 컵
```
[BinaryWriter]        [MemoryStream]
정수기                  컵
"물 따라"        →     "물이 담김"
```
- 정수기(BinaryWriter)는 **데이터를 컵에 부어주는 일만** 함. 자기는 아무것도 안 들고 있음.
- 컵(MemoryStream)이 **바이트를 실제로 보관**.
- 같은 정수기를 종이컵/머그컵/보온병 (MemoryStream/FileStream/NetworkStream) 어디든 꽂아 쓸 수 있음.

### 왜 분리됐나
같은 BinaryWriter 코드로 **여러 종류 Stream에 쓸 수 있게**:
- `BinaryWriter(MemoryStream)` → 메모리에 쌓아 byte[]로 추출 (우리 패턴)
- `BinaryWriter(FileStream)` → 파일에 저장
- `BinaryWriter(NetworkStream)` → TCP로 즉시 전송
- `BinaryWriter(GZipStream)` → 압축하면서 쓰기

→ 변환 로직(int → 4바이트)은 한 곳, 매체는 갈아끼움. **소프트웨어 디자인의 공통 패턴** (Java/Go/Python도 비슷).

### 전체 흐름
```
[사용자 코드]
    │ bw.Write(42)
    ▼
[BinaryWriter]
    │ "int 42 = [0x2A,0x00,0x00,0x00] 이지"
    │ ms에 1바이트씩 4번 push
    ▼
[MemoryStream 내부: 0x2A | 0x00 | 0x00 | 0x00]
    │ ms.ToArray()
    ▼
[byte[] 결과: [0x2A, 0x00, 0x00, 0x00]]
```

### 왜 통 2개? (msPayload + msFull)
패킷 직렬화 시 통이 2번 등장하는 이유:
- 헤더의 `length` 필드 = payload 크기를 적어야 함
- payload를 다 만들기 전엔 그 크기를 모름

→ 강제 순서:
1. msPayload에 payload 다 만듦
2. payload.Length 봄
3. msFull에 헤더(length 채움) + payload 붙임

통 1개로 짜려면 "헤더에 임시 0 쓰고 나중에 덮어쓰기" 같은 트릭 필요 → 복잡. 통 2개가 가장 단순.

### 타입별 바이트 길이
| 타입 | 바이트 | 비고 |
|---|---|---|
| bool | 1 | 0 또는 1 |
| byte / sbyte | 1 | |
| short / ushort | 2 | 헤더 필드 |
| int / uint | 4 | |
| long / ulong | 8 | |
| float | 4 | |
| double | 8 | |
| string | 가변 | BinaryWriter 기본은 7-bit length prefix (우리는 안 씀) |

### 우리 문자열 규칙 (PacketIO.WriteString)
- BinaryWriter 기본 string은 7-bit encoded 가변 길이 → 다른 언어 호환 어려움
- 우리 규칙: `ushort(2) 길이 + UTF-8 바이트` — 모든 언어에서 쉽게 파싱

### Endian
- **Little Endian**: 낮은 자리 먼저 (`5` = `[0x05, 0x00]`) — x86/x64, BinaryWriter 기본
- Big Endian: 높은 자리 먼저 — 네트워크 표준
- **우리는 Little Endian** (서버/유니티 둘 다 C#이라 변환 불필요)

### `using` 왜 붙이나?
- `IDisposable` 객체를 스코프 끝에서 자동 `Dispose()` 호출
- BinaryWriter는 내부 버퍼링 → Dispose가 Flush까지 보장 (안 그러면 잔여 바이트 누락 가능)
- IDisposable 객체엔 거의 항상 `using` 붙이는 게 C# 컨벤션

### 외울 5가지
1. **MemoryStream = 저장 통**, **BinaryWriter = 변환 도구**. 분리되어 있음.
2. **BinaryWriter는 자기는 아무것도 안 들고 있음**. 결과는 항상 Stream에 쌓임.
3. **`ms.ToArray()` = 통 안 전체 바이트 복사해서 byte[] 반환**
4. **모든 데이터(닉네임/채팅/좌표 등)가 같은 패턴**. 패킷 종류만 다를 뿐.
5. **읽기는 거울 대칭**. 쓴 순서 = 읽는 순서. 한 줄 어긋나면 깨짐.

### 보충 Q&A (오늘 추가)

**Q. 바이트를 16진수로 표현?**
- 1바이트 = 8비트 = 16진수 정확히 2자리 (0x00 ~ 0xFF = 0 ~ 255)
- 16진수는 자리수 고정이라 byte[] 출력 시 깔끔하게 정렬됨
- `BitConverter.ToString(bytes)` = .NET이 `XX-XX-XX...` 형식으로 16진수 출력
- 같은 값이라도 코드에선 `42` 또는 `0x2A`로 쓸 수 있음 (동일)

**Q. PacketIO.WriteHeader/WriteString이 ms/bw와 상관없다?**
- **상관 있음.** PacketIO 함수는 첫 인자로 `BinaryWriter bw`를 받음.
- 그 bw로 `.Write(...)`를 호출 → ms에 결국 다 쌓임.
- PacketIO는 **반복되는 `bw.Write(...)` 호출을 묶어둔 함수**일 뿐.
- 비유: ms=컵, bw=정수기, **PacketIO=정수기 사용법 매뉴얼** ("이 순서로 따라주세요")

**Q. ms/bw 분리가 C# 권장 표준?**
- ✅ .NET 공식 패턴. BinaryWriter 생성자가 Stream을 강제로 요구.
- 거의 모든 언어에 같은 분리: Java(DataOutputStream + ByteArrayOutputStream), Go(binary.Write + bytes.Buffer), Python(struct.pack + io.BytesIO), C++(std::ostream + stringstream)
- "변환 도구 + 저장 매체" 분리는 소프트웨어 디자인의 공통 패턴 (일종의 Decorator/Pipeline 패턴)

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
- [x] ~~토픽 4 BinaryWriter/Reader~~ — "정수기-컵" 비유로 정리됨 (위 섹션 4 재정리 참고)
- [ ] 토픽 5 셀프 퀴즈 답변 검증 (`while` vs `if`, BlockCopy 의미 등)
- [ ] 토픽 6 (패킷 클래스 Serialize/Deserialize 비대칭) 아직 미진행
- [ ] 토픽 7~8 (ClientSession 객체화 / SemaphoreSlim / switch 디스패치) 아직 미진행

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
- **MemoryStream = 저장 통**, **BinaryWriter = 변환 도구 (정수기)** — 분리되어 있음
- **PacketIO = bw.Write 묶음 함수** (사용법 매뉴얼). 결국 ms에 다 쌓임
- **1바이트 = 16진수 2자리** (0x00 ~ 0xFF)
- **모든 데이터(닉네임/채팅/좌표)는 같은 직렬화 패턴**. 패킷 종류만 다름
