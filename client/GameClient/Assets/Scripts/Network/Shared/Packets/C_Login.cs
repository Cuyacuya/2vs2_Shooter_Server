using System.IO;

namespace Shared
{
    public class C_Login
    {
        public string Nickname { get; set; } = "";

        public byte[] Serialize(ushort sessionToken = 0)
        {
            using var msPayload = new MemoryStream();
            using var bwPayload = new BinaryWriter(msPayload);
            PacketIO.WriteString(bwPayload, Nickname);
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.C_Login, sessionToken, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }

        public static C_Login Deserialize(BinaryReader br)
        {
            return new C_Login
            {
                Nickname = PacketIO.ReadString(br),
            };
        }
    }
}
