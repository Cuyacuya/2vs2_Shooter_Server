# Packet Protocol v0

## Common Rules

| 항목 | 결정 | 선택 근거 |
|---|---|---|
| Endian | Little Endian | x86/x64 CPU + C# `BinaryWriter` 기본값. 서버(C#)와 클라(Unity C#) 둘 다 자동 일치 → 변환 코드 불필요 |
| 문자열 | UTF-8 + length-prefixed (`ushort` 길이 + bytes) | UTF-8 = 인터넷 표준, 모든 언어 호환. 길이 접두 = `\0` 종료자보다 빠르고 본문에 `\0` 들어가도 안전 |
| 프레이밍 (TCP) | length-prefixed (헤더 첫 필드가 payload 길이) | TCP는 메시지 경계가 없음 → 받는 쪽이 직접 자르려면 "내 크기" 정보가 맨 앞에 필요 |
| 프레이밍 (UDP) | 없음 (1 datagram = 1 패킷) | UDP는 datagram 경계 보존. length 필드는 검증용으로만 사용. |

---

## Transport — TCP vs UDP 분류 (3주차 도입)

### 분류 원칙

| 기준 | TCP | UDP |
|---|---|---|
| 신뢰성 필요? (한 번 누락하면 게임 깨짐) | ✅ | ❌ |
| 빈도 매우 높음? (초당 수십 회) | ❌ | ✅ |
| 순서 보장 필요? | ✅ | ❌ |
| 손실 시 다음 패킷으로 복구 가능? | — | ✅ |

### 우리 분류

**TCP (신뢰 + 이벤트)** — 한 번 보내고 한 번 정확히 도착해야 하는 것
- C_Login / S_LoginResult — 한 번만, 누락 시 게임 진입 불가
- S_MatchingStatus / S_GameStart — 게임 진행 이벤트
- **C_Fire / S_HitResult** — 데미지 이벤트, 누락 시 클라/서버 HP 불일치 (유령 데미지)
- S_RoundStart / S_RoundEnd / S_MatchEnd — 라운드/매치 진행 이벤트

**UDP (빈도 + 손실 허용)** — 매 50ms 흐르고 최신값만 의미 있는 것
- **C_Input** (TCP → UDP 이관, 3주차) — 20Hz, 한두 개 누락해도 다음 입력으로 복구
- **S_Snapshot** (TCP → UDP 이관, 3주차) — 20Hz, 가장 최신 1개만 의미. 오래된 건 버려도 OK
- **C_UdpHello** (신규) — UDP endpoint 핸드셰이크. 도착 안 하면 클라가 짧은 주기로 재시도

### HitResult가 UDP가 아닌 이유 (자주 헷갈리는 부분)

| 비교 | S_Snapshot | S_HitResult |
|---|---|---|
| 빈도 | **20Hz 항상** = 80 패킷/sec (4명) | 격렬 교전 시 ~10 hit/sec |
| 누락 시 복구 | 50ms 후 다음 Snapshot이 최신 위치 | **영원히 사라짐** (이벤트는 한 번뿐) |
| 누락 영향 | 한 프레임 끊김 (시각) | 클라 HP=100 / 서버 HP=75 불일치 (게임 로직 깨짐) |
| 비유 | CCTV 영상 | 알람 벨 |

HitResult는 빈도가 Snapshot의 1/8이라 TCP throughput에 부담 없음. 신뢰성이 압도적으로 더 중요.

### Snapshot 빈도 — 30Hz / 20Hz 의 위치

| 게임 | 시뮬 / 송신 | 비고 |
|---|---|---|
| **우리 프로젝트** | 30Hz / 20Hz | 학습용 |
| CS:GO 일반 | 64Hz / 64Hz | 표준 |
| CS:GO 128 tick | 128Hz / 128Hz | 경쟁용 |
| Valorant | 128Hz / 128Hz | 경쟁 |
| Overwatch 콘솔 | 60Hz | 표준 |
| Fortnite | 30Hz | 배틀로얄 (인원 많음) |
| WoW (MMO) | ~10Hz | 인원 매우 많음 |

30/20은 경쟁 FPS엔 부족하지만 학습 + 1주차 ~ 2주차 TCP "받자마자 처리" 한계 체감 → 정식 틱 루프 전환 학습 흐름에 적합.

---

## Header (6 bytes, 모든 패킷 공통)

