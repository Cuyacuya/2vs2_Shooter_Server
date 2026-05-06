using System;
using System.IO; //bw, br, MemoryStream
using System.Text; //encoding.UTF8

//PacketIO : 직렬화/역직렬화 규칙모음
//BinaryWriter로 타입을 명시하여 write시 알아서  Little Endiand으로 바이트로 직렬화
//BinaryReader로 역직렬화 (바이트를 데이터로)
namespace Shared
{
    public static class PacketIO
    {
        //(직렬화)헤더 6바이트 : length(2) + packetId(2) + sessionToken(2)
        public static void WriteHeader(BinaryWriter bw, ushort packetId, ushort sessionToken, ushort payloadLength)
        {
            bw.Write(payloadLength);   // length 2바이트
            bw.Write(packetId);        // packetId 2바이트
            bw.Write(sessionToken);    // sessionToken 2바이트
        }
        //(역직렬화)헤더 읽기: 튜플로 3개 값 한 번에 반환
        public static (ushort length, ushort packetId, ushort sessionToken)
        ReadHeader(BinaryReader br)
        {
            ushort length = br.ReadUInt16();
            ushort packetId = br.ReadUInt16();
            ushort sessionToken = br.ReadUInt16();
            return (length, packetId, sessionToken);
        }
        //(직렬화)문자열 쓰기: ushort 길이(바이트 단위) + utf-8 바이트
        public static void WriteString(BinaryWriter bw, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            bw.Write((ushort)bytes.Length);   // 길이 (바이트 수)
            bw.Write(bytes);                  // UTF-8 바이트
        }

        //(역직렬화)문자열 읽기 : 길이만큼 바이트 읽고 utf-8 디코드
        public static string ReadString(BinaryReader br)
        {
            ushort length = br.ReadUInt16();
            byte[] bytes = br.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}