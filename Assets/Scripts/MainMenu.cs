using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Главное меню: онлайн-игра через Relay (код игры), локальная игра, выход.
/// Интерфейс строится кодом — достаточно повесить скрипт на пустой объект в сцене MainMenu.
/// Требует в той же сцене объект NetworkManager (с UnityTransport и PlayerSpawner).
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Настройки")]
    [SerializeField] private string gameTitle = "Fog with you";
    [SerializeField] private string gameSceneName = "SampleScene";
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private ushort port = 7777;   // только для локальной игры

    private Font font;
    private GameObject mainPanel;
    private Button cancelButton;
    private Text statusText;
    private InputField codeField;
    private bool cancelRequested;

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

    // ───────────── Онлайн-игра (Relay) ─────────────

    private async void OnHostOnlineClicked()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("В сцене нет NetworkManager!"); return; }

        SetBusy(true, "Подключение к онлайн-сервисам...", false);
        try
        {
            await EnsureSignedIn();

            SetStatus("Создание игры...");
            nm.OnServerStarted += HandleServerStarted;   // как только хост запустится — грузим игровую сцену

            // WithRelayNetwork() сам выделяет Relay и запускает NetworkManager как хост
            var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
            ISession session = await MultiplayerService.Instance.CreateSessionAsync(options);

            PauseMenu.CurrentSession = session;
            Debug.Log($"Игра создана. Код для друзей: {session.Code}");
        }
        catch (System.Exception e)
        {
            nm.OnServerStarted -= HandleServerStarted;
            Debug.LogException(e);
            if (nm.IsListening) nm.Shutdown();
            SetBusy(false, "Не удалось создать игру: " + e.Message);
        }
    }

    private async void JoinOnline(string code)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("В сцене нет NetworkManager!"); return; }

        cancelRequested = false;
        SetBusy(true, "Подключение к онлайн-сервисам...");
        nm.OnClientDisconnectCallback += HandleClientDisconnect;
        try
        {
            await EnsureSignedIn();
            if (cancelRequested) return;

            SetStatus($"Вход в игру {code}...");
            ISession session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            PauseMenu.CurrentSession = session;

            if (cancelRequested)
            {
                PauseMenu.CurrentSession = null;
                await session.LeaveAsync();
            }
            // Дальше игровая сцена придёт от хоста сама
        }
        catch (System.Exception e)
        {
            nm.OnClientDisconnectCallback -= HandleClientDisconnect;
            if (cancelRequested) return;
            Debug.LogException(e);
            if (nm.IsListening) nm.Shutdown();
            SetBusy(false, "Не удалось войти: " + e.Message);
        }
    }

    private async Task EnsureSignedIn()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
#if UNITY_EDITOR
            // Чтобы окна Multiplayer Play Mode были РАЗНЫМИ игроками
            AuthenticationService.Instance.SwitchProfile("editor" + Random.Range(0, 1000000));
