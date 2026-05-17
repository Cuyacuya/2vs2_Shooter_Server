using System;
using System.Net.Sockets;
using UnityEngine;

public class TcpClientConnector
{
    private TcpClient client;
    private NetworkStream stream;

    public NetworkStream Stream => stream;
    public bool IsConnected => client != null && client.Connected && stream != null;

    public bool Connect(string ip, int port)
    {
        try
        {
            client = new TcpClient();
            client.Connect(ip, port);

            stream = client.GetStream();

            Debug.Log("서버 연결 성공");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("서버 연결 실패: " + e.Message);
            return false;
        }
    }

    public void Send(byte[] data)
    {
        if (!IsConnected)
        {
            Debug.LogError("서버에 연결되어 있지 않아 패킷을 보낼 수 없습니다.");
            return;
        }

        try
        {
            stream.Write(data, 0, data.Length);
            stream.Flush();

            Debug.Log($"패킷 전송 완료: {data.Length} bytes");
        }
        catch (Exception e)
        {
            Debug.LogError("패킷 전송 실패: " + e.Message);
        }
    }

    public void Disconnect()
    {
        stream?.Close();
        client?.Close();

        stream = null;
        client = null;

        Debug.Log("서버 연결 종료");
    }
}