using System.IO;
namespace Shared
{
    // UDP 핸드셰이크: 클라가 S_GameStart 직후 UDP로 송신.
    // 서버는 발신 endpoint(IP:port)를 sessionToken과 매핑하여 이후 S_Snapshot을 UDP로 보낼 수 있게 한다.
    // payload 없음. 본문 데이터는 헤더 sessionToken 하나.
    public class C_UdpHello
    {
        public byte[] Serialize(ushort sessionToken)
        {
            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.C_UdpHello, sessionToken, payloadLength: 0);
            return msFull.ToArray();
        }

        public static C_UdpHello Deserialize(BinaryReader br)
        {
            return new C_UdpHello();
        }
    }
}
