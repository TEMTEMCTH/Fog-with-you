using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Фонарик (F — включить/выключить). Свет создаётся кодом, настраивать ничего не нужно.
/// У владельца свет идёт из камеры (светит туда, куда вы смотрите),
/// остальные игроки видят луч у вас сбоку, направленный вперёд.
/// Вешается на префаб игрока.
/// </summary>
public class PlayerFlashlight : NetworkBehaviour
{
    [Header("Свет")]
    [SerializeField] private float intensity = 30f;
    [SerializeField] private float range = 28f;
    [SerializeField] private float spotAngle = 60f;
    [SerializeField] private Color color = new Color(1f, 0.95f, 0.8f);

    // Включён ли фонарик — видят все, переключает владелец
    private NetworkVariable<bool> isOn;

    private Camera cam;
    private Light flashlight;

    private void Awake()
    {
        isOn = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    }

    public override void OnNetworkSpawn()
    {
        cam = GetComponentInChildren<Camera>(true);
        BuildLight();

        isOn.OnValueChanged += OnToggled;
        ApplyState();

        if (!IsOwner) enabled = false;   // переключать может только владелец
    }

    public override void OnNetworkDespawn()
    {
        isOn.OnValueChanged -= OnToggled;
        if (flashlight != null) Destroy(flashlight.gameObject);
    }

    private void Update()
    {
        if (PauseMenu.IsOpen) return;
        if (TogglePressed())
            isOn.Value = !isOn.Value;
    }

    private void OnToggled(bool oldValue, bool newValue) => ApplyState();

    private void ApplyState()
    {
        if (flashlight != null) flashlight.enabled = isOn.Value;
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
