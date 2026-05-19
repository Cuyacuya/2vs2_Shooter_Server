using System.IO;

namespace Shared
{
    public class S_LoginResult
    {
        public bool Success { get; set; }
        public ushort SessionToken { get; set; }
        public string Reason { get; set; } = "";

        public byte[] Serialize()
        {
            using var msPayload = new MemoryStream();
            using var bwPayload = new BinaryWriter(msPayload);
            bwPayload.Write(Success);
            bwPayload.Write(SessionToken);
            PacketIO.WriteString(bwPayload, Reason);
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.S_LoginResult, 0, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }

        public static S_LoginResult Deserialize(BinaryReader br)
        {
            return new S_LoginResult
            {
                Success = br.ReadBoolean(),
                SessionToken = br.ReadUInt16(),
                Reason = PacketIO.ReadString(br),
            };
        }
    }
}
