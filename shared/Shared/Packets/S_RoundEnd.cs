using System.IO;
namespace Shared
{
    // 라운드 종료 신호. 한 팀 전원 사망 시 4명 전원에게 TCP 송신.
    // 클라는 결과 UI 잠깐 표시 후 S_RoundStart 대기.
    public class S_RoundEnd
    {
        public byte WinnerTeam { get; set; } = 0;   // 0=Red, 1=Blue
        public byte RedScore   { get; set; } = 0;
        public byte BlueScore  { get; set; } = 0;

        public byte[] Serialize(ushort sessionToken = 0)
        {
            using var msPayload = new MemoryStream();
            using var bwPayload = new BinaryWriter(msPayload);
            bwPayload.Write(WinnerTeam);   // 1B
            bwPayload.Write(RedScore);     // 1B
            bwPayload.Write(BlueScore);    // 1B
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.S_RoundEnd, sessionToken, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }

        public static S_RoundEnd Deserialize(BinaryReader br)
        {
            return new S_RoundEnd
            {
                WinnerTeam = br.ReadByte(),
                RedScore   = br.ReadByte(),
                BlueScore  = br.ReadByte(),
            };
        }
    }
}
