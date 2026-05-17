using UnityEngine;
using UnityEngine.UI;

public class MainSceneController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private InputField nicknameInput;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private MainUIManager uiManager;

    private void Start()
    {
        connectButton.onClick.AddListener(OnClickConnect);
        exitButton.onClick.AddListener(OnClickExit);
        cancelButton.onClick.AddListener(OnClickCancel);
    }

    private void OnClickConnect()
    {
        string nickname = nicknameInput.text.Trim();

        if (string.IsNullOrEmpty(nickname))
        {
            Debug.Log("닉네임을 입력하세요.");
            return;
        }

        Debug.Log($"서버 접속 시도: {nickname}");

        NetworkManager.Instance.ConnectToServer();

        // 너희 패킷 구조에 맞게 함수 이름은 바꿔도 됨
        NetworkManager.Instance.SendLogin(nickname);
    }

    private void OnClickCancel()
    {
        Debug.Log("매칭 취소");

        NetworkManager.Instance.Disconnect();

        uiManager.HideMatchingPopup();
    }

    private void OnClickExit()
    {
        Debug.Log("게임 종료");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}