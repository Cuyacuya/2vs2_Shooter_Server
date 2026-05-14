# Day5 — 5/13(금) 트러블슈팅

> 날짜별 발생한 오류 기록 (각 항목: 문제 / 원인 / 해결 / 결과 / 배운점)

---

## #1 — S_GameStart.cs `new()` 구문 컴파일 에러

### 문제 사항
```
error CS8400: '대상으로 형식화된 개체 만들기' 기능은 C# 8.0에서 사용할 수 없습니다.
언어 버전 9.0 이상을 사용하세요.
파일: shared/Shared/Packets/S_GameStart.cs, line 10
```

### 원인 분석
- `Shared` 프로젝트의 TargetFramework이 `netstandard2.1` → 기본 C# 언어 버전 8.0
- 사용한 `new()` 구문(target-typed new expression)은 C# 9.0부터 지원
- 서버/봇은 `net8.0`이라 문제없지만, Shared만 구버전 적용

### 해결 방법
`new()` → `new List<PlayerInfo>()` 로 명시적 타입 사용 (C# 8.0 호환)

```csharp
// 변경 전
public List<PlayerInfo> Players { get; set; } = new();

// 변경 후
public List<PlayerInfo> Players { get; set; } = new List<PlayerInfo>();
```

(대안: `Shared.csproj`에 `<LangVersion>latest</LangVersion>` 추가하면 `new()` 사용 가능. 하지만 현재 방식이 더 명확.)

### 결과
빌드 통과 (경고 0 / 오류 0).

### 배운점
- 프로젝트마다 TargetFramework이 다르면 사용 가능한 C# 문법도 다름
- Shared가 Unity 호환을 위해 `netstandard2.1`로 묶여 있어서 최신 문법 사용 제한
- Shared에 코드 쓸 때는 **C# 8.0 호환 문법**만 사용 (명시적 타입, 패턴 매칭 일부 X 등)
- 서버/봇(net8.0)에선 자유롭게 최신 문법 사용 OK

---

## #2 — Shared.dll 잠금으로 빌드 실패 (MSB3027 / MSB3021)

### 문제 사항
```
error MSB3027: "Shared.dll"을(를) "bin\Debug\net8.0\Shared.dll"(으)로 복사할 수 없습니다.
재시도 횟수(10)를 초과하여 작업을 수행하지 못했습니다.
파일이 "GameServer (32152)"에 의해 잠겨 있습니다.
```

### 원인 분석
- 다른 터미널에서 `dotnet run --project server\GameServer` 가 아직 실행 중
- 실행 중인 GameServer.exe가 `Shared.dll`을 로드해서 잡고 있음
- 빌드 시 새 `Shared.dll`을 출력 폴더에 복사하려는데 파일이 잠겨 있어 실패

### 해결 방법
실행 중인 서버 프로세스를 종료한 뒤 빌드:

```powershell
# 옵션 A: 실행 중인 터미널에서 Ctrl+C
# 옵션 B: 다른 터미널에서 강제 종료
Get-Process -Name GameServer -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet build Game.sln
```

### 결과
빌드 통과 (경고 0 / 오류 0).

### 배운점
- **빌드하기 전 실행 중인 서버 프로세스 끄기**가 습관이 되어야 함
- 에러 메시지에 PID가 친절히 표시됨 (예: `(32152)`) → 그걸로 어느 프로세스가 잠그는지 알 수 있음
- 비슷한 에러: `error MSB3021` (복사 실패), `MSB3026` (재시도 중)
- Windows의 일반 패턴: 실행 중인 exe가 자기 dll/exe를 잠금 → 덮어쓰기 불가
