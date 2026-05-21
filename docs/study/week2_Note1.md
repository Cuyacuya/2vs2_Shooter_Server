# Week2 Note 1 — 인게임 패킷 설계 + 입력 처리 + 설정 파일 분리 (월~화)

> 작성일: 2026-05-21
> 범위: 2주차 월요일 (패킷 설계 + Shared 구현) ~ 화요일 (PlayerState + C_Input 핸들러 + balance.json 인프라)
> 학습 등급: 🟡 (보조) 중심. C_Input 핸들러는 🔴 → 🟡 강등.
> 진행 방식: 토론 → 의사코드 → 본인 작성 시도 → diff 비교 → 빌드 검증

---

## 0. 진행 요약

| 작업 | 등급 | 결과물 |
|---|---|---|
| A. 인게임 패킷 4종 설계 (C_Input/S_Snapshot/C_Fire/S_HitResult) | 🟡 | `docs/protocol.md` 갱신 |
| B. WBS / 기획서 시점 정합성 (1인칭 → 3인칭) | 🟢 | WBS×2 + FPS_Server v1.3 일관성 |
| C. Shared 패킷 클래스 4종 구현 | 🟡 | `Packets/C_Input.cs`, `S_Snapshot.cs`, `C_Fire.cs`, `S_HitResult.cs` |
| D. 인프라 정비 | 🟢 | global.json rollForward, Shared LangVersion=latest |
| E. PlayerState 클래스 | 🟡 | `server/GameServer/PlayerState.cs` |
| F. WBS / 기획서 설정 파일 전략 반영 | 🟢 | balance.json 도입 명시 |
| G. balance.json 인프라 | 🟡 | `config/balance.json` + `Shared/BalanceConfig.cs` + `BalanceLoader.cs` |
| H. C_Input 핸들러 (WASD + yaw 회전 + 정규화 + JSON 적용) | 🔴 → 🟡 | `ClientSession.HandleInput` |

**커밋**: `feature/HD-week2` 브랜치 운영. 월요일 작업까지 2개 커밋 정리 완료.

---

## 1. 패킷 4종 설계 — 결정 근거

### C_Input — 키 상태 비트마스크 (A) vs 이동 벡터 (B)

| 방식 | 클라가 보내는 것 | 평가 |
|---|---|---|
| **A. inputBits 1바이트** ⭐ 선택 | 비트 0=W, 1=S, 2=A, 3=D, 4=Jump + yaw/pitch | 서버 권한 강화, 치트 방지, 클라 예측 시 결정적 재현 가능 |
| B. moveX/moveZ float | 정규화된 벡터 + jump bool + yaw/pitch | 클라 권한 일부 가져감, float 오차로 예측 어긋남 |

**채택 이유**: Source/Quake/Overwatch 모두 키 상태 방식. WBS의 "Server Authoritative" 학습 목표와 일치. 3주차 이후 클라 예측 도입 시 같은 입력으로 양쪽 결정적 재현 가능.

### S_Snapshot — 한 명당 24바이트 × 4명

| Field | Type | 비고 |
|---|---|---|
| token / pos×3 / yaw / pitch / hp / stateBits | ushort + float×5 + byte×2 | 24B |

- **3인칭 확정** (클라가 따라가는 영상이 3인칭) → `pitch` 포함 (다른 캐릭터의 상하 시점 화면에 보임)
- **velocityY 제외** — 2주차는 받자마자 처리, 점프 끊김은 의도된 TCP 한계 체감
- 4명 고정 길이로 단순화 (사망자 포함 송신)

### C_Fire — payload 0바이트

**필드 없음**. 헤더 6바이트만:
- `packetId = 7` → "사격 행동"
- `sessionToken` → "누가 쐈는지"
- 어디로/누구에게? → **서버가 들고 있는 그 플레이어의 pos/yaw/pitch로 ray 생성**

**서버 권한 원칙의 정수**: 클라는 "쐈음 사실만", 판정·방향 전부 서버. 치트 100% 차단.

### S_HitResult — 5필드 7바이트, hit만 송신

| Field | Type | Size |
|---|---|---|
| attackerToken / victimToken | ushort × 2 | 4B |
| damage / victimHpAfter / isKill | byte × 3 | 3B |

