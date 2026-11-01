using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Создаёт игроков в игровой сцене (а не в меню).
/// Повесьте на тот же объект, что и NetworkManager (в сцене MainMenu),
/// а поле Player Prefab у самого NetworkManager оставьте ПУСТЫМ.
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private string gameSceneName = "SampleScene";
    [SerializeField] private Vector3 spawnCenter = new Vector3(0f, 1.1f, 0f);
    [SerializeField] private float spawnRadius = 3f;

    private void Start()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;

        var sm = NetworkManager.Singleton.SceneManager;
        if (sm != null)
        {
            sm.OnLoadEventCompleted -= HandleLoadCompleted;
            sm.OnSynchronizeComplete -= SpawnPlayer;
        }
    }

    private void HandleServerStarted()
    {
        var sm = NetworkManager.Singleton.SceneManager;
        sm.OnLoadEventCompleted -= HandleLoadCompleted;
        sm.OnSynchronizeComplete -= SpawnPlayer;
        sm.OnLoadEventCompleted += HandleLoadCompleted;   // игроки, бывшие при загрузке сцены
        sm.OnSynchronizeComplete += SpawnPlayer;          // игроки, подключившиеся позже
    }

    private void HandleLoadCompleted(string sceneName, LoadSceneMode mode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameSceneName) return;
        foreach (ulong id in clientsCompleted)
            SpawnPlayer(id);
    }

    private void SpawnPlayer(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (nm.SpawnManager.GetPlayerNetworkObject(clientId) != null) return; // уже есть

        if (playerPrefab == null)
        {
            Debug.LogError("PlayerSpawner: не назначен Player Prefab!", this);
            return;
        }

        Vector2 offset = Random.insideUnitCircle * spawnRadius;
        Vector3 pos = spawnCenter + new Vector3(offset.x, 0f, offset.y);

        var player = Instantiate(playerPrefab, pos, Quaternion.identity);
        player.SpawnAsPlayerObject(clientId, true);
    }
}
