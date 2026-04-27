# 팀 합의 사항

> Day 1에 정한 규칙. 끝까지 지킨다.

## 네트워크
| 항목 | 값 |
|---|---|
| TCP 포트 | 7777 |
| UDP 포트 | 7778 |
| 엔디안 | Little Endian (BinaryWriter/BinaryReader 기본값) |
| 문자열 인코딩 | UTF-8, length-prefixed (앞 2byte = 길이) |

## 패킷 규칙
| 항목 | 규칙 |
|---|---|
| 명명 규칙 | `C_*` = Client→Server, `S_*` = Server→Client |
| packetId 시작값 | 1부터 (0은 미사용/예약) |

## Git 규칙
| 항목 | 규칙 |
|---|---|
| 브랜치 전략 | `main`, `dev`, `feature/이름-기능` |
| main 브랜치 | 주차별 완성본 머지할 때만 사용 |
| 평소 작업 | dev 브랜치에서 진행 |
| 커밋 메시지 prefix | `feat:`, `fix:`, `refactor:`, `docs:` |

## 역할 분담
| 담당 | 파트 |
|---|---|
| A (서버) | C# 서버 코어, TCP/UDP 소켓, 게임 상태 머신, 봇 클라이언트 |
| B (클라이언트) | Unity 씬/UI/게임플레이, 패킷 송수신 레이어 |
| 공동 | 패킷 프로토콜 설계, 매주 금요일 통합 테스트 |