#endif
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    // ───────────── Локальная игра (без интернета) ─────────────

    private void OnHostLocalClicked()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("В сцене нет NetworkManager!"); return; }

        // 0.0.0.0 — принимаем подключения и из локальной сети
        nm.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", port, "0.0.0.0");

        SetBusy(true, "Запуск игры...", false);
        nm.OnServerStarted += HandleServerStarted;
        if (!nm.StartHost())
        {
            nm.OnServerStarted -= HandleServerStarted;
            SetBusy(false, "Не удалось запустить хост.");
        }
    }

    private void JoinLocal(string ip)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) { SetStatus("В сцене нет NetworkManager!"); return; }

        nm.GetComponent<UnityTransport>().SetConnectionData(ip, port);

        SetBusy(true, $"Подключение к {ip}:{port}...");
        nm.OnClientDisconnectCallback += HandleClientDisconnect;
        if (!nm.StartClient())
        {
            nm.OnClientDisconnectCallback -= HandleClientDisconnect;
            SetBusy(false, "Не удалось запустить клиент.");
        }
    }

    // ───────────── Общие обработчики ─────────────

    private void OnJoinClicked()
    {
        string input = codeField.text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            SetStatus("Введите код игры (или IP-адрес для локальной игры).");
            return;
        }

        // Есть точка или localhost — это IP (локальная игра), иначе — код онлайн-игры
        if (input.Contains(".") || input.ToLowerInvariant() == "localhost")
            JoinLocal(input);
        else
            JoinOnline(input.ToUpperInvariant());
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

    private void HandleClientDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        nm.OnClientDisconnectCallback -= HandleClientDisconnect;

        string reason = nm.DisconnectReason;
        if (nm.IsListening) nm.Shutdown();

        var session = PauseMenu.CurrentSession;
        PauseMenu.CurrentSession = null;
        if (session != null) _ = session.LeaveAsync();

        SetBusy(false, string.IsNullOrEmpty(reason)
            ? "Не удалось подключиться. Проверьте код/IP и что хост уже запущен."
            : "Отключено: " + reason);
    }

    private void OnCancelClicked()
    {
        cancelRequested = true;

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted -= HandleServerStarted;
            nm.OnClientDisconnectCallback -= HandleClientDisconnect;
            if (nm.IsListening) nm.Shutdown();
        }

        var session = PauseMenu.CurrentSession;
        PauseMenu.CurrentSession = null;
        if (session != null) _ = session.LeaveAsync();

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

    private void SetBusy(bool busy, string message, bool allowCancel = true)
    {
        if (mainPanel == null) return;   // меню уже выгружено
        mainPanel.SetActive(!busy);
        cancelButton.gameObject.SetActive(busy && allowCancel);
        SetStatus(message);
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

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

        var bg = CreateRect("Background", root, Vector2.zero, Vector2.zero);
        bg.anchorMin = Vector2.zero;
        bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        bg.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.11f);

        CreateText("Title", root, gameTitle, 110, new Vector2(0, 300), new Vector2(1400, 150),
            TextAnchor.MiddleCenter, textColor);
        CreateText("Subtitle", root, "Кооперативная игра", 36, new Vector2(0, 215), new Vector2(1000, 60),
            TextAnchor.MiddleCenter, new Color(0.55f, 0.62f, 0.7f));

        mainPanel = CreateRect("MainPanel", root, Vector2.zero, Vector2.zero).gameObject;
        Transform p = mainPanel.transform;
        var size = new Vector2(500, 70);

        CreateButton("HostOnlineButton", p, "Создать онлайн-игру", new Vector2(0, 110), size, OnHostOnlineClicked);
        CreateButton("HostLocalButton", p, "Локальная игра (без интернета)", new Vector2(0, 20), size, OnHostLocalClicked, 28);
        codeField = CreateInput("CodeField", p, "", "Код игры или IP-адрес", new Vector2(0, -70), size);
        CreateButton("JoinButton", p, "Подключиться", new Vector2(0, -160), size, OnJoinClicked);
        CreateButton("QuitButton", p, "Выход", new Vector2(0, -250), size, OnQuitClicked);

        cancelButton = CreateButton("CancelButton", root, "Отмена", new Vector2(0, -60), size, OnCancelClicked);
        cancelButton.gameObject.SetActive(false);

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

    private Button CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size,
        UnityAction onClick, int fontSize = 34)
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

        CreateText("Label", rt, label, fontSize, Vector2.zero, size, TextAnchor.MiddleCenter, textColor);
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
        var placeholder = CreateText("Placeholder", rt, placeholderText, 30, Vector2.zero, inner,
            TextAnchor.MiddleCenter, new Color(0.45f, 0.5f, 0.56f));
        placeholder.fontStyle = FontStyle.Italic;

        input.textComponent = text;
        input.placeholder = placeholder;
        input.characterLimit = 40;
        input.text = defaultText;
        return input;
    }
}
