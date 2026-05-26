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
    }
}