- **hit만 송신, miss 무시** — miss는 HP 변화 없음, 사격 사운드는 클라가 C_Fire 직후 자체 재생
- **isKill 명시 필드** — `victimHpAfter == 0` 으로 추론 가능하지만 클라 분기 단순화 (`if (isKill)` 한 줄)
- **4명 전원 브로드캐스트** — 4주차 킬피드 도입 시 추가 패킷 불필요

---

## 2. 시점 변경 (1인칭 → 3인칭)

### 원인 발견
클라 친구가 따라가는 영상이 "FPS Prototype #01 게임 월드 생성과 카메라 회전" — **3인칭 카메라 셋업**. 양쪽 WBS와 기획서는 "1인칭" 으로 적혀있어 어긋남.

### 영향
| 항목 | 영향 |
|---|---|
| **S_Snapshot의 pitch** | 1인칭이면 빼도 됐는데 3인칭이라 **포함** 필요 (다른 캐릭터 상하 시점 화면에 보임) |
| **C_Input의 pitch** | 무관 (서버가 ray 방향 계산용으로 어차피 필요) |
| **C_Fire / hitscan** | 무관 (서버 권한이라 1/3인칭 표현과 별개) |
| **클라 카메라 부착** | 캡슐 머리 위 → 캡슐 뒤쪽 + 살짝 위 |

### 학습 포인트
- **상위/하위 문서 정합성** 의 중요성: 영상 한 마디가 WBS·기획서·protocol.md 패킷 구조 결정까지 거꾸로 영향
- WBS 두 파일 + FPS_Server v1.3 모두 "1인칭" → "3인칭" 일괄 swap, 카메라 위치 묘사도 같이 수정

---

## 3. 직렬화 패턴 학습 (Shared 패킷 클래스)

### 두 스트림 패턴 — payload 있을 때

```csharp
using var msPayload = new MemoryStream();
using var bwPayload = new BinaryWriter(msPayload);
bwPayload.Write(InputBits);     // 1B
bwPayload.Write(Yaw);            // 4B
bwPayload.Write(Pitch);          // 4B
byte[] payload = msPayload.ToArray();   // 9B

using var msFull = new MemoryStream();
using var bwFull = new BinaryWriter(msFull);
PacketIO.WriteHeader(bwFull, (ushort)PacketId.C_Input, sessionToken, (ushort)payload.Length);
bwFull.Write(payload);
return msFull.ToArray();
```

**왜 두 스트림?** 헤더의 `length` 필드에 payload 길이를 적어야 하는데, **그 길이를 먼저 알아야** 함 → payload 따로 만들어 길이 잰 다음 헤더 + payload 통합.

**예외**: C_Fire (payload 0) 는 msFull 하나만. `WriteHeader(..., payloadLength: 0)` 한 줄로 끝.

### MemoryStream vs BinaryWriter

| | MemoryStream | BinaryWriter |
|---|---|---|
| 정체 | **byte[] 컨테이너** (자라는 버퍼 + 위치 포인터) | **타입 변환기** (어댑터) |
| 저장 | ✅ | ❌ — 자체 저장 없음, ms로 직접 씀 |
| 메서드 | `Write(byte[])`, `ToArray()` | `Write(int)`, `Write(float)`, `Write(string)` 등 타입별 |

**비유**: MemoryStream = 책상, BinaryWriter = 비서. 비서가 자기 책상이 없어서 생성 시 어느 책상에 쓸지 지정 (`new BinaryWriter(ms)`).

### 데이터 흐름 (잘못된 멘탈 모델 정정)

| 잘못된 이해 | 정확한 이해 |
|---|---|
| "bw가 데이터 들고 있다가 ms로 옮긴다" | bw는 자체 저장소 없음, 호출되는 즉시 변환→ms에 씀 |
| "ms를 bw가 변환한다" | "객체 프로퍼티 → bw 변환 → ms에 직접 쓰기" |
| "두 ms는 임시 보관" | "헤더의 length 채우려면 payload 길이를 먼저 알아야" |

### 반복 직렬화 — S_Snapshot

