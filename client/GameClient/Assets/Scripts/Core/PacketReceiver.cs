using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class PacketReceiver
{
    private readonly NetworkStream stream;
    private readonly ConcurrentQueue<ReceivedPacket> receiveQueue;
    private CancellationTokenSource cts;

    public PacketReceiver(NetworkStream stream, ConcurrentQueue<ReceivedPacket> receiveQueue)
    {
        this.stream = stream;
        this.receiveQueue = receiveQueue;
    }

    public void StartReceive()
    {
        cts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(cts.Token);
    }

    public void StopReceive()
    {
        cts?.Cancel();
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                byte[] headerBuffer = await ReadExactAsync(6, token);

                ushort length = BitConverter.ToUInt16(headerBuffer, 0);
                ushort packetId = BitConverter.ToUInt16(headerBuffer, 2);
                ushort sessionToken = BitConverter.ToUInt16(headerBuffer, 4);

                byte[] payload = await ReadExactAsync(length, token);

                receiveQueue.Enqueue(
                    new ReceivedPacket(packetId, sessionToken, payload)
                );
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log("패킷 수신 중단");
        }
        catch (Exception e)
        {
            Debug.LogError("패킷 수신 오류: " + e.Message);
        }
    }

    private async Task<byte[]> ReadExactAsync(int size, CancellationToken token)
    {
        byte[] buffer = new byte[size];
        int offset = 0;

        while (offset < size)
        {
            int read = await stream.ReadAsync(buffer, offset, size - offset, token);

            if (read == 0)
            {
                throw new IOException("서버 연결이 종료되었습니다.");
            }

            offset += read;
        }

        return buffer;
    }
}