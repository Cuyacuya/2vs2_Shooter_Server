using System.IO;

namespace Shared
{
    public class S_MatchingStatus
    {
        public byte CurrentCount { get; set; }
        public byte MaxCount { get; set; }

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
