using System.IO;

namespace Shared
{
    // 서버가 C_Login 처리 후 응답하는 패킷
    public class S_LoginResult
    {
        public bool Success { get; set; }
        public ushort SessionToken { get; set; }   // 성공 시 발급된 토큰
        public string Reason { get; set; } = "";   // 실패 시 사유 (예: "MATCH_IN_PROGRESS")

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
            // S→C 방향이라 헤더의 sessionToken 필드는 0 (UDP에서만 의미 있음)
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
