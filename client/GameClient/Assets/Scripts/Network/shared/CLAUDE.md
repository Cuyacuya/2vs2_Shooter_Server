# shared/ — 패킷 정의 규칙

## 역할
서버와 클라이언트가 공통으로 사용하는 패킷 구조체, enum, 직렬화 로직을 관리한다.

## 패킷 헤더 구조 (6바이트)
| 필드 | 타입 | 설명 |
|---|---|---|
| length | uint16 | payload 바이트 길이 |
| packetId | uint16 | 패킷 종류 (PacketId enum) |
| sessionToken | uint16 | 세션 ID (UDP용) |

## PacketId 목록
| Id | 이름 | 방향 |
|---|---|---|
| 1 | C_Login | Client→Server |
| 2 | S_LoginResult | Server→Client |

## 직렬화 규칙
- BinaryWriter/BinaryReader 사용 (Little Endian 기본값)
- 문자열: UTF-8, 앞 2바이트에 길이 기록
- 새 패킷 추가 시 PacketId enum과 이 문서를 함께 업데이트
