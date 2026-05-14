using System.Collections.Generic;
using System.IO;

namespace Shared
{
    // 게임 시작 알림 (서버 → 매치된 4명 각자에게)
    public class S_GameStart
    {
        public byte MyTeam { get; set; } // 받는 사람의 팀 (0=Red,1=Blue)
        public List<PlayerInfo> Players { get; set; } = new List<PlayerInfo>();   // 참가자 4명

        public class PlayerInfo
        {
            public ushort Token { get; set; }
            public byte Team { get; set; }       // 0=Red, 1=Blue
            public string Nickname { get; set; } = "";
        }

        public byte[] Serialize()
        {
            using var msPayload = new MemoryStream();
            using var bwPayload = new BinaryWriter(msPayload);

            bwPayload.Write(MyTeam);
            bwPayload.Write((byte)Players.Count);
            foreach (var p in Players)
            {
                bwPayload.Write(p.Token);
                bwPayload.Write(p.Team);
                PacketIO.WriteString(bwPayload, p.Nickname);
            }
            byte[] payload = msPayload.ToArray();

            using var msFull = new MemoryStream();
            using var bwFull = new BinaryWriter(msFull);
            PacketIO.WriteHeader(bwFull, (ushort)PacketId.S_GameStart, 0, (ushort)payload.Length);
            bwFull.Write(payload);
            return msFull.ToArray();
        }

        public static S_GameStart Deserialize(BinaryReader br)
        {
            var pkt = new S_GameStart
            {
                MyTeam = br.ReadByte(),
            };
            byte count = br.ReadByte();
            for (int i = 0; i < count; i++)
            {
                pkt.Players.Add(new PlayerInfo
                {
                    Token = br.ReadUInt16(),
                    Team = br.ReadByte(),
                    Nickname = PacketIO.ReadString(br),
                });
            }
            return pkt;
        }
    }
}