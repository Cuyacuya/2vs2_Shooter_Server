# Packet Protocol v0

## Common
- Endian: Little Endian
- String: UTF-8, length-prefixed (uint16 length + bytes)

## Header (6 bytes)
| Field        | Size   | Description         |
|--------------|--------|---------------------|
| length       | uint16 | payload byte length |
| packetId     | uint16 | packet type (enum)  |
| sessionToken | uint16 | session id (UDP용)  |

## PacketId Enum
| Id | Name          | Direction     |
|----|---------------|---------------|
| 1  | C_Login       | Client→Server |
| 2  | S_LoginResult | Server→Client |

## Packet Definitions

### C_Login (id=1)
| Field    | Type   | Notes          |
|----------|--------|----------------|
| nickname | string | 2~16자, UTF-8 |

### S_LoginResult (id=2)
| Field        | Type   | Notes                   |
|--------------|--------|-------------------------|
| result       | byte   | 0=성공, 1=닉네임 중복   |
| sessionToken | uint16 | 발급된 세션 id (성공 시)|