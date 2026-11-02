using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Планшет: пока он в руке, на экране появляется карта-радар, где виден ОДИН случайный другой игрок.
/// Верх карты — направление вашего взгляда. Новый случайный игрок выбирается, когда вы берёте планшет в руку,
/// и по ЛКМ (не чаще, чем раз в 15 секунд).
/// Вешается на префаб игрока.
/// </summary>
public class PlayerTablet : NetworkBehaviour
{
    /// <summary>Item Id предмета-планшета (должен совпадать с PickupItem на префабе предмета).</summary>
    public const string ItemId = "tablet";

    [SerializeField] private float mapRadius = 150f;       // сколько метров помещается от центра до края карты
    [SerializeField] private float rerollCooldown = 15f;

    private const float MapSize = 300f;

    private PlayerInventory inventory;
    private PlayerController target;
    private bool wasHolding;
    private float nextRerollTime;

    private GameObject uiRoot;
    private GameObject panel;
    private RectTransform targetDot;
    private Text infoText;
    private Font font;

    public override void OnNetworkSpawn()
    {
        inventory = GetComponent<PlayerInventory>();
        if (!IsOwner) { enabled = false; return; }
        BuildUI();
    }

    public override void OnNetworkDespawn()
    {
        if (uiRoot != null) Destroy(uiRoot);
    }

    private void Update()
    {
        if (inventory == null || panel == null) return;

        bool holding = inventory.HeldItemId == ItemId;
        panel.SetActive(holding);
        if (!holding) { wasHolding = false; return; }

        if (!wasHolding)
        {
            wasHolding = true;
            PickTarget();
        }

        if (!inventory.InputBlocked && LeftPressed())
        {
            if (Time.time >= nextRerollTime)
            {
                nextRerollTime = Time.time + rerollCooldown;
                PickTarget();
            }
            else
            {
                inventory.ShowMessage($"Планшет обновится через {Mathf.CeilToInt(nextRerollTime - Time.time)} с");
            }
        }

        if (target == null) PickTarget();   // выбранный игрок вышел из игры (или раньше никого не было)
        UpdateMap();
    }

    private void PickTarget()
    {
        var candidates = new System.Collections.Generic.List<PlayerController>();
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (!pc.IsOwner) candidates.Add(pc);

        if (candidates.Count == 0) { target = null; return; }

        // Стараемся выбрать другого игрока, чем в прошлый раз
        if (candidates.Count > 1 && target != null) candidates.Remove(target);
        target = candidates[Random.Range(0, candidates.Count)];
    }

    private void UpdateMap()
    {
        if (target == null)
        {
            targetDot.gameObject.SetActive(false);
            infoText.text = "Планшет · других игроков нет";
            return;
        }

        Vector3 delta = target.transform.position - transform.position;
        delta.y = 0f;

        // Поворачиваем так, чтобы «вверх» на карте было там, куда вы смотрите
        Vector3 local = Quaternion.Euler(0f, -transform.eulerAngles.y, 0f) * delta;
        Vector2 mapPos = new Vector2(local.x, local.z) / mapRadius * (MapSize / 2f);
        float maxLength = MapSize / 2f - 10f;
        if (mapPos.magnitude > maxLength) mapPos = mapPos.normalized * maxLength;   // за краем — у края

        targetDot.gameObject.SetActive(true);
        targetDot.anchoredPosition = mapPos;
        infoText.text = $"Планшет · Игрок {target.OwnerClientId} · {delta.magnitude:0} м";
    }

    // ───────────── Интерфейс ─────────────

    private void BuildUI()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 24);

        uiRoot = new GameObject("TabletUI");
        var canvas = uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;
        var scaler = uiRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Панель в левом нижнем углу (над индикатором микрофона)
        var panelRt = CreateRect("Panel", uiRoot.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(40f, 150f), new Vector2(MapSize, MapSize + 40f));
        panel = panelRt.gameObject;

        var map = CreateRect("Map", panelRt, new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 40f), new Vector2(MapSize, MapSize));
        var mapImage = map.gameObject.AddComponent<Image>();
        mapImage.color = new Color(0.04f, 0.1f, 0.07f, 0.92f);
        mapImage.raycastTarget = false;

        CreateCentered("LineH", map, new Vector2(0f, 0f), new Vector2(MapSize - 20f, 2f), new Color(0.2f, 0.5f, 0.3f, 0.6f));
        CreateCentered("LineV", map, new Vector2(0f, 0f), new Vector2(2f, MapSize - 20f), new Color(0.2f, 0.5f, 0.3f, 0.6f));
        CreateCentered("You", map, new Vector2(0f, 0f), new Vector2(12f, 12f), Color.white);
        targetDot = CreateCentered("Target", map, new Vector2(0f, 0f), new Vector2(16f, 16f), new Color(1f, 0.3f, 0.25f));

        var info = new GameObject("Info", typeof(RectTransform));
        var infoRt = info.GetComponent<RectTransform>();
        infoRt.SetParent(panelRt, false);
        infoRt.anchorMin = infoRt.anchorMax = infoRt.pivot = new Vector2(0f, 0f);
        infoRt.anchoredPosition = Vector2.zero;
        infoRt.sizeDelta = new Vector2(MapSize, 36f);
        infoText = info.AddComponent<Text>();
        infoText.font = font;
        infoText.fontSize = 22;
        infoText.alignment = TextAnchor.MiddleLeft;
        infoText.color = new Color(0.7f, 0.95f, 0.8f);
        infoText.raycastTarget = false;
        info.AddComponent<Shadow>();

        panel.SetActive(false);
    }

    private RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
        Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    private RectTransform CreateCentered(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var rt = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rt;
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool LeftPressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    private bool LeftPressed() => Input.GetMouseButtonDown(0);
#endif
}
