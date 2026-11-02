using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Фонарик как предмет: светит только когда он у игрока В РУКЕ (выбран в инвентаре) и включён клавишей F.
/// В начале игры фонарик всегда выключен. Если убрать его из рук, он гаснет.
/// У владельца свет идёт из камеры, остальные игроки видят луч у вас сбоку.
/// Вешается на префаб игрока (вместе с PlayerInventory).
/// </summary>
public class PlayerFlashlight : NetworkBehaviour
{
    /// <summary>Item Id предмета-фонарика (должен совпадать с PickupItem на префабе предмета).</summary>
    public const string ItemId = "flashlight";

    [Header("Свет")]
    [SerializeField] private float intensity = 30f;
    [SerializeField] private float range = 28f;
    [SerializeField] private float spotAngle = 60f;
    [SerializeField] private Color color = new Color(1f, 0.95f, 0.8f);

    // Включён ли фонарик — видят все, переключает владелец. В начале игры выключен.
    private NetworkVariable<bool> isOn;

    private Camera cam;
    private Light flashlight;
    private PlayerInventory inventory;

    private void Awake()
    {
        isOn = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    }

    public override void OnNetworkSpawn()
    {
        cam = GetComponentInChildren<Camera>(true);
        inventory = GetComponent<PlayerInventory>();
        BuildLight();
        flashlight.enabled = false;
    }

    public override void OnNetworkDespawn()
    {
        if (flashlight != null) Destroy(flashlight.gameObject);
    }

    private void Update()
    {
        if (flashlight == null) return;

        bool holding = inventory != null && inventory.HeldItemId == ItemId;

        if (IsOwner && !PauseMenu.IsOpen)
        {
            if (!holding && isOn.Value)
                isOn.Value = false;                  // убрали фонарик из рук — он гаснет
            else if (holding && TogglePressed())
                isOn.Value = !isOn.Value;
        }

        // Свет есть только если фонарик в руке и включён (так видят все игроки)
        flashlight.enabled = isOn.Value && holding;
    }

    private void BuildLight()
    {
        bool firstPerson = IsOwner && cam != null;

        var go = new GameObject("Flashlight");
        go.transform.SetParent(firstPerson ? cam.transform : transform, false);
        go.transform.localPosition = firstPerson ? new Vector3(0.15f, -0.1f, 0.2f) : new Vector3(0.5f, 0.3f, 0.3f);
        go.transform.localRotation = Quaternion.identity;

        flashlight = go.AddComponent<Light>();
        flashlight.type = LightType.Spot;
        flashlight.color = color;
        flashlight.intensity = intensity;
        flashlight.range = range;
        flashlight.spotAngle = spotAngle;
        flashlight.innerSpotAngle = spotAngle * 0.6f;
        flashlight.shadows = LightShadows.None;
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool TogglePressed() => Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
    private bool TogglePressed() => Input.GetKeyDown(KeyCode.F);
#endif
}
