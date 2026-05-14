using System.IO;

namespace Shared
{
    //매칭 큐 대기자 수 알림 (서버 -> 대기 중인 모든 클라)
    public class S_MatchingStatus
    {
        public byte CurrentCount { get; set; } //현재 대기자 수
        public byte MaxCount { get; set; } //매치 정원(=4)

        public byte[] Serialize()
        {
            using var msPayload = new MemoryStream();
            using var bwPayload = new BinaryWriter(msPayload);
            bwPayload.Write(CurrentCount);
            bwPayload.Write(MaxCount);
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.S_MatchingStatus, 0, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }

        public static S_MatchingStatus Deserialize(BinaryReader br)
        {
            return new S_MatchingStatus
            {
                CurrentCount = br.ReadByte(),
                MaxCount = br.ReadByte(),
            };
        }
    }
}