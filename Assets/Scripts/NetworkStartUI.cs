using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Простые кнопки Host / Client / Server для запуска сети.
/// Повесьте на тот же объект, где стоит NetworkManager.
/// </summary>
public class NetworkStartUI : MonoBehaviour
{
    private void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 200, 150));
        if (!nm.IsClient && !nm.IsServer)
        {
            if (GUILayout.Button("Host"))   nm.StartHost();
            if (GUILayout.Button("Client")) nm.StartClient();
            if (GUILayout.Button("Server")) nm.StartServer();
        }
        else
        {
            string mode = nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client";
            GUILayout.Label("Режим: " + mode);
            if (GUILayout.Button("Отключиться")) nm.Shutdown();
        }
        GUILayout.EndArea();
    }
}
