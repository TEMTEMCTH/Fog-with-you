using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Голос монстра: время от времени он повторяет то, что игроки недавно говорили в голосовой чат, —
/// их собственными голосами. Пока никто ничего не сказал, монстр молчит.
/// Монстр НИКОГДА не произносит голос игрока, который находится рядом с ним, и никогда не проигрывает
/// игроку его же голос — вы слышите только голоса других игроков.
/// Вешается на объект монстра (нужен NetworkObject). Логика работает на хосте.
/// Пока у монстра нет ИИ, это просто «говорящая» точка в лесу — для проверки звука.
/// </summary>
public class MonsterVoice : NetworkBehaviour
{
    [Header("Как часто говорит (секунды)")]
    [SerializeField] private float firstDelay = 10f;
    [SerializeField] private float minInterval = 20f;
    [SerializeField] private float maxInterval = 45f;

    [Header("Голос")]
    [SerializeField] private float voiceMinDistance = 4f;
    [SerializeField] private float voiceMaxDistance = 60f;

    [Header("Защита от «своего голоса»")]
    [Tooltip("Если игрок, чей голос хочет повторить монстр, ближе этого расстояния к монстру — монстр выберет голос другого игрока (или промолчит).")]
    [SerializeField] private float ownerExclusionRadius = 40f;

    private VoicePlayback playback;
    private bool speaking;

    public bool IsSpeaking => speaking;

    public override void OnNetworkSpawn()
    {
        playback = VoicePlayback.Create(transform, new Vector3(0f, 1f, 0f), voiceMinDistance, voiceMaxDistance);
        if (IsServer) StartCoroutine(Loop());
    }

    private IEnumerator Loop()
    {
        yield return new WaitForSeconds(firstDelay);
        while (true)
        {
            if (!speaking) yield return SayRandom();
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
        }
    }

    /// <summary>Повторить случайную подслушанную реплику. Можно вызывать из ИИ монстра (только на хосте).</summary>
    public IEnumerator SayRandom()
    {
        // Берём только реплики тех игроков, которых сейчас нет рядом с монстром
        if (VoiceBank.TryGetOverheard(out VoiceBank.Clip clip, IsOwnerFarEnough) && clip != null)
            yield return Speak(clip);
    }

    private bool IsOwnerFarEnough(ulong ownerId)
    {
        var nm = NetworkManager.Singleton;
        if (!nm.ConnectedClients.TryGetValue(ownerId, out var client)) return true;   // игрок уже вышел
        var playerObject = client.PlayerObject;
        if (playerObject == null) return true;

        return (playerObject.transform.position - transform.position).sqrMagnitude
               > ownerExclusionRadius * ownerExclusionRadius;
    }

    private IEnumerator Speak(VoiceBank.Clip clip)
    {
        speaking = true;

        int frameBytes = Mathf.Clamp(clip.Rate / 20, 100, 1000);
        var wait = new WaitForSeconds(frameBytes / (float)clip.Rate * 0.95f);

        for (int offset = 0; offset < clip.Data.Length; offset += frameBytes)
        {
            int length = Mathf.Min(frameBytes, clip.Data.Length - offset);
            var frame = new byte[length];
            System.Buffer.BlockCopy(clip.Data, offset, frame, 0, length);

            // Хозяин голоса не получает свою реплику, даже если подошёл ближе во время фразы
            var targets = ListenersInRange(clip.Owner);
            if (targets.Count > 0)
                PlayVoiceRpc(frame, clip.Rate, RpcTarget.Group(targets, RpcTargetUse.Temp));

            yield return wait;
        }

        speaking = false;
    }

    private List<ulong> ListenersInRange(ulong excludeClientId)
    {
        var result = new List<ulong>();
        float maxSqr = voiceMaxDistance * 1.1f;
        maxSqr *= maxSqr;

        foreach (var kv in NetworkManager.Singleton.ConnectedClients)
        {
            if (kv.Key == excludeClientId) continue;

            var playerObject = kv.Value.PlayerObject;
            if (playerObject == null) continue;
            if ((playerObject.transform.position - transform.position).sqrMagnitude <= maxSqr)
                result.Add(kv.Key);
        }
        return result;
    }

    [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
    private void PlayVoiceRpc(byte[] data, int rate, RpcParams rpcParams = default)
    {
        if (playback != null) playback.Feed(data, rate);
    }
}