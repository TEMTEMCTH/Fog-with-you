using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Клетка или яма, куда монстр относит пойманных игроков.
/// Вешается на пустой объект в игровой сцене (нужен NetworkObject).
/// Необязательные дочерние точки: PrisonPoint — где сидит пленник, ExitPoint — где появляется освобождённый.
/// </summary>
public class CaptureSpot : NetworkBehaviour
{
    public static readonly List<CaptureSpot> All = new List<CaptureSpot>();

    [SerializeField] private Transform prisonPoint;
    [SerializeField] private Transform exitPoint;

    private NetworkVariable<bool> occupied;
    private PlayerCapture prisoner;   // только на сервере

    public bool Occupied => occupied.Value;

    public Vector3 PrisonPosition =>
        prisonPoint != null ? prisonPoint.position : transform.position + Vector3.up * 1.0f;

    public Vector3 ExitPosition =>
        exitPoint != null ? exitPoint.position : transform.position + transform.forward * 3f + Vector3.up * 1.2f;

    private void Awake()
    {
        occupied = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
    }

    public static CaptureSpot FindNearestFree(Vector3 from)
    {
        CaptureSpot best = null;
        float bestDistance = float.MaxValue;
        foreach (var spot in All)
        {
            if (spot == null || spot.Occupied) continue;
            float d = (spot.transform.position - from).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = spot; }
        }
        return best;
    }

    /// <summary>Посадить игрока (только на сервере).</summary>
    public void ServerImprison(PlayerCapture player)
    {
        prisoner = player;
        occupied.Value = true;
        player.ServerImprison(PrisonPosition);
    }

    /// <summary>Освободить пленника (только на сервере).</summary>
    public void ServerFree()
    {
        if (prisoner != null) prisoner.ServerFree(ExitPosition);
        prisoner = null;
        occupied.Value = false;
    }
}
