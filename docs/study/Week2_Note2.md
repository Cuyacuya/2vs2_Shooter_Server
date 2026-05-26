# Week2 Note 2 — 점프/중력 + Ray-Sphere Hitscan (수~목)

> 작성일: 2026-05-26
> 범위: 2주차 수요일 (점프 + 중력 시뮬) ~ 목요일 (C_Fire + ray-sphere hitscan)
> 학습 등급: 🔴 (직접) 중심. 시간 압박으로 두 작업 모두 🔴 → 🟡 강등 (코드 받은 뒤 라인별 해설).
> 진행 방식: 개념·다이어그램·함정 → 코드 → 줄별 해설 + Q&A

---

## 0. 진행 요약

| 작업 | 등급 | 결과물 |
|---|---|---|
| A. 점프 + 중력 시뮬레이션 (semi-implicit Euler + 지면 클램프) | 🔴 → 🟡 | `ClientSession.HandleInput` 점프/Y축 블록 추가 |
| B. ray-sphere hitscan + HP/사망 + 브로드캐스트 | 🔴 → 🟡 | `ClientSession.HandleFire` 신규 + `MatchManager.GetMatchSnapshot()` |

**커밋**: `feature/HD-week2` 브랜치, 수요일 점프 작업 1개 커밋 완료 (`523814d`). 목요일 hitscan은 빌드 검증 후 커밋 예정.

---

## 1. 점프 + 중력 — semi-implicit Euler

### 핵심 운동방정식

```
velocity.y += gravity * dt      ← 속도 먼저 갱신
position.y += velocity.y * dt   ← 위치 나중 갱신
```

이 순서가 **semi-implicit Euler**. 시뮬레이션 안정성이 explicit Euler(위치 먼저)보다 좋음. 일관성만 지키면 됨.

점프 = "지면에 있을 때만 `VelocityY = JumpVelocity` 한 번 대입". 그 다음은 중력이 알아서 끌어내림.

### dt가 어디서 나오나? — 받자마자 처리 모델

3주차 정식 틱 루프 전까지는 **C_Input 패킷 받을 때마다 처리**. 그러면 dt는?

```csharp
long nowTicks = Stopwatch.GetTimestamp();
dt = (nowTicks - Player.LastInputTicks) / (float)Stopwatch.Frequency;
Player.LastInputTicks = nowTicks;
```

→ "이전 입력 시점과 현재 시점의 차이" = dt.

### 함정 (이미 화요일 코드에 방어 들어가있음)

| 함정 | 결과 | 대응 |
|---|---|---|
| 첫 입력에 `LastInputTicks == 0` → 거대 dt | 우주로 날아감 | `if (LastInputTicks == 0) dt = 0;` |
| 클라 0.5초 멈췄다 입력 보내면 dt가 큼 | 점프가 텔레포트, 지면 뚫음 | `dt = Math.Min(dt, 0.1f)` 클램프 |

### 지면 충돌 처리 — 함정 4종

```csharp
// 1) 점프 입력 + IsGrounded 검사
if (jump && Player.IsGrounded)
{
    Player.VelocityY = JumpVelocity;
    Player.IsGrounded = false;     // ★ 같은 프레임 지면 검사에 즉시 false
}
// 2) 중력 적분
Player.VelocityY += Gravity * dt;
// 3) 위치 적분
Player.PosY += Player.VelocityY * dt;
// 4) 지면 클램프 (적분 직후 즉시)
if (Player.PosY <= GroundY)
{
    Player.PosY = GroundY;
    Player.VelocityY = 0f;          // ★ 누적 낙하속도 제거
    Player.IsGrounded = true;
}
```

| 함정 | 결과 | 대응 |
|---|---|---|
| `if (jump)` 만 검사 (IsGrounded 누락) | 무한 점프 | && IsGrounded |
| 점프 입력 후 IsGrounded=false 안 함 | 같은 프레임 4번 검사에 다시 true로 → 한 프레임만에 착지 | 즉시 false |
| 지면 검사 적분 **이전** | 한 프레임 동안 지면 박혀있다 보정 → 떨림 | 적분 **직후** 즉시 |
| 착지 시 VelocityY=0 누락 | 다음 프레임 즉시 다시 -중력 → 박힘 | 0으로 초기화 |
| 4번 IsGrounded=true 누락 | 한 번 점프 후 영영 점프 못 함 | true로 복구 |

