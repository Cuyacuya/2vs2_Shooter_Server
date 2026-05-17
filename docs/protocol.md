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

---

## 새 패킷 추가 절차 (체크리스트)

1. `shared/Shared/PacketId.cs` — enum에 새 id 추가
2. `shared/Shared/Packets/{PacketName}.cs` — 패킷 클래스 (필드 + `Serialize` + `Deserialize`)
3. `server/GameServer/ClientSession.cs` — `HandlePacket` switch에 `case` 추가 (서버가 받을 패킷일 때만)
4. 이 문서에 PacketId 표 + Packet Definitions에 추가
