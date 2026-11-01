using Unity.Netcode;
using UnityEngine;

public class FixPrefabs : MonoBehaviour
{
    public GameObject playerPrefab;

    void Start()
    {
        // Проверяем, что префаб привязан и сеть существует
        if (playerPrefab != null && NetworkManager.Singleton != null)
        {
            // Принудительно добавляем префаб в список разрешенных
            NetworkManager.Singleton.AddNetworkPrefab(playerPrefab);
        }
    }
}