using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Выносливость игрока. Тратится на бег (постепенно), прыжок и выбрасывание предмета (разово).
/// После траты через небольшую паузу восстанавливается. Если выносливость кончилась,
/// игрок «выдохся» и не может бежать, пока не восстановит часть сил.
/// Шкала внизу слева появляется, только когда выносливость не полная.
/// Вешается на префаб игрока.
/// </summary>
public class PlayerStamina : NetworkBehaviour
{
    [Header("Выносливость")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float regenPerSecond = 18f;
    [SerializeField] private float regenDelay = 1f;          // пауза после траты, прежде чем начнёт восстанавливаться
    [SerializeField] private float recoverThreshold = 25f;   // сколько нужно набрать после «выдоха», чтобы снова бежать

    [Header("Стоимость действий")]
    [SerializeField] private float sprintDrainPerSecond = 20f;
    [SerializeField] private float jumpCost = 15f;
    [SerializeField] private float dropCost = 10f;

    public float Current { get; private set; }
    public float Max => maxStamina;
    public bool Exhausted { get; private set; }
    public bool CanSprint => !Exhausted && Current > 0f;
    public float SprintDrainPerSecond => sprintDrainPerSecond;
    public float JumpCost => jumpCost;
    public float DropCost => dropCost;

    private float lastUseTime = -100f;

    private GameObject uiRoot;
    private CanvasGroup group;
    private RectTransform fill;
    private Image fillImage;

    private const float BarWidth = 354f;
    private const float BarHeight = 14f;

    private void Awake()
    {
        Current = maxStamina;
    }

    public override void OnNetworkSpawn()
    {
        Current = maxStamina;

        if (!IsOwner)
        {
            enabled = false;   // у чужих игроков выносливость не считаем
            return;
        }
        BuildUI();
    }

    public override void OnNetworkDespawn()
    {
        if (uiRoot != null) Destroy(uiRoot);
    }

    /// <summary>Потратить выносливость разово (прыжок, выбрасывание). Вернёт false, если сил не хватает.</summary>
    public bool TrySpend(float amount)
    {
        if (Exhausted || Current < amount) return false;
        Current -= amount;
        lastUseTime = Time.time;
        if (Current <= 0f) { Current = 0f; Exhausted = true; }
        return true;
    }

    /// <summary>Потратить выносливость постепенно (бег). Вызывайте каждый кадр с amount = скорость * deltaTime.</summary>
    public void Drain(float amount)
    {
        Current = Mathf.Max(0f, Current - amount);
        lastUseTime = Time.time;
        if (Current <= 0f) Exhausted = true;
    }

    private void Update()
    {
        if (Time.time - lastUseTime >= regenDelay)
            Current = Mathf.Min(maxStamina, Current + regenPerSecond * Time.deltaTime);

        if (Exhausted && Current >= recoverThreshold)
            Exhausted = false;

        UpdateUI();
    }

    // ───────────── Интерфейс ─────────────

    private void BuildUI()
    {
        uiRoot = new GameObject("StaminaUI");
        var canvas = uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = uiRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        group = uiRoot.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        // Фон шкалы
        var bgGo = new GameObject("Background", typeof(RectTransform));
        var bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.SetParent(uiRoot.transform, false);
        bgRt.anchorMin = bgRt.anchorMax = Vector2.zero;
        bgRt.pivot = Vector2.zero;
        bgRt.anchoredPosition = new Vector2(40f, 40f);
        bgRt.sizeDelta = new Vector2(BarWidth + 6f, BarHeight + 6f);
        var bg = bgGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;

        // Заполнение
        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fill = fillGo.GetComponent<RectTransform>();
        fill.SetParent(bgRt, false);
        fill.anchorMin = fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = new Vector2(3f, 0f);
        fill.sizeDelta = new Vector2(BarWidth, BarHeight);
        fillImage = fillGo.AddComponent<Image>();
        fillImage.raycastTarget = false;
    }

    private void UpdateUI()
    {
        if (group == null) return;

        float ratio = Current / maxStamina;
        fill.sizeDelta = new Vector2(BarWidth * ratio, BarHeight);
        fillImage.color = Exhausted ? new Color(0.9f, 0.35f, 0.3f) : new Color(0.6f, 0.85f, 0.65f);

        // Показываем шкалу, только пока она не полная
        float targetAlpha = (ratio < 0.995f || Exhausted) ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.deltaTime * 4f);
    }
}
