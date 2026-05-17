public class ReceivedPacket
{
    public ushort PacketId { get; }
    public ushort SessionToken { get; }
    public byte[] Payload { get; }

    public ReceivedPacket(ushort packetId, ushort sessionToken, byte[] payload)
    {
        PacketId = packetId;
        SessionToken = sessionToken;
        Payload = payload;
    }
}