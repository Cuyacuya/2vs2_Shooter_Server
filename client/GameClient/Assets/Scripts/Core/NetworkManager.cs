using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;
    private TcpClientConnector connector;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            connector = new TcpClientConnector();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    private void Start()
    {
        connector.Connect("127.0.0.1", 7777);
    }
}