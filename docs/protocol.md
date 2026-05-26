# Packet Protocol v0

## Common Rules

| 항목 | 결정 | 선택 근거 |
|---|---|---|
| Endian | Little Endian | x86/x64 CPU + C# `BinaryWriter` 기본값. 서버(C#)와 클라(Unity C#) 둘 다 자동 일치 → 변환 코드 불필요 |
| 문자열 | UTF-8 + length-prefixed (`ushort` 길이 + bytes) | UTF-8 = 인터넷 표준, 모든 언어 호환. 길이 접두 = `\0` 종료자보다 빠르고 본문에 `\0` 들어가도 안전 |
| 프레이밍 | length-prefixed (헤더 첫 필드가 payload 길이) | TCP는 메시지 경계가 없음 → 받는 쪽이 직접 자르려면 "내 크기" 정보가 맨 앞에 필요 |

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
| sessionToken 모든 패킷에 포함 | UDP는 비연결이라 발신자 정보 없음 → 매 패킷에 자기 신원 첨부. TCP는 사실 불필요하지만 형식 통일 (3주차에 UDP 도입 시 활용) |
| 헤더 크기 6바이트 고정 | 가변이면 파싱 복잡. 작은 게임 패킷에선 6바이트도 충분히 작음 |

---

## PacketId Enum

  | Id | Name             | Direction     | 주차 |
  |----|------------------|---------------|------|
  | 1  | C_Login          | Client→Server | 1주차 |
  | 2  | S_LoginResult    | Server→Client | 1주차 |
  | 3  | S_MatchingStatus | Server→Client | 1주차 |
  | 4  | S_GameStart      | Server→Client | 1주차 |
  | 5  | C_Input          | Client→Server | 2주차 |
  | 6  | S_Snapshot       | Server→Client | 2주차 |
  | 7  | C_Fire           | Client→Server | 2주차 |
  | 8  | S_HitResult      | Server→Client | 2주차 |

  - `packetId = 0` 은 미사용/예약. 1부터 시작.

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
- **시퀀스 번호 없음 (2주차)**: TCP라 순서 보장됨. 3주차 UDP 도입 시 seq 추가 예정.

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

---

## 새 패킷 추가 절차 (체크리스트)

1. `shared/Shared/PacketId.cs` — enum에 새 id 추가
2. `shared/Shared/Packets/{PacketName}.cs` — 패킷 클래스 (필드 + `Serialize` + `Deserialize`)
3. `server/GameServer/ClientSession.cs` — `HandlePacket` switch에 `case` 추가 (서버가 받을 패킷일 때만)
4. 이 문서에 PacketId 표 + Packet Definitions에 추가