| Field        | Type   | Size | Description                 |
|--------------|--------|------|-----------------------------|
| length       | ushort | 2    | payload 바이트 길이 (헤더 제외) |
| packetId     | ushort | 2    | 패킷 종류 (`PacketId` enum)   |
| sessionToken | ushort | 2    | 세션 식별자 (UDP에서 발신자 매칭) |

### 설계 근거

| 결정 | 근거 |
|---|---|
| length가 첫 필드 | 받는 쪽이 payload 크기 모르는 상태에서 자르려면 **맨 앞**에 있어야 함 |
| length는 ushort(최대 65535) | 우리 게임 패킷은 다 작음. `uint`(4바이트)는 낭비, `byte`(255)는 부족 |
| packetId 헤더에 포함 | 어떤 패킷인지 알아야 어떤 Deserialize 호출할지 결정. payload 안에 두면 늦음 |
| sessionToken 모든 패킷에 포함 | UDP는 비연결이라 발신자 정보 없음 (IP:포트만으로 식별 불완전, NAT/포트 변경 가능) → **매 UDP 패킷에 자기 신원 첨부 필수**. TCP는 연결 자체가 식별이라 0 사용 (형식 통일용) |
| 헤더 크기 6바이트 고정 | 가변이면 파싱 복잡. 작은 게임 패킷에선 6바이트도 충분히 작음 |

---

## PacketId Enum

  | Id | Name             | Direction     | Transport | 주차 |
  |----|------------------|---------------|-----------|------|
  | 1  | C_Login          | Client→Server | TCP       | 1주차 |
  | 2  | S_LoginResult    | Server→Client | TCP       | 1주차 |
  | 3  | S_MatchingStatus | Server→Client | TCP       | 1주차 |
  | 4  | S_GameStart      | Server→Client | TCP       | 1주차 |
  | 5  | C_Input          | Client→Server | **UDP**¹  | 2주차→3주차 |
  | 6  | S_Snapshot       | Server→Client | **UDP**¹  | 2주차→3주차 |
  | 7  | C_Fire           | Client→Server | TCP       | 2주차 |
  | 8  | S_HitResult      | Server→Client | TCP       | 2주차 |
  | 9  | C_UdpHello       | Client→Server | **UDP**   | 3주차 |
  | 10 | S_RoundStart     | Server→Client | TCP       | 3주차 |
  | 11 | S_RoundEnd       | Server→Client | TCP       | 3주차 |
  | 12 | S_MatchEnd       | Server→Client | TCP       | 3주차 |

  - `packetId = 0` 은 미사용/예약. 1부터 시작.
  - ¹ 2주차까지는 TCP 임시 운영, 3주차 화요일에 UDP로 이관.

---

## Packet Definitions

### C_Login (id=1)
클라 접속 직후 닉네임 전송.

| Field    | Type   | Notes              |
|----------|--------|--------------------|
| nickname | string | UTF-8, 2~16자 권장 |

**Payload 예 ("Alice"):**
```
[05 00]                       ← nickname 길이 = 5
[41 6C 69 63 65]              ← "Alice" UTF-8
총 7바이트
```

### S_MatchingStatus (id=3)
  큐 대기자 수가 바뀔 때 대기 중인 모두에게 브로드캐스트.

  | Field        | Type | Notes              |
  |--------------|------|--------------------|
  | currentCount | byte | 현재 대기자 수      |
  | maxCount     | byte | 매치 정원 (=4)      |

  **설계 근거**: 4명까지라 byte 충분. ushort 낭비.

  ### S_GameStart (id=4)
  4명 매칭 완료 시 각 클라에게 송신. 자기 팀 + 전체 플레이어 정보.

  | Field        | Type   | Notes                                   |
  |--------------|--------|-----------------------------------------|
  | myTeam       | byte   | 받는 사람의 팀 (0=Red, 1=Blue)            |
  | playerCount  | byte   | 참가자 수 (=4)                            |
  | players[]    | 반복   | playerCount 만큼 반복. 각 항목:           |
  | └ token      | ushort | 그 플레이어의 세션 토큰                    |
  | └ team       | byte   | 그 플레이어의 팀 (0=Red, 1=Blue)          |
  | └ nickname   | string | 그 플레이어의 닉네임                      |

  **설계 근거**:
  - `myTeam` 따로 둠: 모든 정보가 players[]에 있긴 하지만 자기 팀 빠르게 확인용
  - 팀 배정 규칙: 등록 순서 1,3 → Red(0) / 2,4 → Blue(1)

