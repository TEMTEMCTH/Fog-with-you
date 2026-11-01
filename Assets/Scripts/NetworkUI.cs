using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NetworkUI : MonoBehaviour
{
    public Button hostButton;
    public Button clientButton;
    public TMP_InputField ipInput;
    public GameObject menuCanvas; // Ссылка на весь Canvas

    void Start()
    {
        if (ipInput != null)
        {
            ipInput.text = "127.0.0.1";
        }

        hostButton.onClick.AddListener(() => {
            NetworkManager.Singleton.StartHost();
            HideUI();
        });

        clientButton.onClick.AddListener(() => {
            string ip = ipInput.text;
            if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";

            NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().ConnectionData.Address = ip;
            NetworkManager.Singleton.StartClient();
            HideUI();
        });
    }

    void HideUI()
    {
        // Прячем весь Canvas целиком
        if (menuCanvas != null) menuCanvas.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}