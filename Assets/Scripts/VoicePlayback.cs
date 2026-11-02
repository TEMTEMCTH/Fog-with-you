using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Воспроизводит принятый по сети голос в 3D. Используется и для игроков, и для монстров.
/// Громкость падает с расстоянием: на расстоянии «слышимости» голоса звук полностью затихает.
/// Крик (shout) слышно дальше и с эхом, как в лесу.
/// Создаётся кодом через VoicePlayback.Create.
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

    private float minDistance;
    private float defaultRange;

    // Эхо (работает в звуковом потоке)
    private float[] echoBuffer;
    private int echoPos;
    private volatile bool echoFeed;
    private float lastEchoFeedTime;

    private static Transform cachedListener;
    private static float nextListenerSearch;

    public static VoicePlayback Create(Transform parent, Vector3 localPosition, float minDistance, float maxDistance)
    {
        var go = new GameObject("VoicePlayback");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        var voice = go.AddComponent<VoicePlayback>();
        voice.minDistance = minDistance;
        voice.defaultRange = maxDistance;

        voice.source = go.AddComponent<AudioSource>();
        voice.source.spatialBlend = 1f;   // объёмный звук (слева/справа, спереди/сзади)
        // Громкость по расстоянию считаем сами (чтобы у обычной речи и крика была разная дальность),
        // поэтому встроенное затухание отключаем
        voice.source.rolloffMode = AudioRolloffMode.Custom;
        voice.source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0f, 1f, 1f, 1f));
        voice.source.minDistance = 1f;
        voice.source.maxDistance = 500f;
        voice.source.loop = true;
        voice.source.playOnAwake = false;
        return voice;
    }

    /// <summary>Добавляет в очередь кусочек голоса (μ-law, mono).</summary>
    /// <param name="range">Расстояние, на котором голос полностью затихает (меньше нуля — по умолчанию).</param>
    /// <param name="shout">Крик: добавляется эхо.</param>
    public void Feed(byte[] mulaw, int rate, float range = -1f, bool shout = false)
    {
        if (mulaw == null || mulaw.Length == 0 || rate < 4000) return;

        // Чем дальше говорящий, тем тише
        float gain = ComputeGain(range > 0f ? range : defaultRange);
        if (gain <= 0.001f) return;

        if (clip == null || rate != clipRate)
            CreateClip(rate);

        if (shout)
        {
            echoFeed = true;
            lastEchoFeedTime = Time.unscaledTime;
        }

        float[] samples = VoiceCodec.Decode(mulaw);
        for (int i = 0; i < samples.Length; i++) samples[i] *= gain;

        lock (queue)
        {
            for (int i = 0; i < samples.Length; i++) queue.Enqueue(samples[i]);
            // Не копим задержку: если очередь слишком длинная — выбрасываем самое старое
            while (queue.Count > maxQueueSamples) queue.Dequeue();
        }
    }

    private void Update()
    {
        // Эхо выключаем, когда крик закончился (хвост эха ещё доиграет сам)
        if (echoFeed && Time.unscaledTime - lastEchoFeedTime > 0.4f)
            echoFeed = false;
    }

    private float ComputeGain(float range)
    {
        Transform listener = GetListener();
        if (listener == null) return 1f;

        float distance = Vector3.Distance(listener.position, transform.position);
        float span = Mathf.Max(0.01f, range - minDistance);
        return Mathf.Clamp01(1f - (distance - minDistance) / span);
    }

    private static Transform GetListener()
    {
        if (cachedListener != null && cachedListener.gameObject.activeInHierarchy
            && Time.unscaledTime < nextListenerSearch)
            return cachedListener;

        nextListenerSearch = Time.unscaledTime + 1f;
        cachedListener = null;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (l.enabled && l.gameObject.activeInHierarchy)
            {
                cachedListener = l.transform;
                break;
            }
        }
        return cachedListener;
    }

    private void CreateClip(int rate)
    {
        source.Stop();
        if (clip != null) Destroy(clip);

        clipRate = rate;
        prebufferSamples = Mathf.RoundToInt(rate * 0.10f);   // небольшой запас против рывков
        maxQueueSamples = Mathf.RoundToInt(rate * 0.50f);

        lock (queue)
        {
            queue.Clear();
            playing = false;
            echoBuffer = new float[Mathf.Max(1, Mathf.RoundToInt(rate * 0.3f))];
            echoPos = 0;
        }

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

            bool feedEcho = echoFeed;
            int echoLength = echoBuffer != null ? echoBuffer.Length : 0;

            for (int i = 0; i < data.Length; i++)
            {
                float dry = (playing && queue.Count > 0) ? queue.Dequeue() : 0f;
                float wet = 0f;

                if (echoLength > 0)
                {
                    if (echoPos >= echoLength) echoPos = 0;
                    wet = echoBuffer[echoPos];
                    // В линию задержки подаём голос только во время крика; повторы затухают сами
                    echoBuffer[echoPos] = (feedEcho ? dry * 0.6f : 0f) + wet * 0.5f;
                    echoPos++;
                }

                float v = dry + wet * 0.7f;
                if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                data[i] = v;
            }

            if (playing && queue.Count == 0) playing = false;   // данные кончились — ждём новые
        }
    }

    private void OnDestroy()
    {
        if (clip != null) Destroy(clip);
    }
}