### S_LoginResult (id=2)
C_Login 처리 후 서버가 응답.

| Field        | Type   | Notes                                    |
|--------------|--------|------------------------------------------|
| success      | bool   | 1바이트 (1=성공, 0=실패)                  |
| sessionToken | ushort | 발급된 세션 id (실패 시 0)                |
| reason       | string | 실패 사유 (예: "MATCH_IN_PROGRESS"). 성공 시 "" |

**설계 근거:**
- `success`를 `bool(1)`로 (`byte` 대신): 의미 명확. 추가 코드(0=성공/1=실패) 외울 필요 없음
- `reason`을 항상 포함 (성공이어도): 성공 시 빈 문자열(2바이트) — 일관성. payload 길이 가변 처리 단순화

### C_Input (id=5)
매 프레임 클라가 자기 입력 상태 + 시점을 서버에 송신. 2주차는 "받자마자 처리", 3주차는 30Hz 틱 루프에서 일괄 처리.

| Field      | Type   | Size | Notes                                              |
|------------|--------|------|----------------------------------------------------|
| inputBits  | byte   | 1    | 비트마스크. bit0=W, bit1=S, bit2=A, bit3=D, bit4=Jump |
| yaw        | float  | 4    | 좌우 시점 (도, 0~360). 캡슐 자체 회전               |
| pitch      | float  | 4    | 상하 시점 (도, -85 ~ +85). 카메라만 회전            |

**Payload 총 9바이트. 헤더 포함 15바이트.**

**설계 근거**:
- **inputBits 1바이트 (벡터 X)**: 서버 권한 모델. 클라는 "키 눌렀음" 사실만 보고, 이동 방향/대각선 정규화/속도 곱은 서버가 책임. 치트 방지 + 3주차 이후 클라 예측·재플레이 도입 시 결정적 재현 가능.
- **yaw/pitch는 float**: 마우스 룩은 연속값이라 비트로 못 묶음. pitch 클램프(-85~+85)는 클라가 1차로, 서버도 검증해서 클램프.
- **Jump를 inputBits에 포함**: "지금 점프 키 눌렀음" 상태로 처리. 서버는 점프 비트 + `isGrounded` 둘 다 참이어야 점프 시작 (무한 점프 방지).
- **시퀀스 번호 없음**: 3주차 UDP 이관 시에도 추가 안 함. "가장 최신 입력만 의미" 모델이라 순서 어긋나면 늦게 도착한 건 버림. (시퀀스 추가는 4주차 이후 클라 예측 도입 시 검토)
- **Transport**: 2주차까지 TCP로 임시 운영. 3주차 화요일 UDP로 이관 (C_UdpHello 핸드셰이크 후). sessionToken 헤더 필드가 그때부터 진짜 역할 함.

### S_Snapshot (id=6)
서버가 4명 전원의 현재 상태를 모두에게 브로드캐스트. 2주차는 "C_Input 받자마자 즉시", 3주차는 20Hz 일괄 송신으로 교체.

| Field | Type | Size | Notes |
|---|---|---|---|
| playerCount | byte | 1 | 항상 4 (사망자도 포함) |
| **반복 × playerCount (한 명당 24B)** | | | |
| └ token | ushort | 2 | 그 플레이어의 sessionToken |
| └ posX | float | 4 | X 위치 (월드 좌표) |
| └ posY | float | 4 | Y 위치 (수직) |
| └ posZ | float | 4 | Z 위치 |
| └ yaw | float | 4 | 좌우 회전 (도, 0~360). 캐릭터 자체 회전 |
| └ pitch | float | 4 | 상하 시점 (도, -85~+85). 3인칭이라 다른 캐릭터 총구/머리 방향 표시용 |
| └ hp | byte | 1 | 0~100. 0이면 사망 |
| └ stateBits | byte | 1 | bit0=isDead, bit1~7=예약 |

**Payload 총 97B (count 1 + 4×24). 헤더 포함 103B.**

