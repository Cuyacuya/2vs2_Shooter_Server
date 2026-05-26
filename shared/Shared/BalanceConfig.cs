namespace Shared
{
    // 게임 밸런스 설정 POCO. config/balance.json 에서 로드.
    // 서버·클라가 같은 클래스로 deserialize하여 단일 진실(single source of truth) 확보.
    // JSON 로딩 로직은 각 측에 분리 (서버: GameServer/BalanceLoader.cs, 클라: 별도 helper).
    public class BalanceConfig
    {
        public PlayerConfig  Player  { get; set; } = new();
        public PhysicsConfig Physics { get; set; } = new();
        public WeaponConfig  Weapon  { get; set; } = new();
    }

    public class PlayerConfig
    {
        public float MoveSpeed    { get; set; } = 5.0f;
        public byte  InitialHp    { get; set; } = 100;
        public float JumpVelocity { get; set; } = 5.0f;
        public float PitchMinDeg  { get; set; } = -85.0f;
        public float PitchMaxDeg  { get; set; } = 85.0f;
    }

    public class PhysicsConfig
    {
        public float Gravity { get; set; } = -9.8f;
        public float GroundY { get; set; } = 0.0f;
    }

    public class WeaponConfig
    {
        public byte  Damage              { get; set; } = 25;
        public float HitscanSphereRadius { get; set; } = 0.5f;
    }

    // 전역 접근용 정적 핸들. 서버 Program.cs 또는 클라 부팅 코드가 Current 세팅.
    public static class Balance
    {
        public static BalanceConfig Current { get; set; } = new();
    }
}
