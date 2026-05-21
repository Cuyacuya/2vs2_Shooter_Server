namespace GameServer
{
    //한 명의 인게임 상태. 매치 시작 시 4개 인스턴스 생성, 매치 종료 시 폐기
    public class PlayerState
    {
        // 식별
        public ushort SessionToken { get; set; } = 0;
        public byte Team { get; set; } = 0; //Red=0, 1=Blue

        //위치
        public float PosX { get; set; } = 0f;
        public float PosY { get; set; } = 0f;
        public float PosZ { get; set; } = 0f;

        //시점
        public float Yaw { get; set; } = 0f;
        public float Pitch { get; set; } = 0f;
        //점프/중력
        public float VelocityY { get; set; } = 0f;
        public bool IsGrounded { get; set; } = true;
        //전투
        public byte Hp { get; set; } = 100;
        public bool IsDead { get; set; } = false;
        public long LastInputTicks { get;set;} = 0;

        //동시성 보호 PlayerState 접근 시 반드시 이 객체로 lock
        public readonly object Lock = new();
    }
}