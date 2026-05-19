using System.IO;
using System.Text;

namespace Shared
{
    public static class PacketIO
    {
        public static void WriteHeader(BinaryWriter bw, ushort packetId, ushort sessionToken, ushort payloadLength)
        {
            bw.Write(payloadLength);
            bw.Write(packetId);
            bw.Write(sessionToken);
        }

        public static (ushort length, ushort packetId, ushort sessionToken) ReadHeader(BinaryReader br)
        {
            ushort length = br.ReadUInt16();
            ushort packetId = br.ReadUInt16();
            ushort sessionToken = br.ReadUInt16();
            return (length, packetId, sessionToken);
        }

        public static void WriteString(BinaryWriter bw, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            bw.Write((ushort)bytes.Length);
            bw.Write(bytes);
        }

        public static string ReadString(BinaryReader br)
        {
            ushort length = br.ReadUInt16();
            byte[] bytes = br.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
