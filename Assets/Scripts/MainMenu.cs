using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Главное меню: создать игру (Host), подключиться по IP (Client), выйти.
/// Интерфейс строится кодом — достаточно повесить скрипт на пустой объект в сцене MainMenu.
/// Требует в той же сцене объект NetworkManager (с UnityTransport и PlayerSpawner).
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Настройки")]
    [SerializeField] private string gameTitle = "Fog with you";
    [SerializeField] private string gameSceneName = "SampleScene";
    [SerializeField] private ushort port = 7777;

    private Font font;
    private GameObject mainPanel;
    private Button cancelButton;
    private Text statusText;
    private InputField ipField;

    private readonly Color buttonColor = new Color(0.16f, 0.2f, 0.27f);
    private readonly Color textColor = new Color(0.88f, 0.92f, 0.96f);

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 28);

        EnsureEventSystem();
        BuildUI();

        // Сообщение после выхода из игры (например, хост закрыл игру)
        if (!string.IsNullOrEmpty(PauseMenu.MenuMessage))
        {
            SetStatus(PauseMenu.MenuMessage);
            PauseMenu.MenuMessage = null;
        }
    }

    private void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnServerStarted -= HandleServerStarted;
        nm.OnClientDisconnectCallback -= HandleClientDisconnect;
    }

    // ───────────── Кнопки ─────────────

    private void OnHostClicked()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("В сцене нет NetworkManager!"); return; }

        var transport = nm.GetComponent<UnityTransport>();
        // 0.0.0.0 — принимаем подключения не только с этого компьютера, но и из локальной сети
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");

        SetBusy(true, "Запуск игры...");
        nm.OnServerStarted += HandleServerStarted;
        if (!nm.StartHost())
        {
            nm.OnServerStarted -= HandleServerStarted;
            SetBusy(false, "Не удалось запустить хост.");
        }
    }

    private void HandleServerStarted()
    {
        var nm = NetworkManager.Singleton;
        nm.OnServerStarted -= HandleServerStarted;

        var status = nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            nm.Shutdown();
            SetBusy(false, $"Не удалось загрузить сцену «{gameSceneName}». Добавьте её в Build Profiles.");
        }
    }

    private void OnJoinClicked()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("В сцене нет NetworkManager!"); return; }

        string ip = ipField.text.Trim();
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";

        nm.GetComponent<UnityTransport>().SetConnectionData(ip, port);

        SetBusy(true, $"Подключение к {ip}:{port}...");
        nm.OnClientDisconnectCallback += HandleClientDisconnect;
        if (!nm.StartClient())
        {
            nm.OnClientDisconnectCallback -= HandleClientDisconnect;
            SetBusy(false, "Не удалось запустить клиент.");
        }
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        nm.OnClientDisconnectCallback -= HandleClientDisconnect;

        string reason = nm.DisconnectReason;
        if (nm.IsListening) nm.Shutdown();

        SetBusy(false, string.IsNullOrEmpty(reason)
            ? "Не удалось подключиться. Проверьте IP-адрес и что хост уже запущен."
            : "Отключено: " + reason);
    }

    private void OnCancelClicked()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted -= HandleServerStarted;
            nm.OnClientDisconnectCallback -= HandleClientDisconnect;
            if (nm.IsListening) nm.Shutdown();
        }
        SetBusy(false, "");
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetBusy(bool busy, string message)
    {
        mainPanel.SetActive(!busy);
        cancelButton.gameObject.SetActive(busy);
        SetStatus(message);
    }

    private void SetStatus(string message) => statusText.text = message;

    // ───────────── Построение интерфейса ─────────────

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        go.AddComponent<InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    private void BuildUI()
    {
        // Canvas
        var canvasGo = new GameObject("MenuCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        Transform root = canvasGo.transform;

        // Фон на весь экран
        var bg = CreateRect("Background", root, Vector2.zero, Vector2.zero);
        bg.anchorMin = Vector2.zero;
        bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        bg.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.11f);

        // Заголовки
        CreateText("Title", root, gameTitle, 110, new Vector2(0, 300), new Vector2(1400, 150),
            TextAnchor.MiddleCenter, textColor);
        CreateText("Subtitle", root, "Кооперативная игра", 36, new Vector2(0, 215), new Vector2(1000, 60),
            TextAnchor.MiddleCenter, new Color(0.55f, 0.62f, 0.7f));

        // Основная панель с кнопками
        mainPanel = CreateRect("MainPanel", root, Vector2.zero, Vector2.zero).gameObject;
        Transform p = mainPanel.transform;
        var size = new Vector2(460, 80);

        CreateButton("HostButton", p, "Создать игру (Host)", new Vector2(0, 60), size, OnHostClicked);
        ipField = CreateInput("IpField", p, "127.0.0.1", "IP адрес хоста", new Vector2(0, -45), new Vector2(460, 70));
        CreateButton("JoinButton", p, "Подключиться", new Vector2(0, -140), size, OnJoinClicked);
        CreateButton("QuitButton", p, "Выход", new Vector2(0, -250), size, OnQuitClicked);

        // Кнопка отмены (видна только во время подключения)
        cancelButton = CreateButton("CancelButton", root, "Отмена", new Vector2(0, -60), size, OnCancelClicked);
        cancelButton.gameObject.SetActive(false);

        // Строка состояния
        statusText = CreateText("Status", root, "", 32, new Vector2(0, -370), new Vector2(1500, 60),
            TextAnchor.MiddleCenter, new Color(0.95f, 0.75f, 0.45f));
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private Text CreateText(string name, Transform parent, string content, int fontSize,
        Vector2 pos, Vector2 size, TextAnchor anchor, Color color)
    {
        var rt = CreateRect(name, parent, pos, size);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private Button CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, UnityAction onClick)
    {
        var rt = CreateRect(name, parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        var colors = btn.colors;
        colors.normalColor = buttonColor;
        colors.highlightedColor = new Color(0.26f, 0.34f, 0.46f);
        colors.pressedColor = new Color(0.12f, 0.15f, 0.2f);
        colors.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.6f);
        colors.selectedColor = colors.highlightedColor;
        btn.colors = colors;
        img.color = Color.white;

        CreateText("Label", rt, label, 34, Vector2.zero, size, TextAnchor.MiddleCenter, textColor);
        btn.onClick.AddListener(onClick);
        return btn;
    }

    private InputField CreateInput(string name, Transform parent, string defaultText, string placeholderText,
        Vector2 pos, Vector2 size)
    {
        var rt = CreateRect(name, parent, pos, size);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.12f, 0.16f);
        var input = rt.gameObject.AddComponent<InputField>();
        input.targetGraphic = bg;

        var inner = new Vector2(size.x - 30, size.y);
        var text = CreateText("Text", rt, "", 32, Vector2.zero, inner, TextAnchor.MiddleCenter, textColor);
        text.supportRichText = false;
        var placeholder = CreateText("Placeholder", rt, placeholderText, 32, Vector2.zero, inner,
            TextAnchor.MiddleCenter, new Color(0.45f, 0.5f, 0.56f));
        placeholder.fontStyle = FontStyle.Italic;

        input.textComponent = text;
        input.placeholder = placeholder;
        input.text = defaultText;
        return input;
    }
}