### 점프 한 사이클 시뮬 (JumpVelocity=5, Gravity=-9.8, dt=0.05)

| t(s) | VelocityY | PosY | IsGrounded | 사건 |
|---|---|---|---|---|
| 0.00 | 0 → 5 | 0 | true → false | 점프 입력 |
| 0.05 | 4.51 | 0.25 | false | (V += -9.8*0.05) |
| 0.10 | 4.02 | 0.45 | false | |
| ... | | | | |
| 0.51 | ~0 | ~1.27 | false | 정점 |
| ... | | | | |
| 1.02 | -5 → 0 | 0 | false → true | 착지 (PosY ≤ 0) |

약 1초 비행. JumpVelocity를 올리면 더 높이/오래 뜸.

### "받자마자 처리"의 의도된 한계

`HandleInput`은 C_Input이 올 때만 호출됨. **공중에서 클라가 입력 안 보내면 중력 적분 멈춤** = 공중에 떠 있음.

→ 2주차는 클라가 매 프레임 입력 보내준다는 가정으로 진행. 3주차 정식 30Hz 틱 루프 도입 시 해결됨 (서버가 강제로 적분 진행).

이게 WBS 2주차 종료 기준의 "**TCP는 부드럽지 않다, 점프가 텔레포트 같다**" 체감 포인트 중 하나.

---

## 2. Ray-Sphere Hitscan — 수학 본질

### 좌표계 정합성 (HandleInput과 동일)

기존 이동 코드의 yaw 적용:
- `yaw=0` → forward = +Z
- `yaw=90` → forward = +X (시계방향, 위에서 봤을 때)

이 좌표계를 그대로 따라가야 ray 방향이 맞음.

### Forward 벡터 (yaw + pitch → 단위벡터)

```
dx = sin(yaw) · cos(pitch)
dy = sin(pitch)              ← 위(+)/아래(-)
dz = cos(yaw) · cos(pitch)
```

**단위벡터 검증** (|d|=1):
```
dx² + dy² + dz²
= sin²y·cos²p + sin²p + cos²y·cos²p
= cos²p(sin²y + cos²y) + sin²p
= cos²p · 1 + sin²p
= 1 ✓
```

### 광선 방정식

`P(t) = O + t·d`, t ≥ 0
- O = attacker 위치
- d = forward 단위벡터
- t = 시작점부터 거리 (단위벡터라 t가 그대로 미터 단위 거리)

### 구 충돌 판정 (네 단계)

```
L = C - O                       # 시작점에서 구 중심까지 벡터
t_ca = L · d                    # L을 ray 방향에 사영
if t_ca < 0:        miss (구가 ray 뒤)
d² = L·L - t_ca²                # 피타고라스: ray와 구 중심 수선거리²
if d² > r²:         miss (옆으로 빗나감)
t = t_ca - √(r² - d²)           # 입사점까지 거리
if t < 0:           miss (시작점이 구 내부)
```

### 도식

```
         C (구 중심, 반지름 r)
        ╱│
       ╱ │ 수선거리 = √d² 
      ╱  │
   O ────┴────────→ d
         ↑
        t_ca
```

수선거리² > r² 면 ray가 구를 지나치지 못함. 작거나 같으면 ray가 구 표면을 만남 → 두 교점 중 가까운 점 거리 = `t_ca - √(r²-d²)`.

### 다중 표적 처리

```
bestT = ∞, hitTarget = null
for each enemy (적팀 && 생존):
    t = ray_sphere(enemy)
    if t < bestT:
        bestT = t
        hitTarget = enemy
```

**가장 가까운 적 1명만 채택** (관통 X). bestT 초기값 `float.MaxValue`.

---

## 3. 코드 — HandleFire 5단계

