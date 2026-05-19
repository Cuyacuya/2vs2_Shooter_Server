using System.Collections.Generic;

namespace Shared
{
    public class S_Snapshot
    {
        public List<PlayerSnapshot> Players = new();
    }

    public class PlayerSnapshot
    {
        public ushort SessionToken;

        public int Team;

        public float PosX;
        public float PosY;
        public float PosZ;

        public float Yaw;
        public float Pitch;

        public int Hp;

        public bool IsDead;
    }
}
