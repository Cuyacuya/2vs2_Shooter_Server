# 2v2 Arena Shooter

게임서버프로그래밍 학기 프로젝트. C# 자체 제작 서버 + Unity 클라이언트로 구현하는 2vs2 라운드 슈팅.

## 기술 스택
- 서버: C# / .NET 8
- 클라이언트: Unity 2022 LTS
- 통신: TCP + UDP 하이브리드

## 폴더 구조
- `server/` — 게임 서버 (A 담당)
- `client/` — Unity 클라이언트 (B 담당)
- `shared/` — 공통 패킷 정의 (공동)
- `bot/` — 디버그용 봇 클라이언트 (A 담당)
- `docs/` — 설계 문서

## 팀 파트 분담
| 담당 | 파트 |
|---|---|
| A (서버) | C# 서버 코어, TCP/UDP 소켓, 게임 상태머신, 봇 클라이언트 |
| B (클라이언트) | Unity 씬/UI/게임플레이, 패킷 송수신 레이어 |
| 공동 | 패킷 프로토콜 설계, 매주 금요일 통합 테스트 |

## 협업 방법

### 레포 클론
```bash
git clone https://github.com/Cuyacuya/2vs2_Shooter_Server.git
cd 2vs2_Shooter_Server
git checkout dev
```

### 브랜치 규칙
```
main
└── dev
    ├── feature/A-기능명   ← 서버 기능 작업
    └── feature/B-기능명   ← 클라이언트 기능 작업
```
- 평소 작은 작업 → 바로 `dev`에 커밋/푸시
- 기능 단위 작업 → `feature/이름-기능명` 브랜치 생성 → PR → `dev` 머지
- 주차 완성본 → `dev` → `main` 머지

### 커밋 메시지
```
feat: 기능 추가
fix: 버그 수정
refactor: 리팩터링
docs: 문서 수정
```

## 참고 문서
- `docs/agreements.md` — 포트, 엔디안, 문자열 인코딩 등 팀 합의사항
- `docs/protocol.md` — 패킷 구조 및 헤더 정의

## 진행 상황
- [ ] 1주차 — 기반 구축, 로그인 패킷 송수신
- [ ] 2주차 — 로비/방/팀 배정
- [ ] 3주차 — 인게임 진입 + 이동 동기화
- [ ] 4주차 — 전투 + 라운드 판정
- [ ] 5주차 — 마무리 + 포트폴리오 정리
