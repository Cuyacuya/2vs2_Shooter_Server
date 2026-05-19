using System.IO;
namespace Shared
{
    public class S_HitResult
    {
        public ushort AttackerToken { get; set; } = 0;
        public ushort VictimToken { get; set; } = 0;
        public byte Damage { get; set; } = 0;
        public byte VictimHpAfter { get; set; } = 0;
        public byte IsKill { get; set; } = 0; //1:사망, 0:생존
        //binarywriter의 write(bool) = 1B

        public byte[] Serialize(ushort sessionToken = 0)
        {
            using var msPayload = new MemoryStream();
            using var bwPayload = new BinaryWriter(msPayload);
            bwPayload.Write(AttackerToken);   // 2B
            bwPayload.Write(VictimToken);     // 2B
            bwPayload.Write(Damage);          // 1B
            bwPayload.Write(VictimHpAfter);   // 1B
            bwPayload.Write(IsKill);          // 1B
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.S_HitResult, sessionToken, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }
        public static S_HitResult Deserialize(BinaryReader br)
        {
            return new S_HitResult
            {
                AttackerToken = br.ReadUInt16(),
                VictimToken = br.ReadUInt16(),
                Damage = br.ReadByte(),
                VictimHpAfter = br.ReadByte(),
                IsKill = br.ReadByte(),
            };
        }
    }
}