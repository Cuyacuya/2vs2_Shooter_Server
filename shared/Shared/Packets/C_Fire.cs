using System.IO;
namespace Shared
{
    //클라가 사격 버튼 클릭시 서버에 알리는 패킷
    public class C_Fire
    {
        public byte[] Serialize(ushort sessionToken = 0)
        {
            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.C_Fire, sessionToken, payloadLength: 0);
            return msFull.ToArray();
        }

        //헤더는 이미 읽은 상태로 가정, payload가 없으므로 br에서 더 읽지 않음.
        public static C_Fire Deserialize(BinaryReader br)
        {
            return new C_Fire();
        }
    }
}