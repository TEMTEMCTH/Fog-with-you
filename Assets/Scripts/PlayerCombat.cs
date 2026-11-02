using System.Collections;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Бой с монстром предметами из рук:
///   ЛКМ — ударить (палка, копьё) или бросить (камень);
///   ПКМ — бросить копьё.
/// Попадание оглушает монстра. Брошенный предмет падает на землю, и его можно поднять снова.
/// Вешается на префаб игрока. Тип оружия задаётся у PickupItem предмета.
/// </summary>
public class PlayerCombat : NetworkBehaviour
{
    [Header("Удар")]
    [SerializeField] private float meleeRange = 2.8f;
    [SerializeField] private float meleeRadius = 0.5f;
    [SerializeField] private float meleeCooldown = 0.8f;

    [Header("Бросок")]
    [SerializeField] private float throwSpeed = 18f;
    [SerializeField] private float throwUpward = 2f;
    [SerializeField] private float maxFlightSeconds = 4f;
    [SerializeField] private float hitRadius = 0.3f;

    private PlayerInventory inventory;
    private Camera cam;
    private float nextAttackTime;
    private float serverNextMeleeTime;

    public override void OnNetworkSpawn()
    {
        inventory = GetComponent<PlayerInventory>();
        cam = GetComponentInChildren<Camera>(true);
        if (!IsOwner) enabled = false;   // RPC на сервере работают и так
    }

    private void Update()
    {
        if (inventory == null || cam == null || inventory.InputBlocked) return;

        PickupItem item = inventory.HeldItemPrefab;
        if (item == null || (!item.CanMelee && !item.CanThrow)) return;

        bool left = LeftPressed();
        bool right = RightPressed();
        Transform t = cam.transform;

        if (item.CanMelee && left)
        {
            if (Time.time < nextAttackTime) return;
            nextAttackTime = Time.time + meleeCooldown;
            MeleeRpc(t.position, t.forward);
        }
        else if (item.CanThrow && ((!item.CanMelee && left) || right))
        {
            ThrowRpc(t.position + t.forward * 0.6f, t.forward);
        }
    }

    // ───────────── Удар (сервер проверяет попадание) ─────────────

    [Rpc(SendTo.Server)]
    private void MeleeRpc(Vector3 origin, Vector3 direction)
    {
        PickupItem item = inventory.HeldItemPrefab;
        if (item == null || !item.CanMelee) return;
        if (Time.time < serverNextMeleeTime) return;
        if (direction.sqrMagnitude < 0.01f) return;
        serverNextMeleeTime = Time.time + meleeCooldown * 0.8f;

        if (Physics.SphereCast(origin, meleeRadius, direction.normalized, out RaycastHit hit, meleeRange,
                ~0, QueryTriggerInteraction.Ignore))
        {
            var monster = hit.collider.GetComponentInParent<MonsterAI>();
            if (monster != null) monster.ServerHit();
        }
    }

    // ───────────── Бросок ─────────────

    [Rpc(SendTo.Server)]
    private void ThrowRpc(Vector3 origin, Vector3 direction)
    {
        PickupItem item = inventory.HeldItemPrefab;
        if (item == null || !item.CanThrow) return;
        if (direction.sqrMagnitude < 0.01f) return;
        if (!inventory.ServerConsumeHeld(out string itemId)) return;

        Vector3 velocity = direction.normalized * throwSpeed + Vector3.up * throwUpward;
        ThrowVisualRpc(origin, velocity);
        StartCoroutine(SimulateThrow(itemId, origin, velocity));
    }

    [Rpc(SendTo.Everyone)]
    private void ThrowVisualRpc(Vector3 origin, Vector3 velocity)
    {
        ThrownVisual.Launch(origin, velocity);
    }

    // Полёт считает сервер: попал в монстра — оглушение, в любом случае предмет падает на землю
    private IEnumerator SimulateThrow(string itemId, Vector3 position, Vector3 velocity)
    {
        const float dt = 0.02f;
        var wait = new WaitForSeconds(dt);

        for (float t = 0f; t < maxFlightSeconds; t += dt)
        {
            velocity += Physics.gravity * dt;
            Vector3 step = velocity * dt;
            float length = step.magnitude;

            if (Physics.SphereCast(position, hitRadius, step / length, out RaycastHit hit, length,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                var monster = hit.collider.GetComponentInParent<MonsterAI>();
                if (monster != null) monster.ServerHit();

                inventory.ServerSpawnItemAt(itemId, hit.point);
                yield break;
            }

            position += step;
            yield return wait;
        }

        inventory.ServerSpawnItemAt(itemId, position);
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool LeftPressed()  => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    private bool RightPressed() => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
    private bool LeftPressed()  => Input.GetMouseButtonDown(0);
    private bool RightPressed() => Input.GetMouseButtonDown(1);
#endif
}
