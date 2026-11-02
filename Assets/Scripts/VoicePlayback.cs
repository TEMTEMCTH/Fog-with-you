using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Воспроизводит принятый по сети голос в 3D (громкость падает с расстоянием).
/// Используется и для игроков, и для монстров. Создаётся кодом через VoicePlayback.Create.
/// </summary>
public class VoicePlayback : MonoBehaviour
{
    private AudioSource source;
    private AudioClip clip;
    private int clipRate;

    private readonly Queue<float> queue = new Queue<float>();
    private bool playing;
    private int prebufferSamples;
    private int maxQueueSamples;

    public static VoicePlayback Create(Transform parent, Vector3 localPosition, float minDistance, float maxDistance)
    {
        var go = new GameObject("VoicePlayback");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        var voice = go.AddComponent<VoicePlayback>();
        voice.source = go.AddComponent<AudioSource>();
        voice.source.spatialBlend = 1f;
        voice.source.rolloffMode = AudioRolloffMode.Linear;
        voice.source.minDistance = minDistance;
        voice.source.maxDistance = maxDistance;
        voice.source.loop = true;
        voice.source.playOnAwake = false;
        return voice;
    }

    /// <summary>Добавляет в очередь кусочек голоса (μ-law, mono).</summary>
    public void Feed(byte[] mulaw, int rate)
    {
        if (mulaw == null || mulaw.Length == 0 || rate < 4000) return;

        if (clip == null || rate != clipRate)
            CreateClip(rate);

        float[] samples = VoiceCodec.Decode(mulaw);
        lock (queue)
        {
            for (int i = 0; i < samples.Length; i++) queue.Enqueue(samples[i]);
            // Не копим задержку: если очередь слишком длинная — выбрасываем самое старое
            while (queue.Count > maxQueueSamples) queue.Dequeue();
        }
    }

    private void CreateClip(int rate)
    {
        source.Stop();
        if (clip != null) Destroy(clip);

        clipRate = rate;
        prebufferSamples = Mathf.RoundToInt(rate * 0.10f);   // небольшой запас против рывков
        maxQueueSamples = Mathf.RoundToInt(rate * 0.50f);

        lock (queue) { queue.Clear(); playing = false; }

        clip = AudioClip.Create("VoiceStream", rate, 1, rate, true, OnAudioRead);
        source.clip = clip;
        source.Play();
    }

    // Вызывается звуковым потоком — здесь нельзя обращаться к объектам Unity
    private void OnAudioRead(float[] data)
    {
        lock (queue)
        {
            if (!playing && queue.Count >= prebufferSamples) playing = true;

            for (int i = 0; i < data.Length; i++)
                data[i] = (playing && queue.Count > 0) ? queue.Dequeue() : 0f;

            if (playing && queue.Count == 0) playing = false;   // данные кончились — ждём новые
        }
    }

    private void OnDestroy()
    {
        if (clip != null) Destroy(clip);
    }
}
