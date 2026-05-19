namespace Shared
{
    public enum PacketId : ushort
    {
        C_Login = 1,
        S_LoginResult = 2,
        S_MatchingStatus = 3,
        S_GameStart = 4,

        C_Input = 10,
        S_Snapshot = 11,
        C_Fire = 12,
        S_HitResult = 13,
    }
}
