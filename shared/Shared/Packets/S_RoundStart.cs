using System.IO;
namespace Shared
{
    // 라운드 시작 신호. 4명 전원에게 TCP 송신.
    // 실제 위치/HP 리셋은 같은 시점에 송신되는 S_Snapshot이 담당.
    public class S_RoundStart
    {
        public byte RoundIndex { get; set; } = 0;   // 1부터 시작
        public byte RedScore   { get; set; } = 0;
        public byte BlueScore  { get; set; } = 0;

        public byte[] Serialize(ushort sessionToken = 0)
        {
            using var msPayload = new MemoryStream();
            using var bwPayload = new BinaryWriter(msPayload);
            bwPayload.Write(RoundIndex);   // 1B
            bwPayload.Write(RedScore);     // 1B
            bwPayload.Write(BlueScore);    // 1B
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.S_RoundStart, sessionToken, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }

        public static S_RoundStart Deserialize(BinaryReader br)
        {
            return new S_RoundStart
            {
                RoundIndex = br.ReadByte(),
                RedScore   = br.ReadByte(),
                BlueScore  = br.ReadByte(),
            };
        }
    }
}