**설계 근거**:
- **항상 4명 고정 길이**: 사망해도 위치 계속 송신 (시체/사망 카메라 처리에 필요). 직렬화 단순.
- **pitch 포함 (3인칭)**: 다른 플레이어의 상하 시점이 화면에 보이므로 동기화 필요. 1인칭이었다면 제외 가능.
- **stateBits 1바이트 통째 예약**: 지금은 bit0(isDead)만 쓰지만, 4주차에 isReloading 등 추가 시 비트만 더 쓰면 됨. 패킷 구조 변경 없음.
- **hp byte (0~100)**: 최대 100이라 1바이트로 충분. 음수/감산 처리는 서버 권한.
- **velocityY 없음**: 2주차는 받자마자 처리 → 클라가 Lerp만 함. 점프 끊김은 의도된 TCP 한계 체감. 3주차 UDP 시 추가 검토.
- **20Hz × 4명 송신 시 대역폭**: 약 7.6KB/s (서버→각 클라). 무시할 수준.
- **Transport**: 2주차까지 TCP. 3주차 UDP로 이관. 패킷 누락 시 재전송 X (다음 50ms 후 새 Snapshot이 최신 상태 줌).

### C_Fire (id=7)
클라가 사격 버튼을 누른 사실만 서버에 통보. 페이로드 없음.

| Field | Type | Size | Notes |
|---|---|---|---|
| (없음) | — | 0 | 헤더만 전송 |

**Payload 0B. 헤더 6B만.**

**설계 근거**:
- **"쐈음" 사실만 전달**: 서버 권한 원칙. 서버는 그 sessionToken의 최신 pos+yaw+pitch를 자기가 들고 있으므로 ray 생성에 추가 정보 불필요.
- **치트 방지**: 클라가 ray 방향을 못 보냄 → "벽 너머 적도 맞추기" 같은 위조 불가.
- **시차 영향 적음 (2주차)**: 받자마자 처리라 직전 C_Input의 시점이 곧 사격 시점. TCP 순서 보장으로 yaw/pitch 갱신 후 사격이 같은 순서로 도착.
- **fireSeq/weaponId 미포함**: MVP는 무기 1종. 4주차 이후 예측·다중 무기 도입 시 그때 필드 추가.
- **payload 0**: 페이로드 길이 0인 패킷은 length 필드 = 0. 직렬화/역직렬화 코드는 헤더만 쓰면 끝.

### S_HitResult (id=8)
ray-sphere hitscan이 적중했을 때 4명 전원에게 브로드캐스트. miss는 보내지 않음.

| Field | Type | Size | Notes |
|---|---|---|---|
| attackerToken | ushort | 2 | 사격자의 sessionToken |
| victimToken   | ushort | 2 | 피격자의 sessionToken |
| damage        | byte   | 1 | 이번 사격으로 입힌 데미지 (현재 MVP는 고정값) |
| victimHpAfter | byte   | 1 | 적중 직후 피격자 HP (0이면 사망) |
| isKill        | byte   | 1 | 1=이 적중으로 사망, 0=생존. (victimHpAfter==0과 의미 중복이지만 클라 분기 단순화) |

**Payload 총 7B. 헤더 포함 13B.**

**설계 근거**:
- **4명 전원 브로드캐스트**: 1주차 S_GameStart/S_MatchingStatus와 일관. 4주차 킬피드 도입 시 추가 패킷 불필요.
- **hit만 송신, miss 미송신**: miss는 HP 변화 없음 → 클라가 알아야 할 정보 없음. 사격 사운드는 C_Fire 직후 클라가 자체 재생.
- **damage 포함**: MVP는 고정값이지만, 4주차 무기 다양화·헤드샷 추가 시 그대로 활용 가능. 1바이트라 비용 미미.
- **victimHpAfter 포함**: 클라가 S_Snapshot 다음 틱 기다리지 않고 즉시 HP UI 반영. 이펙트 타이밍 자연스러움.
- **isKill 명시**: victimHpAfter==0으로 추론 가능하지만, 명시 필드면 클라가 `if (isKill)` 한 줄로 사망 이펙트 분기. 1바이트라 부담 X.
- **hitPoint 미포함**: 2주차 시각화는 Line Renderer 0.1초 사격선뿐. 핏자국/파편 이펙트는 4주차 이후 폴리싱. 그때 hitX/Y/Z 12B 추가 가능.

### C_UdpHello (id=9, **UDP**)
클라가 S_GameStart 수신 직후 송신. 서버에 자기 UDP endpoint(IP:port)를 알려 sessionToken과 매핑하게 함.

| Field | Type | Size | Notes |
|---|---|---|---|
| (없음) | — | 0 | 헤더만. sessionToken은 헤더에 포함되어 있음 |

**Payload 0B. 헤더 6B만.**

