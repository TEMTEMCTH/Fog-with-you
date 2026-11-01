using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Меню паузы (Escape): «Продолжить» и «Выйти в главное меню».
/// Повесьте на пустой объект в ИГРОВОЙ сцене. Интерфейс строится кодом.
/// Игра не останавливается (это кооператив) — просто освобождается курсор
/// и блокируется управление своим персонажем.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    /// <summary>Открыто ли меню паузы (читает PlayerController).</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>Сообщение, которое покажет главное меню после выхода из игры.</summary>
    public static string MenuMessage;

    [SerializeField] private string menuSceneName = "MainMenu";

    private Font font;
    private GameObject panel;
    private Text hintText;
    private bool leaving;

    private readonly Color buttonColor = new Color(0.16f, 0.2f, 0.27f);
    private readonly Color textColor = new Color(0.88f, 0.92f, 0.96f);

    private void Start()
    {
        IsOpen = false;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 28);

        EnsureEventSystem();
        BuildUI();
        panel.SetActive(false);

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleDisconnect;
    }

    private void OnDestroy()
    {
        IsOpen = false;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleDisconnect;
    }

    private void Update()
    {
        if (leaving) return;
        if (EscapePressed())
            SetOpen(!IsOpen);
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;
        panel.SetActive(open);

        var nm = NetworkManager.Singleton;
        hintText.text = (open && nm != null && nm.IsHost)
            ? "Вы хост: если выйдете, игра закончится для всех."
            : "";

        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    // ───────────── Выход в главное меню ─────────────

    private void OnResumeClicked() => SetOpen(false);

    private void OnLeaveClicked()
    {
        MenuMessage = null;
        StartCoroutine(LeaveRoutine());
    }

    // Клиента выкинуло (например, хост закрыл игру) — возвращаемся в меню
    private void HandleDisconnect(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsServer || leaving) return;

        MenuMessage = string.IsNullOrEmpty(nm.DisconnectReason)
            ? "Соединение с хостом потеряно."
            : "Отключено: " + nm.DisconnectReason;
        StartCoroutine(LeaveRoutine());
    }

    private IEnumerator LeaveRoutine()
    {
        leaving = true;
        IsOpen = false;

        // NetworkManager переживает смену сцен, поэтому удаляем его:
        // в главном меню появится свежий, без остатков прошлой игры.
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening) nm.Shutdown();
            Destroy(nm.gameObject);
        }

        yield return null;
        SceneManager.LoadScene(menuSceneName);
    }

    // ───────────── Ввод ─────────────

    private bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // ───────────── Интерфейс ─────────────

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
        var canvasGo = new GameObject("PauseCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Панель на весь экран с затемнением
        var rt = CreateRect("Panel", canvasGo.transform, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
        panel = rt.gameObject;

        var size = new Vector2(460, 80);
        CreateText("Title", rt, "Пауза", 90, new Vector2(0, 200), new Vector2(900, 120),
            TextAnchor.MiddleCenter, textColor);
        CreateButton("ResumeButton", rt, "Продолжить", new Vector2(0, 50), size, OnResumeClicked);
        CreateButton("LeaveButton", rt, "Выйти в главное меню", new Vector2(0, -60), size, OnLeaveClicked);
        hintText = CreateText("Hint", rt, "", 30, new Vector2(0, -170), new Vector2(1400, 50),
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
        colors.selectedColor = colors.highlightedColor;
        btn.colors = colors;
        img.color = Color.white;

        CreateText("Label", rt, label, 34, Vector2.zero, size, TextAnchor.MiddleCenter, textColor);
        btn.onClick.AddListener(onClick);
        return btn;
    }
}
