using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Состояние плена игрока: свободен / несёт монстр / сидит в клетке или яме.
/// Пока игрок схвачен или в плену, он не может ходить и пользоваться предметами (но может смотреть и говорить).
/// Вешается на префаб игрока.
/// </summary>
public class PlayerCapture : NetworkBehaviour
{
    public enum CaptureState { Free = 0, Carried = 1, Imprisoned = 2 }

    private NetworkVariable<int> state;
    private CharacterController controller;
    private PlayerController motion;

    // Пока игрока несёт монстр — он следует за этой точкой (считается на компьютере самого игрока)
    private Transform holder;
    private Vector3 holderOffset;

    public CaptureState State => (CaptureState)state.Value;
    public bool IsLocked => state.Value != (int)CaptureState.Free;

    private void Awake()
    {
        state = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        controller = GetComponent<CharacterController>();
        motion = GetComponent<PlayerController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;   // следовать за монстром нужно только на компьютере владельца
    }

    // ───────────── Вызывается на сервере (хосте) ─────────────

    public void ServerBeginCarried(NetworkObject monster, Vector3 localOffset)
    {
        state.Value = (int)CaptureState.Carried;
        BeginCarriedRpc(monster, localOffset);
    }

    /// <summary>Монстр отпустил игрока (оглушён) — игрок свободен и оказывается на земле.</summary>
    public void ServerEndCarried(Vector3 dropPosition)
    {
        state.Value = (int)CaptureState.Free;
        PlaceRpc(dropPosition);
    }

    /// <summary>Игрока посадили в клетку или яму.</summary>
    public void ServerImprison(Vector3 position)
    {
        state.Value = (int)CaptureState.Imprisoned;
        PlaceRpc(position);
    }

    /// <summary>Игрока освободили.</summary>
    public void ServerFree(Vector3 exitPosition)
    {
        state.Value = (int)CaptureState.Free;
        PlaceRpc(exitPosition);
    }

    // ───────────── Выполняется на компьютере игрока ─────────────

    [Rpc(SendTo.Owner)]
    private void BeginCarriedRpc(NetworkObjectReference monsterRef, Vector3 localOffset)
    {
        if (!monsterRef.TryGet(out NetworkObject monsterObject)) return;
        holder = monsterObject.transform;
        holderOffset = localOffset;
        if (controller != null) controller.enabled = false;
    }

    [Rpc(SendTo.Owner)]
    private void PlaceRpc(Vector3 position)
    {
        holder = null;

        if (controller != null) controller.enabled = false;
        transform.position = position;
        var networkTransform = GetComponent<NetworkTransform>();
        if (networkTransform != null)
            networkTransform.Teleport(position, transform.rotation, transform.localScale);
        if (controller != null) controller.enabled = true;

        if (motion != null) motion.ResetMotion();
    }

    private void LateUpdate()
    {
        if (holder == null) return;
        transform.position = holder.TransformPoint(holderOffset);
    }
}
