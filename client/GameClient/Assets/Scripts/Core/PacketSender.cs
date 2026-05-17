using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

public class PacketSender
{
    private readonly NetworkStream stream;

    public PacketSender(NetworkStream stream)
    {
        this.stream = stream;
    }

    public async Task SendAsync(ushort packetId, ushort sessionToken, byte[] payload)
    {
        try
        {
            ushort length = (ushort)(payload?.Length ?? 0);

            byte[] packet = new byte[6 + length];

            // header
            BitConverter.GetBytes(length).CopyTo(packet, 0);
            BitConverter.GetBytes(packetId).CopyTo(packet, 2);
            BitConverter.GetBytes(sessionToken).CopyTo(packet, 4);

            // payload
            if (payload != null && payload.Length > 0)
            {
                payload.CopyTo(packet, 6);
            }

            await stream.WriteAsync(packet, 0, packet.Length);
            Debug.Log($"패킷 전송: packetId={packetId}, length={length}");
        }
        catch (Exception e)
        {
            Debug.LogError("패킷 전송 오류: " + e.Message);
        }
    }
}