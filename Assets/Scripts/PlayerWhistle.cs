using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Свисток: пока он в руке, ЛКМ — свистнуть. Свист слышно на 60 метров (чем дальше, тем тише),
/// повторить можно только через 3 минуты.
/// Вешается на префаб игрока.
/// </summary>
public class PlayerWhistle : NetworkBehaviour
{
    /// <summary>Item Id предмета-свистка (должен совпадать с PickupItem на префабе предмета).</summary>
    public const string ItemId = "whistle";

    [Header("Свист")]
    [SerializeField] private float cooldown = 180f;       // 3 минуты
    [SerializeField] private float hearDistance = 60f;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Header("Свой звук (необязательно, иначе используется встроенный)")]
    [SerializeField] private AudioClip customWhistle;

    private PlayerInventory inventory;
    private AudioSource source;
    private float nextWhistleTime;        // у владельца
    private float serverNextWhistleTime;  // на сервере

    public override void OnNetworkSpawn()
    {
        inventory = GetComponent<PlayerInventory>();

        var go = new GameObject("WhistleAudio");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;   // чем дальше, тем тише
        source.minDistance = 5f;
        source.maxDistance = hearDistance;

        if (!IsOwner) enabled = false;   // RPC работают и так
    }

    private void Update()
    {
        if (inventory == null || inventory.InputBlocked) return;
        if (inventory.HeldItemId != ItemId) return;
        if (!FirePressed()) return;

        if (Time.time < nextWhistleTime)
        {
            int remaining = Mathf.CeilToInt(nextWhistleTime - Time.time);
            inventory.ShowMessage($"Свистнуть можно будет через {remaining / 60}:{remaining % 60:00}");
            return;
        }

        nextWhistleTime = Time.time + cooldown;
        WhistleRpc();
    }

    [Rpc(SendTo.Server)]
    private void WhistleRpc()
    {
        if (inventory == null || inventory.HeldItemId != ItemId) return;
        if (Time.time < serverNextWhistleTime) return;
        serverNextWhistleTime = Time.time + cooldown * 0.95f;   // небольшой допуск на сетевую задержку
        PlayWhistleRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void PlayWhistleRpc()
    {
        AudioClip clip = customWhistle != null ? customWhistle : ProceduralSounds.Whistle;
        if (source != null && clip != null) source.PlayOneShot(clip, volume);
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool FirePressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    private bool FirePressed() => Input.GetMouseButtonDown(0);
#endif
}
