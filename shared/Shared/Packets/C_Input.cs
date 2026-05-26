using System.IO;
namespace Shared
{
    public class C_Input
    {
        // 비트마스크: bit0=W, bit1=S, bit2=A, bit3=D, bit4=Jump
        public byte InputBits { get; set; } = 0;
        public float Yaw { get; set; } = 0f;      // 좌우 시점 (0~360도)
        public float Pitch { get; set; } = 0f;    // 상하 시점 (-85~+85도)
        public byte[] Serialize(ushort sessionToken = 0)
        {
            using var msPayload = new MemoryStream(); //데이터보관
            using var bwPayload = new BinaryWriter(msPayload); //C#타입 값을 byte로 바꾸는 변환기
            bwPayload.Write(InputBits); //1B
            bwPayload.Write(Yaw);       //4B
            bwPayload.Write(Pitch);     //4B
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.C_Input, sessionToken, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }
        public static C_Input Deserialize(BinaryReader br)
        {
            return new C_Input
            {
                InputBits = br.ReadByte(),
                Yaw = br.ReadSingle(),
                Pitch = br.ReadSingle(),
            };
        }
    }
}