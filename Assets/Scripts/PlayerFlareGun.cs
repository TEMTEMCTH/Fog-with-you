using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Сигнальный пистолет: пока он у игрока в руке, левая кнопка мыши выпускает ракету,
/// которая освещает лес красным светом (её видят все игроки). Между выстрелами — перезарядка.
/// Вешается на префаб игрока (вместе с PlayerInventory).
/// </summary>
public class PlayerFlareGun : NetworkBehaviour
{
    /// <summary>Item Id предмета-пистолета (должен совпадать с PickupItem на префабе предмета).</summary>
    public const string ItemId = "flaregun";

    [Header("Выстрел")]
    [SerializeField] private float cooldown = 30f;        // секунд между выстрелами
    [SerializeField] private float flareSpeed = 22f;

    [Header("Ракета")]
    [SerializeField] private float flareLifetime = 25f;
    [SerializeField] private float lightRange = 50f;
    [SerializeField] private float lightIntensity = 100f;
    [SerializeField] private Color flareColor = new Color(1f, 0.25f, 0.15f);

    private PlayerInventory inventory;
    private Camera cam;
    private float nextShotTime;          // у владельца
    private float serverNextShotTime;    // на сервере

    public override void OnNetworkSpawn()
    {
        inventory = GetComponent<PlayerInventory>();
        cam = GetComponentInChildren<Camera>(true);
        if (!IsOwner) enabled = false;   // стрелять может только владелец (RPC на сервере работают и так)
    }

    private void Update()
    {
        if (inventory == null || cam == null) return;
        if (inventory.InputBlocked) return;
        if (inventory.HeldItemId != ItemId) return;
        if (!FirePressed()) return;

        if (Time.time < nextShotTime)
        {
            inventory.ShowMessage($"Пистолет перезаряжается: {Mathf.CeilToInt(nextShotTime - Time.time)} с");
            return;
        }

        nextShotTime = Time.time + cooldown;
        Transform t = cam.transform;
        FireRpc(t.position + t.forward * 0.6f, t.forward);
    }

    // Владелец -> сервер: проверяем, что пистолет действительно в руке и он перезарядился
    [Rpc(SendTo.Server)]
    private void FireRpc(Vector3 origin, Vector3 direction)
    {
        if (inventory == null || inventory.HeldItemId != ItemId) return;
        if (Time.time < serverNextShotTime) return;
        if (direction.sqrMagnitude < 0.01f) return;

        serverNextShotTime = Time.time + cooldown * 0.9f;   // небольшой допуск на сетевую задержку
        LaunchRpc(origin, direction.normalized * flareSpeed);
    }

    // Сервер -> все игроки: каждый создаёт у себя ракету
    [Rpc(SendTo.Everyone)]
    private void LaunchRpc(Vector3 origin, Vector3 velocity)
    {
        FlareProjectile.Launch(origin, velocity, flareLifetime, lightIntensity, lightRange, flareColor);
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool FirePressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    private bool FirePressed() => Input.GetMouseButtonDown(0);
#endif
}