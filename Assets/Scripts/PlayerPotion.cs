using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Зелье высокого голоса: пока оно в руке, ЛКМ — выпить. Зелье исчезает, и на время эффекта
/// ваш голос в голосовом чате становится очень высоким (его слышат все игроки, а монстр потом повторяет его таким же).
/// Вешается на префаб игрока (вместе с PlayerInventory и VoiceChat).
/// </summary>
public class PlayerPotion : NetworkBehaviour
{
    /// <summary>Item Id предмета-зелья (должен совпадать с PickupItem на префабе предмета).</summary>
    public const string ItemId = "voicepotion";

    [Header("Эффект")]
    [Tooltip("Во сколько раз голос становится выше (2 = на октаву; 1.9 — очень высокий «мультяшный»).")]
    [SerializeField] private float pitchRatio = 1.9f;
    [SerializeField] private float effectSeconds = 90f;

    private PlayerInventory inventory;
    private VoiceChat voice;

    public override void OnNetworkSpawn()
    {
        inventory = GetComponent<PlayerInventory>();
        voice = GetComponent<VoiceChat>();
        if (!IsOwner) enabled = false;   // RPC на сервере работают и так
    }

    private void Update()
    {
        if (inventory == null || inventory.InputBlocked) return;
        if (inventory.HeldItemId != ItemId) return;
        if (!UsePressed()) return;

        DrinkRpc();
    }

    // Сервер проверяет, что зелье действительно в руке, и забирает его
    [Rpc(SendTo.Server)]
    private void DrinkRpc()
    {
        if (inventory == null || inventory.HeldItemId != ItemId) return;
        if (!inventory.ServerConsumeHeld(out string consumedItem)) return;

        ApplyEffectRpc(pitchRatio, effectSeconds);
    }

    // Эффект включается на компьютере самого игрока (там, где записывается его голос)
    [Rpc(SendTo.Owner)]
    private void ApplyEffectRpc(float ratio, float seconds)
    {
        if (voice != null) voice.StartPitchEffect(ratio, seconds);
        if (inventory != null)
            inventory.ShowMessage($"Вы выпили зелье: голос станет высоким на {seconds:0} с", 3f);
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool UsePressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    private bool UsePressed() => Input.GetMouseButtonDown(0);
#endif
}
