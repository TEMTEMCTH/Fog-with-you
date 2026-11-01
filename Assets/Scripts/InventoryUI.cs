using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Интерфейс инвентаря: панель слотов внизу экрана, подсказка «[E] Поднять», сообщения.
/// Создаётся из PlayerInventory кодом — на сцену вешать ничего не нужно.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    private Font font;
    private Image[] slotBackgrounds;
    private Text[] slotNames;
    private Text promptText;
    private Text messageText;
    private float messageHideTime;

    private static readonly Color SlotColor = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color SlotSelectedColor = new Color(0.25f, 0.45f, 0.7f, 0.9f);

    public static InventoryUI Create(int slotCount)
    {
        var go = new GameObject("InventoryUI");
        var ui = go.AddComponent<InventoryUI>();
        ui.Build(slotCount);
        return ui;
    }

    public void SetSlot(int index, string label, bool selected)
    {
        slotNames[index].text = label;
        slotBackgrounds[index].color = selected ? SlotSelectedColor : SlotColor;
    }

    public void SetPrompt(string text) => promptText.text = text;

    public void ShowMessage(string text, float seconds = 2f)
    {
        messageText.text = text;
        messageHideTime = Time.time + seconds;
    }

    private void Update()
    {
        if (messageText.text.Length > 0 && Time.time >= messageHideTime)
            messageText.text = "";
    }

    // ───────────── Построение интерфейса ─────────────

    private void Build(int slotCount)
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 24);

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;   // ниже меню паузы (100)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        slotBackgrounds = new Image[slotCount];
        slotNames = new Text[slotCount];

        const float slotW = 170f, slotH = 100f, gap = 12f;
        float totalW = slotCount * slotW + (slotCount - 1) * gap;

        for (int i = 0; i < slotCount; i++)
        {
            float x = -totalW / 2f + slotW / 2f + i * (slotW + gap);
            var rt = CreateRect("Slot" + i, transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(x, 30f), new Vector2(slotW, slotH));
            slotBackgrounds[i] = rt.gameObject.AddComponent<Image>();
            slotBackgrounds[i].color = SlotColor;

            var num = CreateText("Number", rt, (i + 1).ToString(), 22, TextAnchor.UpperLeft,
                new Color(0.7f, 0.75f, 0.8f));
            Stretch(num.rectTransform, 6f);

            var label = CreateText("Name", rt, "", 24, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform, 6f);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 28;
            slotNames[i] = label;
        }

        // Подсказка «[E] Поднять: ...»
        promptText = CreateText("Prompt", transform, "", 36, TextAnchor.MiddleCenter, Color.white);
        SetupAnchored(promptText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1200f, 50f));
        promptText.gameObject.AddComponent<Shadow>();

        // Сообщения («Инвентарь полон»)
        messageText = CreateText("Message", transform, "", 34, TextAnchor.MiddleCenter, new Color(0.95f, 0.75f, 0.45f));
        SetupAnchored(messageText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 205f), new Vector2(1200f, 50f));
        messageText.gameObject.AddComponent<Shadow>();

        // Подсказка по управлению
        var hint = CreateText("Hint", transform, "E — поднять   Q — выбросить   1–" + slotCount + " — взять в руку",
            24, TextAnchor.UpperLeft, new Color(0.8f, 0.85f, 0.9f, 0.8f));
        SetupAnchored(hint.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(1200f, 40f));
        hint.gameObject.AddComponent<Shadow>();
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private Text CreateText(string name, Transform parent, string content, int fontSize,
        TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.GetComponent<RectTransform>().SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = fontSize;
        t.alignment = alignment;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    private void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    private void SetupAnchored(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