```csharp
private void HandleFire(C_Fire pkt)
{
    const float DEG2RAD = MathF.PI / 180f;
    float radius = Balance.Current.Weapon.HitscanSphereRadius;
    byte damage  = Balance.Current.Weapon.Damage;

    // 1) attacker 스냅샷 (lock 짧게)
    float ox, oy, oz, yaw, pitch;
    bool attackerDead;
    lock (Player.Lock)
    {
        attackerDead = Player.IsDead;
        ox = Player.PosX; oy = Player.PosY; oz = Player.PosZ;
        yaw = Player.Yaw; pitch = Player.Pitch;
    }
    if (attackerDead) return;

    // 2) forward 단위벡터
    float yawR = yaw * DEG2RAD;
    float pitR = pitch * DEG2RAD;
    float dx = MathF.Sin(yawR) * MathF.Cos(pitR);
    float dy = MathF.Sin(pitR);
    float dz = MathF.Cos(yawR) * MathF.Cos(pitR);

    // 3) 적팀 후보 ray-sphere 검사 (가장 가까운 1명)
    var sessions = MatchManager.Instance.GetMatchSnapshot();
    ClientSession? hitSession = null;
    float bestT = float.MaxValue;
    float rSq = radius * radius;

    foreach (var enemy in sessions)
    {
        if (enemy == this) continue;
        if (enemy.Team == this.Team) continue;

        float ex, ey, ez;
        bool eDead;
        lock (enemy.Player.Lock)
        {
            eDead = enemy.Player.IsDead;
            ex = enemy.Player.PosX; ey = enemy.Player.PosY; ez = enemy.Player.PosZ;
        }
        if (eDead) continue;

        float lx = ex - ox, ly = ey - oy, lz = ez - oz;
        float tca = lx*dx + ly*dy + lz*dz;
        if (tca < 0) continue;

        float dSq = (lx*lx + ly*ly + lz*lz) - tca*tca;
        if (dSq > rSq) continue;

        float t = tca - MathF.Sqrt(rSq - dSq);
        if (t < 0) continue;
        if (t < bestT) { bestT = t; hitSession = enemy; }
    }

    if (hitSession == null) return;

    // 4) HP 감소 + 사망 판정
    byte hpAfter; byte isKill;
    lock (hitSession.Player.Lock)
    {
        int newHp = hitSession.Player.Hp - damage;
        if (newHp <= 0)
        {
            hitSession.Player.Hp = 0;
            hitSession.Player.IsDead = true;
            isKill = 1;
        }
        else
        {
            hitSession.Player.Hp = (byte)newHp;
            isKill = 0;
        }
        hpAfter = hitSession.Player.Hp;
    }

    // 5) S_HitResult 4명 전원 브로드캐스트
    var hit = new S_HitResult
    {
        AttackerToken = this.SessionToken,
        VictimToken   = hitSession.SessionToken,
        Damage        = damage,
        VictimHpAfter = hpAfter,
        IsKill        = isKill,
    };
    byte[] bytes = hit.Serialize();
    foreach (var s in sessions)
        _ = s.SendAsync(bytes);
}
```

### MatchManager 추가

```csharp
public List<ClientSession> GetMatchSnapshot()
{
    lock (_lock)
    {
        return new List<ClientSession>(_waiting);
    }
}
```

---

## 4. 핵심 학습 — 동시성 패턴

### "스냅샷 복사" 패턴

여러 스레드가 공유 객체에 접근할 때, **lock 안에서 복사본을 만들어 반환**하면 호출자는 lock 없이 안전하게 사용 가능.

| 종류 | 용도 |
|---|---|
| **컬렉션 스냅샷** (`GetMatchSnapshot()`) | 외부에서 lock 없이 순회 |
| **값 스냅샷** (HandleFire의 ox/oy/...) | 계산 중 다른 스레드 변경 영향 차단 |

### 락 보유 시간 최소화

- attacker 위치 6개 변수만 lock 안에서 복사 (마이크로초)
- ray 계산은 lock 밖에서
- 각 enemy도 자기 lock 잠깐만
- victim HP 갱신은 victim lock 안에서 원자적

→ **여러 락을 동시에 보유하지 않음** → 데드락 회피.

### 사망 처리 — 원자성

```csharp
lock (hitSession.Player.Lock)
{
    int newHp = hitSession.Player.Hp - damage;  // int 캐스팅
    if (newHp <= 0) { Hp=0; IsDead=true; isKill=1; }
    else            { Hp=(byte)newHp; isKill=0; }
    hpAfter = Hp;
}
```

