namespace Shared
{
    public enum PacketId : ushort
    {
        C_Login = 1,
        S_LoginResult = 2,
        S_MatchingStatus = 3,
        S_GameStart = 4,
        C_Input = 5,
        S_Snapshot = 6,
        C_Fire = 7,
        S_HitResult = 8,
        C_UdpHello = 9,
        S_RoundStart = 10,
        S_RoundEnd = 11,
        S_MatchEnd = 12,
    }
}