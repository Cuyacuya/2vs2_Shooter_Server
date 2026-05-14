using UnityEngine.UI;
using UnityEngine;

public class MainUIManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject matchingPopupPanel;

    [Header("Matching UI")]
    [SerializeField] private Text statusText;
    [SerializeField] private Text matchingCountText;

    private void Start()
    {
        ShowMain();
    }

    public void ShowMain()
    {
        mainPanel.SetActive(true);
        matchingPopupPanel.SetActive(false);
    }

    public void ShowMatchingPopup()
    {
        mainPanel.SetActive(true);
        matchingPopupPanel.SetActive(true);

        statusText.text = "매칭 대기중...";
        matchingCountText.text = "0/4";
    }

    public void UpdateMatchingCount(int current, int max)
    {
        matchingCountText.text = $"{current}/{max}";

        if (current >= max)
        {
            statusText.text = "매칭 완료!";
        }
        else
        {
            statusText.text = "매칭 대기중...";
        }
    }

    public void HideMatchingPopup()
    {
        matchingPopupPanel.SetActive(false);
    }
}