**언더플로우 방지**: `byte` 끼리 빼면 `0 - 25 = 231` (오버플로우). `int`로 캐스팅해 음수 잡고 분기.

**HP=0과 IsDead=true 동시 갱신**: lock 안에서 같이 처리해야 다른 스레드가 "HP=0인데 IsDead=false" 중간 상태 못 봄.

---

## 5. 서버 권한 모델 (재확인)

### 누가 뭘 결정하나

| 항목 | 서버 | 클라 |
|---|---|---|
| 위치/속도/점프 | ✅ | 표시만 |
| HP, 데미지, 사망 | ✅ | UI만 |
| 사격 명중 판정 | ✅ | — |
| 머즐 플래시, 발자국 | — | ✅ |
| 카메라, UI | — | ✅ |
| 입력 감지 | — | ✅ → 송신 |

**원칙**: "이걸 클라가 거짓말하면 게임이 불공정해지나?" → Yes면 서버.

### C_Fire 가 payload 0인 이유

클라는 "쐈다" 신호만. 위치·방향은 서버가 attacker의 PlayerState에서 꺼냄. 클라가 위치 위조 못 함.

CS:GO/Valorant/Overwatch 전부 동일 구조.

### Unity의 Input System / Physics Engine은?

- **Input System**: 클라에서 키 감지 → C_Input 패킷 직렬화. 입력 수집기 역할.
- **Physics**: 두 패턴 중 선택
  1. 서버가 보낸 좌표 표시만 (반응성 ↓, 우리 2주차)
  2. Client-side prediction + Reconciliation (반응성 ↑, 실제 FPS 표준, 클라/서버가 같은 물리 코드 공유)
- Unity Physics는 결정론적이지 않아 멀티플레이 동기화엔 부적합. 보통 직접 짠 공식을 Shared로 공유.

---

## 6. 디스패치 (Dispatch)

### 정의
"받은 패킷을 packetId 보고 종류별 처리기로 **분배**" 하는 코드.

### 책임 분리 (server/CLAUDE.md 원칙)

| 역할 | 위치 |
|---|---|
| **디스패처** — 헤더 파싱 + packetId 분기 + 라우팅 | `HandlePacket` switch |
| **핸들러** — 단일 패킷 종류의 비즈니스 로직 | `HandleLogin`, `HandleInput`, `HandleFire` |

**원칙**: 핸들러는 1개 패킷 종류 처리만. 섞지 말 것.

- 좋은 예: `HandleFire`는 사격 판정만
- 나쁜 예: `HandleInput` 안에서 사격 판정도 처리

이유: 디버깅·테스트·확장 모두 1핸들러 = 1책임이라 깔끔.

---

## 7. 성능 — 매 프레임 사격하면?

### 현재 코드 부하

- 사격당 ray-sphere = 곱셈·덧셈 ~20번
- 적 2명 × 4명 클라 = 초당 ~몇 발 수준 (사람 손가락 한계)
- lock 경합 ~없음

### 광클 (매 프레임 C_Fire) 대비

현재 코드는 광클을 막지 않음 = 핵 취약점. 해결책 (4주차 탄약 작업과 함께 도입):

```csharp
public long NextFireAllowedTicks { get; set; } = 0;

// HandleFire 맨 앞:
long now = Stopwatch.GetTimestamp();
if (now < Player.NextFireAllowedTicks) return;
Player.NextFireAllowedTicks = now + (long)(0.15 * Stopwatch.Frequency);
```

→ 클라가 매 프레임 보내도 서버는 0.15초당 1번만 처리.

### 서버 부하 줄이는 3패턴

| 패턴 | 적용 |
|---|---|
| **Rate limit** (쿨다운) | 사격, 재장전, 점프 |
| **Throttle** (다운샘플) | 30Hz 시뮬 → 20Hz 송신 (3주차) |
| **Batch** (일괄 처리) | 한 틱 동안 누적 입력 일괄 처리 (3주차) |

### "스냅샷"의 두 가지 다른 의미

| | 동시성용 (이 노트) | 네트워크용 (3주차 S_Snapshot) |
|---|---|---|
| 언제 | 사격 시 1번 / 컬렉션 접근 시 | 주기적 20Hz |
| 용도 | 락 보유 시간 최소화 | 4명 위치/HP를 클라에 송신 |
| 비용 | 무료 (값 복사) | 본격 부하 (이게 송신 대역폭) |