```csharp
bwPayload.Write((byte)Players.Count);   // count 1B
foreach (var p in Players) { /* 24B 필드 8개 */ }
```

**가변 길이 표준 패턴**: `count + item × count`. 받는 쪽은 count 먼저 읽고 그만큼 for 반복.

**함정**: `Players.Count` 는 int(4B)라 **`(byte)` 캐스팅 필수**. 안 하면 `Write(int)` 호출돼 4바이트 써져 받는 쪽 즉시 깨짐.

### 타입 ↔ Read 메서드 매핑

| C# 타입 | Write | Read |
|---|---|---|
| `byte` | `Write(byte)` | `ReadByte()` |
| `ushort` (UInt16) | `Write(ushort)` | `ReadUInt16()` ← `ReadUShort()` 아님 |
| `float` (Single) | `Write(float)` | `ReadSingle()` ← `ReadFloat()` 아님 |
| `string` (utf8 len-prefixed) | `PacketIO.WriteString` | `PacketIO.ReadString` |

C# 키워드(`ushort`, `float`) 와 .NET 정식 이름(`UInt16`, `Single`) 이 다른 게 함정.

---

## 4. 디스패처 (Dispatcher) 개념 정리

**정의**: 받은 패킷을 packetId 보고 분류해서 알맞은 핸들러에 분배하는 코드.

### 우리 코드 위치
`server/GameServer/ClientSession.cs` 의 `HandlePacket` 메서드 — `switch (packetId)` 블록.

```csharp
switch ((PacketId)id)
{
    case PacketId.C_Login:
    {
        var pkt = C_Login.Deserialize(br);
        await HandleLogin(pkt);
        break;
    }
    case PacketId.C_Input:
    {
        var pkt = C_Input.Deserialize(br);
        HandleInput(pkt);                  // 동기 메서드라 await 없음
        break;
    }
}
```

### 디스패처 4단계
1. **헤더 먼저 읽기** (length, packetId, sessionToken)
2. **packetId로 분기** (switch)
3. **종류별 Deserialize 호출**
4. **핸들러 호출** (`HandleXxx`)

### 책임 분리
- **디스패처** = 분류·라우팅 (1개)
- **핸들러** = 비즈니스 로직 (패킷마다 1개)

**새 패킷 추가 = case 한 줄 + 핸들러 메서드 하나**.

---

## 5. PlayerState 클래스 — 동시성 패턴

### 구조
```csharp
public class PlayerState
{
    // 식별
    public ushort SessionToken;
    public byte Team;

    // 위치
    public float PosX, PosY, PosZ;

    // 시점
    public float Yaw, Pitch;

    // 점프/중력
    public float VelocityY;
    public bool IsGrounded = true;

    // 전투
    public byte Hp = 100;
    public bool IsDead;

    // 받자마자 처리 모델용 dt 측정
    public long LastInputTicks;

    // 동시성 보호. 외부 코드는 lock(player.Lock) 으로 감싸야 함.
    public readonly object Lock = new();
}
```

### 핵심 결정
- **외부 lock 객체 (`public readonly object Lock`)**: PlayerState 자신을 lock(`lock(player)`) 하지 않는 이유 — 외부 코드가 의도치 않게 같은 객체로 lock 잡을 위험 (데드락). 전용 lock 객체로 격리.
- **`readonly`**: Lock 객체 자체 교체 금지 (교체되면 두 코드가 다른 lock 잡아 동시 진입 가능).
- **HP=100, IsGrounded=true 기본값**: 라운드 시작 직후 안전 상태로 초기화.
- **Vector3 미사용**: 학습 + 의존성 최소화 + 디버깅 가독성 위해 float×3 분리.

---

## 6. C_Input 핸들러 — 입력 처리의 전 과정

### 전체 흐름
```
[클라 W 누름]
     ↓ C_Input(InputBits=0b00001, Yaw, Pitch) 송신
[서버 HandlePacket switch case PacketId.C_Input]
     ↓
[HandleInput(pkt)]
     ↓ lock (Player.Lock)
[1. dt 계산] → [2. 비트마스크 풀기] → [3. 정규화] → [4. 시점 갱신] → [5. yaw 회전] → [6. 위치 갱신]
```

