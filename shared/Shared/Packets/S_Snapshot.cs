using System.IO;
using System.Collections.Generic;
namespace Shared
{
    //한 명분의 스냅샷 데이터 (24B)
    public class PlayerSnapshot
    {
        public ushort Token { get; set; } = 0;
        public float PosX { get; set; } = 0f;
        public float PosY { get; set; } = 0f;
        public float PosZ { get; set; } = 0f;
        public float Yaw { get; set; } = 0f;
        public float Pitch { get; set; } = 0f;
        public byte Hp { get; set; } = 0;
        public byte StateBits { get; set; } = 0;   // bit0=isDead 
    }

    //서버가 4명의 클라에게 브로드캐스트하는 스냅샷 패킷
    //payload = playerCount(1B) + 24B X playerCount
    public class S_Snapshot
    {
        public List<PlayerSnapshot> Players { get; set; } = new();
        public byte[] Serialize(ushort sessionToken = 0)
        {
            using var msPayload = new MemoryStream();
            using var bwPayload = new BinaryWriter(msPayload);

            bwPayload.Write((byte)Players.Count);   // playerCount 1B
            foreach (var p in Players)
            {
                bwPayload.Write(p.Token);       // 2B
                bwPayload.Write(p.PosX);        // 4B
                bwPayload.Write(p.PosY);        // 4B
                bwPayload.Write(p.PosZ);        // 4B
                bwPayload.Write(p.Yaw);         // 4B
                bwPayload.Write(p.Pitch);       // 4B
                bwPayload.Write(p.Hp);          // 1B
                bwPayload.Write(p.StateBits);   // 1B
            }
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.S_Snapshot, sessionToken, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }
        public static S_Snapshot Deserialize(BinaryReader br)
        {
            var pkt = new S_Snapshot();
            byte count = br.ReadByte();
            for (int i = 0; i < count; i++)
            {
                pkt.Players.Add(new PlayerSnapshot
                {
                    Token = br.ReadUInt16(),
                    PosX = br.ReadSingle(),
                    PosY = br.ReadSingle(),
                    PosZ = br.ReadSingle(),
                    Yaw = br.ReadSingle(),
                    Pitch = br.ReadSingle(),
                    Hp = br.ReadByte(),
                    StateBits = br.ReadByte(),
                });
            }
            return pkt;
        }
    }
}