같은 단어지만 용도가 다름.

---

## 8. 함정 학습 (이번 작업 추가분)

| 함정 | 결과 | 대응 |
|---|---|---|
| 자기 자신 ray-sphere에 포함 | 자해 발생 | `if (enemy == this) continue;` |
| 같은 팀 포함 | 팀킬 | `if (enemy.Team == this.Team) continue;` |
| 죽은 적도 후보 | 시체에 또 맞음 + 음수 HP | `if (eDead) continue;` |
| `tca < 0` 가드 누락 | 등 뒤 적도 맞음 (WBS 함정표 명시) | 가드 추가 |
| `t < 0` 가드 누락 (시작점 내부) | 코밑 적 자동 명중 | 가드 추가 |
| `byte` HP 직접 빼기 | 0 - 25 = 231 언더플로우 | `int` 캐스팅 후 분기 |
| HP=0과 IsDead 분리 갱신 | 중간 상태 노출 | 같은 lock 안에서 한 번에 |
| 광클 미차단 | 핵 가능 | 쿨다운 (4주차) |
| Pitch 좌표계 (+ 위 vs + 아래) | dy 부호 반대 → 천장 쐈는데 지면 맞음 | "+pitch = 위" 통일, 클라와 일치 확인 |

---

## 9. 검증 시나리오

- [x] Shared 빌드 통과
- [x] GameServer 빌드 통과
- [ ] 봇 4개 매칭 후 C_Input 점프 송신 → 서버 PosY 곡선 로그
- [ ] 점프 후 약 1초 비행 → 착지 확인
- [ ] 봇이 C_Fire 송신 (금요일 작업) → 동팀 안 맞고 적팀만 맞는지
- [ ] HP 0 도달 → IsDead=true, 추가 사격 무시
- [ ] S_HitResult 4명 모두 수신

---

## 10. 다음 단계 — 금요일

| 작업 | 등급 | 시간 |
|---|---|---|
| 봇 랜덤 이동 + 가끔 점프/사격 | 🟡 | 1h |
| 위치 브로드캐스트 (TCP, 받자마자 4명에게) | 🟡 | 1h |
| 통합 테스트 — 4명 3인칭 + 점프 + 사격 + HP | 🔴 | 2h |
| TCP 위치 동기화 한계 관찰 (점프 끊김 등 메모) | 🔴 | 0.5h |

**2주차 종료 기준 ✅**
- 4명 위치가 화면에 보임
- 자/적팀 구분
- 점프 → 중력 → 착지 동작
- 사격 시 HP 감소 + 사망 (정확한 ray-sphere 판정)
- "**TCP는 부드럽지 않다, 점프가 텔레포트 같다**" 직접 체감

---

## 11. AI 활용 회고 — 🔴 → 🟡 강등의 정당성

### 결정 흐름
- WBS: 점프/중력 = 🔴, ray-sphere = 🔴 ("AI에 통째로 받지 말 것")
- 실제: 둘 다 코드 제공 후 라인별 해설 (🟡)
- 사유: 시간 압박 (2주차 일정 빡빡)

### 학습 부채 (방학 / 5주차 회고에서 갚을 것)
- **ray-sphere 수학을 종이에 직접 유도**해서 식 외우기. 4주차 캡슐 hitscan 정밀화 작업에 같은 수학 또 등장.
- **점프 시뮬을 콘솔 단위 테스트로 분리 검증** — WBS 위험 시점 대응책에 명시.
- **여러 dt 값으로 점프 한 사이클 손 시뮬** — 안정성 직관 확보.

### 메타 학습
- 시간 압박 = 강등은 합리적 선택. 단, **부채 명시 + 추후 회수 계획**이 있어야 학습 손해 최소화.
- 메모리 규칙 (`feedback_coding_style.md`)이 명확해서 강등 결정이 빠름 — 룰화의 가치.

---

## 12. 한 줄 요약

> "물리는 dt로 정규화, 충돌은 벡터 사영, 동시성은 짧은 lock + 스냅샷 복사. 클라는 트리거만, 판정은 전부 서버."