### Step 1: dt 계산 (받자마자 처리 모델)

```csharp
long nowTicks = Stopwatch.GetTimestamp();
float dt;
if (Player.LastInputTicks == 0)
    dt = 0f;                                  // 첫 입력은 이동 0
else
{
    dt = (float)((nowTicks - Player.LastInputTicks) / (double)Stopwatch.Frequency);
    if (dt > 0.1f) dt = 0.1f;                 // 네트워크 끊김 방어 (텔레포트 방지)
}
Player.LastInputTicks = nowTicks;
```

**핵심**:
- `Stopwatch.GetTimestamp()` = 고정밀 tick (`DateTime.UtcNow.Ticks` 보다 안전. OS 시각 변경 영향 X)
- `LastInputTicks == 0` = "첫 입력" 판정용 (PlayerState 기본값 0)
- 1초 끊겼다 재개되면 1초 분량 이동 = 텔레포트 → **최대 dt 0.1초로 cap**

### Step 2: 비트마스크 풀기

```csharp
bool w = (pkt.InputBits & (1 << 0)) != 0;
bool s = (pkt.InputBits & (1 << 1)) != 0;
bool a = (pkt.InputBits & (1 << 2)) != 0;
bool d = (pkt.InputBits & (1 << 3)) != 0;
// bit4(Jump)는 수요일 작업
```

`1 << N` = 비트 N번에 1. AND 연산 결과가 0이 아니면 그 비트 켜져있음.

### Step 3: 이동 벡터 + 대각선 정규화

```csharp
float moveX = (d ? 1f : 0f) + (a ? -1f : 0f);
float moveZ = (w ? 1f : 0f) + (s ? -1f : 0f);

float len = MathF.Sqrt(moveX * moveX + moveZ * moveZ);
if (len > 0f)
{
    moveX /= len;
    moveZ /= len;
}
```

**왜 정규화?** W+D 동시 누름 = (1, 1) → 길이 √2. 정규화 없으면 대각선이 √2배 빨라짐. 길이 1로 만들기 → (0.707, 0.707).

### Step 4: 시점 갱신 + 서버측 클램프 검증

```csharp
Player.Yaw = pkt.Yaw;                                              // 그대로 대입
Player.Pitch = Math.Clamp(pkt.Pitch, pitchMin, pitchMax);          // 서버 한 번 더 검증
```

**서버측 클램프 이유**: 클라가 1차 클램프 보내지만 치트/버그 대비 한 번 더. -85~+85 밖이면 자동 자름.

### Step 5: yaw 회전 적용 (서버 권한의 핵심 계산)

```csharp
float yawRad = Player.Yaw * DEG2RAD;
float cosY = MathF.Cos(yawRad);
float sinY = MathF.Sin(yawRad);
float worldX =  moveX * cosY + moveZ * sinY;
float worldZ = -moveX * sinY + moveZ * cosY;
```

**왜 회전 적용?** 캐릭터가 90도 돌아있는데 W 누르면 월드 +Z가 아니라 월드 +X(오른쪽)로 가야 함. yaw 회전 없으면 캐릭터 방향 무시하고 항상 월드 축으로만 이동 = 비정상.

**회전 행렬** = 캐릭터 로컬 좌표(moveX, moveZ) → 월드 좌표(worldX, worldZ) 변환.

### Step 6: 위치 갱신

```csharp
Player.PosX += worldX * speed * dt;
Player.PosZ += worldZ * speed * dt;
// PosY는 점프/중력 (수요일 작업)
```

벡터 × 속도 × 시간 = 변위.

### 함정 학습

| 함정 | 결과 | 대응 |
|---|---|---|
| `await HandleInput(pkt)` | void는 await 불가 (CS4008) | `private void HandleInput` 이라 `await` 제거 |
| `const float DEG2RAD = Math.PI / 180f` | double → float 변환 에러 (CS0266) | `MathF.PI` 사용 (float 전용) |
| lock 빼먹기 | 4명 접속 시 random crash | PlayerState 접근 전체를 `lock(Player.Lock)` 안에 |
| 대각선 정규화 누락 | W+D 동시 시 √2 배 빨라짐 | length로 나누기 |
| yaw 회전 적용 누락 | 캐릭터 방향 무시하고 월드 축 이동 | sin/cos 적용 |
| Pitch 클램프 누락 | 클라 +1000 보내면 화면 뒤집힘 | `Math.Clamp(pitch, min, max)` |
| 첫 dt가 거대 (LastInputTicks=0과 큰 차이) | 첫 입력 시 텔레포트 | `if (LastInputTicks == 0) dt = 0;` |

