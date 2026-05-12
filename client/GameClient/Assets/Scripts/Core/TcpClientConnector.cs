using System;
using System.Net.Sockets;
using UnityEngine;

public class TcpClientConnector
{
    private TcpClient client;
    private NetworkStream stream;

    public NetworkStream Stream => stream;

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

    public void Disconnect()
    {
        stream?.Close();
        client?.Close();

        stream = null;
        client = null;

        Debug.Log("서버 연결 종료");
    }
}