**설계 근거**:
- **UDP는 비연결**: 서버가 클라의 UDP 발신 endpoint를 모르면 S_Snapshot UDP 송신 불가. C_Login은 TCP라 TCP 포트만 알지 UDP 포트는 모름.
- **별도 패킷으로 분리한 이유**: 첫 C_Input에 합쳐도 되지만 "이미 매핑됐나?" 매번 체크가 복잡. 명시적 핸드셰이크 1회로 분리하면 디버깅·테스트 쉬움.
- **재시도 권장**: 클라는 첫 C_UdpHello 송신 후 잠시 기다리고, 서버로부터 S_Snapshot이 50ms 내 도착 안 하면 재시도 (UDP라 누락 가능). 보통 1~2회로 성공.
- **payload 0**: sessionToken은 헤더에 있음. 본문 데이터 불필요.

### S_RoundStart (id=10)
라운드 시작 시 4명 전원에게 송신. 위치/HP 리셋된 시작 상태 알림.

| Field | Type | Size | Notes |
|---|---|---|---|
| roundIndex | byte | 1 | 라운드 번호 (1, 2, 3, ...). best-of-3이면 1~3 |
| redScore   | byte | 1 | 현재 Red 팀 라운드 승수 (시작 시 0) |
| blueScore  | byte | 1 | 현재 Blue 팀 라운드 승수 |

**Payload 3B. 헤더 포함 9B.**

**설계 근거**:
- **점수 같이 송신**: 라운드 시작 UI(스코어보드)에서 즉시 반영. 별도 S_ScoreUpdate 불필요.
- **스폰 위치/HP는 S_Snapshot이 담당**: S_RoundStart는 "이벤트 신호", 실제 상태는 다음 Snapshot이 가져옴. 책임 분리.
- **byte로 충분**: best-of-3 이내 진행 → 1바이트 넉넉.

### S_RoundEnd (id=11)
한 팀 전원 사망 시 4명 전원에게 송신.

| Field | Type | Size | Notes |
|---|---|---|---|
| winnerTeam | byte | 1 | 승리 팀 (0=Red, 1=Blue) |
| redScore   | byte | 1 | 갱신된 Red 점수 |
| blueScore  | byte | 1 | 갱신된 Blue 점수 |

**Payload 3B. 헤더 포함 9B.**

**설계 근거**:
- **승리 팀 + 누적 점수**: 클라가 점수 추적 따로 안 해도 됨. 서버가 진실의 단일 출처.
- **S_RoundStart 도 점수 포함**: 받자마자 동기화. 양쪽 다 있어 누락 방지.
- **다음 라운드 시작 타이밍**: 서버가 일정 시간(예: 3초) 후 S_RoundStart 송신. 별도 "타이머" 패킷 불필요.

### S_MatchEnd (id=12)
한 팀이 매치 승리 조건 달성 시 (best-of-3 = 2승 선취).

| Field | Type | Size | Notes |
|---|---|---|---|
| winnerTeam | byte | 1 | 승리 팀 (0=Red, 1=Blue) |
| redScore   | byte | 1 | 최종 Red 점수 |
| blueScore  | byte | 1 | 최종 Blue 점수 |

**Payload 3B. 헤더 포함 9B.**

**설계 근거**:
- **S_RoundEnd와 구조 동일**: 클라 처리 단순. 차이는 "이걸 받으면 메인 화면 복귀 분기".
- **별도 패킷으로 분리한 이유**: "라운드 끝" 과 "매치 끝"은 클라가 다르게 처리 (라운드 = 잠깐 결과 표시 후 재시작, 매치 = 메인 복귀). 같은 패킷에 플래그 두는 것보다 의도 명확.
- **TCP 보장 필수**: 매치 종료 누락 = 게임 영원히 안 끝남. 신뢰성 최우선.

---

## 새 패킷 추가 절차 (체크리스트)

1. `shared/Shared/PacketId.cs` — enum에 새 id 추가
2. `shared/Shared/Packets/{PacketName}.cs` — 패킷 클래스 (필드 + `Serialize` + `Deserialize`)
3. `server/GameServer/ClientSession.cs` (TCP) 또는 `server/GameServer/UdpServer.cs` (UDP, 3주차 신규) — 디스패치 추가
4. 이 문서에 PacketId 표 + Packet Definitions에 추가
5. Transport 결정 시 위의 "Transport — TCP vs UDP 분류" 원칙 참고