---

## 7. 게임 서버 아키텍처 학습 — Server-Authoritative

### 두 모델 비교

| 모델 | 장점 | 단점 |
|---|---|---|
| **Server Authoritative** ⭐ 채택 | 치트 거의 불가능, 4인 동기화 정확 | 입력 → 화면 반영 지연 (RTT만큼) |
| Client Authoritative | 즉각 반응 | 클라가 거짓말하면 끝 (벽통과/텔레포트/무한HP) |

### 실제 산업 선택

| 장르 | 모델 |
|---|---|
| 경쟁 FPS (CS:GO, Valorant, Overwatch) | Server Auth + Lag Compensation |
| MMORPG (WoW, FFXIV) | Server Auth + Client Prediction |
| 모바일 캐주얼 | Client Auth 종종 |

### 실무에서 끔찍하지 않게 만드는 방법 — Client-Side Prediction

1. 클라 W 누름 → **패킷 송신 + 자기 화면에서 즉시 이동 (예측)**
2. 서버가 정답 위치 송신
3. 클라: 예측 vs 정답 비교 → 일치면 OK, 불일치면 부드럽게 보정 (reconciliation)

**우리 C_Input이 키 상태(비트마스크) 방식인 이유**: 같은 입력으로 양쪽이 결정적 재현 가능. moveX/moveZ float 벡터면 부동소수 오차로 어긋남.

### 우리 프로젝트 진화

| 주차 | 클라 처리 |
|---|---|
| 2주차 | 서버 응답만 기다림 (의도적 끔찍 체감) |
| 3주차 | UDP + 정식 틱 + 위치 보간 (Lerp) |
| 4주차~ | 예측은 학기 범위 밖 |
| 방학 / C++ 리메이크 | 진짜 예측 + reconciliation 도입 |

---

## 8. 설정 파일 분리 — balance.json (CS:GO 패턴)

### 동기
HandleInput에 `const float SPEED = 5.0f;` 박아두는 게 안티패턴인 이유:
1. 밸런스 패치마다 빌드/배포
2. A/B 테스트 불가
3. 클라/서버 값 어긋남 → 캐릭터 떨림
4. 디자이너가 못 만짐

### 업계 패턴 (단계적 진화)
1. 상수 클래스 (`GameConstants`)
2. **설정 파일** (JSON/YAML) ⭐ 우리 선택
3. 핫 리로드
4. 원격 설정 (LiveOps) — Firebase Remote Config 등
5. 데이터 시트 (Excel/Google Sheets) — 원신·우마무스메

### 우리 채택: CS:GO 의 `weapons.txt` 단순 버전

- `config/balance.json` — 양쪽이 같은 파일 읽음
- `shared/Shared/BalanceConfig.cs` (POCO) — 서버·클라 동일 클래스로 deserialize
- `server/GameServer/BalanceLoader.cs` — JSON 로딩 helper (System.Text.Json은 netstandard2.1 기본에 없어서 서버 측 분리)
- `Balance.Current` 정적 핸들 — 어디서든 `Balance.Current.Player.MoveSpeed` 접근

### 무엇을 JSON, 무엇을 코드?

| 가르는 기준 | 위치 |
|---|---|
| 디자이너가 만질 수 있나 (밸런스) | **JSON** |
| 안 바뀜 확정 (수학·프로토콜) | **코드 const** |
| 성능 크리티컬 | **코드 const** |

**JSON 행**: moveSpeed, initialHp, jumpVelocity, pitchMinDeg, pitchMaxDeg, gravity, groundY, damage, hitscanSphereRadius
**코드 const 행**: `1 << 0~4` (비트마스크), `MathF.PI / 180f` (DEG2RAD), `if (dt > 0.1f) dt = 0.1f` (네트워크 방어)

### 적용 효과

