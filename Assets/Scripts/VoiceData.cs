using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Сжатие голоса: 16 бит -> 8 бит (μ-law). Качество «как по рации» (в самый раз для хоррора),
/// но голос занимает в 4 раза меньше места и трафика.
/// </summary>
public static class VoiceCodec
{
    public const int TargetRate = 16000;   // желаемая частота дискретизации
    private const float Mu = 255f;
    private static readonly float LogMu = Mathf.Log(1f + Mu);
    private static readonly float[] DecodeTable = BuildDecodeTable();

    public static byte EncodeSample(float x)
    {
        x = Mathf.Clamp(x, -1f, 1f);
        float y = Mathf.Sign(x) * Mathf.Log(1f + Mu * Mathf.Abs(x)) / LogMu;
        return (byte)Mathf.Clamp(Mathf.RoundToInt((y * 0.5f + 0.5f) * 255f), 0, 255);
    }

    public static byte[] Encode(float[] samples)
    {
        var bytes = new byte[samples.Length];
        for (int i = 0; i < samples.Length; i++) bytes[i] = EncodeSample(samples[i]);
        return bytes;
    }

    public static float[] Decode(byte[] bytes)
    {
        var samples = new float[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) samples[i] = DecodeTable[bytes[i]];
        return samples;
    }

    private static float[] BuildDecodeTable()
    {
        var table = new float[256];
        for (int i = 0; i < 256; i++)
        {
            float y = i / 255f * 2f - 1f;
            table[i] = Mathf.Sign(y) * (Mathf.Pow(1f + Mu, Mathf.Abs(y)) - 1f) / Mu;
        }
        return table;
    }
}

/// <summary>
/// «Память» монстров НА СЕРВЕРЕ (у хоста): реплики, которые игроки недавно сказали в голосовой чат.
/// Монстры берут отсюда звук, чтобы повторять сказанное голосом самих игроков.
/// Хранится только в оперативной памяти и очищается, когда начинается новая игра.
/// </summary>
public static class VoiceBank
{
    public class Clip
    {
        public byte[] Data;
        public int Rate;
        public ulong Owner;
    }

    private class Builder
    {
        public readonly List<byte> Bytes = new List<byte>();
        public int Rate;
        public float LastTime;
    }

    private static readonly Dictionary<ulong, List<Clip>> overheard = new Dictionary<ulong, List<Clip>>();
    private static readonly Dictionary<ulong, Builder> building = new Dictionary<ulong, Builder>();

    private const float GapSeconds = 0.6f;          // пауза, после которой реплика считается законченной
    private const float MinSeconds = 0.5f;          // слишком короткие звуки (кашель, щелчки) не сохраняем
    private const float MaxSeconds = 6f;
    private const int MaxPerPlayer = 6;             // сколько последних реплик помним у каждого игрока

    public static void Clear()
    {
        overheard.Clear();
        building.Clear();
    }

    /// <summary>Сюда сервер отдаёт каждый кадр живой речи игрока — из них собираются реплики.</summary>
    public static void Feed(ulong speaker, byte[] frame, int rate)
    {
        FlushStale();

        if (!building.TryGetValue(speaker, out Builder b))
        {
            b = new Builder { Rate = rate };
            building[speaker] = b;
        }

        b.Rate = rate;
        b.Bytes.AddRange(frame);
        b.LastTime = Time.unscaledTime;

        if (b.Bytes.Count > MaxSeconds * rate)
            Finish(speaker, b);
    }

    /// <param name="ownerFilter">Необязательно: функция, которая решает, можно ли использовать реплики этого игрока.</param>
    public static bool TryGetOverheard(out Clip clip, System.Predicate<ulong> ownerFilter = null)
    {
        FlushStale();
        clip = null;
        var all = new List<Clip>();
        foreach (var kv in overheard)
        {
            if (ownerFilter != null && !ownerFilter(kv.Key)) continue;
            all.AddRange(kv.Value);
        }
        if (all.Count == 0) return false;
        clip = all[Random.Range(0, all.Count)];
        return true;
    }

    private static void FlushStale()
    {
        float now = Time.unscaledTime;
        foreach (var kv in building)
            if (kv.Value.Bytes.Count > 0 && now - kv.Value.LastTime > GapSeconds)
                Finish(kv.Key, kv.Value);
    }

    private static void Finish(ulong speaker, Builder b)
    {
        if (b.Bytes.Count >= MinSeconds * b.Rate)
        {
            if (!overheard.TryGetValue(speaker, out List<Clip> list))
            {
                list = new List<Clip>();
                overheard[speaker] = list;
            }
            list.Add(new Clip { Data = b.Bytes.ToArray(), Rate = b.Rate, Owner = speaker });
            while (list.Count > MaxPerPlayer) list.RemoveAt(0);
        }
        b.Bytes.Clear();
    }
}