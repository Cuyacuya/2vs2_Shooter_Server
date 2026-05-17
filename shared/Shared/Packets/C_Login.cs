using System.IO;

namespace Shared
{
    // 클라가 접속 직후 서버에 보내는 첫 패킷. 닉네임을 알린다.
    public class C_Login
    {
        public string Nickname { get; set; } = "";

        // payload + 헤더까지 합친 byte[] 생성 (송신용)
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

        // 헤더는 이미 읽힌 상태라고 가정하고 payload만 읽는다 (디스패치 후 호출)
        public static C_Login Deserialize(BinaryReader br)
        {
            return new C_Login
            {
                Nickname = PacketIO.ReadString(br),
            };
        }
    }
}