`config/balance.json` 의 `"moveSpeed": 5.0` → `8.0` 변경 + 서버 재시작 = 코드 변경 0줄로 게임 밸런스 변경.

### 경로 안전성

원래 `Path.Combine(BaseDir, "../../../../config/balance.json")` 으로 시도 → **1단계 어긋남** (실제는 5단계 위가 레포 루트, 4단계는 server/).

수정: `Game.sln` 마커 파일 탐색으로 변경:
```csharp
string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("...");
}
```

**원리**: git, npm, cargo 가 `.git`, `package.json`, `Cargo.toml` 마커로 루트 찾는 패턴과 동일. 빌드 경로 깊이 변동에 안전.

---

## 9. 인프라 변경 정리

| 변경 | 파일 | 이유 |
|---|---|---|
| `rollForward: latestFeature` | `global.json` | 팀원 SDK 패치 버전 차이 (8.0.100 vs 8.0.420 등) 허용. 같은 .NET 8 LTS 내 자동 일치 |
| `<LangVersion>latest</LangVersion>` | `shared/Shared/Shared.csproj` | netstandard2.1 기본 C# 8.0 → C# 12 신문법 (target-typed `new()` 등) 사용 가능 |

---

## 10. AI 활용 워크플로 변화 (메타-학습)

### 5단 포맷 도입
원래 4단 (무엇/핵심/완성코드/라인별해설) → 직접 피드백 후 **5단** 으로 갱신:

1. **우리가 무엇을 하는건가**
2. **핵심**
3. **의사코드 / 단계별 가이드** ← 추가
4. **완성된 코드**
5. **라인별 해설**

3단계 의사코드 보고 **본인이 먼저 직접 작성 시도** → 막히면 4단계 완성 코드 확인. WBS의 🟡 (보조) 표준 워크플로 (완성 코드 → 가리기 → 재작성) 보다 능동적 학습 효과 ↑.

### AI 활용 등급 운용
- 🔴 (직접): 개념·다이어그램·함정만, 코드 X — C_Input 핸들러 초기
- 🟡 (보조): 5단 포맷, 본인 시도 후 답 확인 — 대부분 작업
- 🟢 (적극): 보일러플레이트 완성 코드 — balance.json 인프라 등

상황에 따라 🔴 → 🟡 다운그레이드도 자유롭게 (학습 효과 vs 진도 트레이드오프).

### 트레이드오프 비교 패턴
중요 결정 시 AI가 A/B/C 옵션 제시 → **결정은 본인이**:
- 패킷 설계 (키 상태 vs 벡터)
- 설정 분리 시점 (단계 도입 vs 즉시 JSON)
- 시점 변경 경로 (3인칭 확정 vs 1인칭 복귀 vs 서버 중립)

---

## 11. 검증 시나리오 (수요일 작업 전)

- [x] Shared 빌드 통과 (0 경고, 0 오류)
- [x] GameServer 빌드 통과
- [x] BotClient 빌드 통과
- [x] 전체 솔루션 빌드 통과
- [ ] 서버 실행 시 balance.json 로드 로그 출력 — `[Balance] loaded: MoveSpeed=5, Damage=25, Gravity=-9.8`
- [ ] 봇 4개 자동 매칭 → S_GameStart 수신 (1주차 흐름 회귀 검증)
- [ ] 5번째 봇 거부 확인
- [ ] (Level 2 - 봇 확장 후) C_Input 송신 시 PosZ 증가 로그 확인

---

## 12. 다음 단계 — 수요일

| 작업 | 등급 | 시간 |
|---|---|---|
| 점프 + 중력 시뮬레이션 (`velocity.y -= gravity*dt`, 지면 충돌, isGrounded) | 🔴 | 2h |
| 마우스 룩 처리 + 시점 회전 동기화 | 🟡 | 1h |

**위험 시점**: 점프 중력 시뮬 안 되면 콘솔 단위 테스트로 분리 검증 (가속도/지면 충돌만).

---

## 13. 한 줄 요약

> "패킷은 wire format만, 핸들러는 비즈니스 로직. 서버는 단일 권한, 데이터는 코드 밖. 학습은 의사코드 보고 직접 시도부터."
