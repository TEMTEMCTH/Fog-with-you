using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Освобождение пленников: подойдите к занятой клетке или яме и удерживайте E несколько секунд.
/// Вешается на префаб игрока.
/// </summary>
public class PlayerRescue : NetworkBehaviour
{
    [SerializeField] private float rescueRange = 3f;
    [SerializeField] private float rescueSeconds = 3f;

    private PlayerInventory inventory;
    private float progress;

    public override void OnNetworkSpawn()
    {
        inventory = GetComponent<PlayerInventory>();
        if (!IsOwner) enabled = false;   // RPC на сервере работают и так
    }

    private void Update()
    {
        if (inventory == null || inventory.InputBlocked) { progress = 0f; return; }

        CaptureSpot spot = FindNearbyOccupiedSpot();
        if (spot == null) { progress = 0f; return; }

        if (!InteractHeld())
        {
            progress = 0f;
            inventory.ShowMessage("Удерживайте E, чтобы освободить пленника", 0.2f);
            return;
        }

        progress += Time.deltaTime;
        inventory.ShowMessage($"Освобождаем... {Mathf.CeilToInt(rescueSeconds - progress)}", 0.2f);

        if (progress >= rescueSeconds)
        {
            progress = 0f;
            RescueRpc(spot.NetworkObject);
        }
    }

    private CaptureSpot FindNearbyOccupiedSpot()
    {
        CaptureSpot best = null;
        float bestDistance = rescueRange;
        foreach (var spot in CaptureSpot.All)
        {
            if (spot == null || !spot.Occupied) continue;
            float d = Vector3.Distance(spot.transform.position, transform.position);
            if (d <= bestDistance) { bestDistance = d; best = spot; }
        }
        return best;
    }

    [Rpc(SendTo.Server)]
    private void RescueRpc(NetworkObjectReference spotRef)
    {
        if (!spotRef.TryGet(out NetworkObject spotObject)) return;
        var spot = spotObject.GetComponent<CaptureSpot>();
        if (spot == null || !spot.Occupied) return;

        // Освобождающий сам должен быть свободен и находиться рядом
        var self = GetComponent<PlayerCapture>();
        if (self != null && self.IsLocked) return;
        if (Vector3.Distance(spot.transform.position, transform.position) > rescueRange + 3f) return;

        spot.ServerFree();
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool InteractHeld() => Keyboard.current != null && Keyboard.current.eKey.isPressed;
#else
    private bool InteractHeld() => Input.GetKey(KeyCode.E);
#endif
}
