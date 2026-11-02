using UnityEngine;

/// <summary>
/// Звуки, которые создаются кодом (шаги, прыжок, приземление, приседание, карканье ворон, крик монстра).
/// Это «заглушки», чтобы игра звучала без внешних файлов. Любой из них можно заменить настоящей записью
/// в инспекторе (поля Custom ... у PlayerAudio и AmbientScares).
/// </summary>
public static class ProceduralSounds
{
    private const int Rate = 22050;

    private static AudioClip[] steps, caws, screams;
    private static AudioClip jump, land, crouch;

    public static AudioClip[] Steps  => steps ??= BuildSteps();
    public static AudioClip[] Caws   => caws ??= BuildCaws();
    public static AudioClip[] Screams => screams ??= BuildScreams();
    public static AudioClip Jump     => jump ??= MakeJump();
    public static AudioClip Land     => land ??= MakeLand();
    public static AudioClip Crouch   => crouch ??= MakeCrouch();

    // ───────────── Шаги, прыжок, приземление, приседание ─────────────

    private static AudioClip[] BuildSteps()
    {
        var list = new AudioClip[4];
        for (int i = 0; i < list.Length; i++) list[i] = MakeStep(100 + i * 7);
        return list;
    }

    private static AudioClip MakeStep(int seed)
    {
        var rng = new System.Random(seed);
        int n = (int)(Rate * 0.22f);
        var d = new float[n];
        float lp = 0f, lp2 = 0f;
        float crunchFreq = 250f + (seed % 5) * 70f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.10f;
            lp2 += (white - lp2) * 0.45f;
            float hp = white - lp2;
            float attack = Mathf.Clamp01(t * 400f);
            float thud = lp * Mathf.Exp(-t * 32f) * 4f;
            float crunch = hp * Mathf.Exp(-t * 26f) * (0.55f + 0.45f * Mathf.Sin(t * crunchFreq * 6.28318f)) * 0.7f;
            d[i] = (thud + crunch) * attack;
        }
        Normalize(d, 0.8f);
        return Make("Step" + seed, d);
    }

    private static AudioClip MakeJump()
    {
        var rng = new System.Random(7);
        float dur = 0.3f;
        int n = (int)(Rate * dur);
        var d = new float[n];
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.15f;
            lp2 += (white - lp2) * 0.4f;
            float env = Mathf.Sin(Mathf.PI * (t / dur));
            d[i] = (lp * 1.2f + (white - lp2) * 0.5f) * env;
        }
        Normalize(d, 0.7f);
        return Make("Jump", d);
    }

    private static AudioClip MakeLand()
    {
        var rng = new System.Random(11);
        int n = (int)(Rate * 0.4f);
        var d = new float[n];
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.05f;
            lp2 += (white - lp2) * 0.4f;
            float thump = lp * Mathf.Exp(-t * 14f) * 6f;
            float sub = Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 12f) * 0.8f;
            float crunch = (white - lp2) * Mathf.Exp(-t * 30f) * 0.4f;
            d[i] = (thump + sub + crunch) * Mathf.Clamp01(t * 500f);
        }
        Normalize(d, 0.9f);
        return Make("Land", d);
    }

    private static AudioClip MakeCrouch()
    {
        var rng = new System.Random(13);
        float dur = 0.28f;
        int n = (int)(Rate * dur);
        var d = new float[n];
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.05f;
            lp2 += (white - lp2) * 0.3f;
            float band = lp2 - lp;                       // шорох ткани
            float env = Mathf.Pow(Mathf.Sin(Mathf.PI * (t / dur)), 2f)
                        * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 9f * t));
            d[i] = band * env;
        }
        Normalize(d, 0.6f);
        return Make("Crouch", d);
    }

    // ───────────── Вороны ─────────────

    private static AudioClip[] BuildCaws()
    {
        return new[]
        {
            MakeCaw(21, 520f, 0.50f),
            MakeCaw(22, 600f, 0.42f),
            MakeCaw(23, 460f, 0.60f),
        };
    }

    private static AudioClip MakeCaw(int seed, float baseFreq, float dur)
    {
        var rng = new System.Random(seed);
        int n = (int)(Rate * dur);
        var d = new float[n];
        float phase = 0f, noiseLp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float u = t / dur;

            // Высота: быстро вверх, потом вниз
            float f = baseFreq * (0.8f + 0.5f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.4f))) * (1f - 0.25f * u);
            phase += 2f * Mathf.PI * f / Rate;
            if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;

            // Резкий «жужжащий» тон: много гармоник, средние усилены (носовой оттенок)
            float tone = 0f;
            for (int h = 1; h <= 9; h++)
                tone += Mathf.Sin(phase * h) / h * (h >= 3 && h <= 5 ? 1.6f : 1f);

            float rough = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 75f * t);   // хрипота
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            noiseLp += (white - noiseLp) * 0.35f;

            float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((dur - t) / 0.15f);
            d[i] = (tone * 0.35f * rough + noiseLp * 0.45f) * env;
        }
        Normalize(d, 0.85f);
        return Make("Caw" + seed, d);
    }

    // ───────────── Крик монстра ─────────────

    private static AudioClip[] BuildScreams()
    {
        return new[]
        {
            MakeScream(31, 320f, 1.5f),
            MakeScream(32, 240f, 1.8f),
        };
    }

    private static AudioClip MakeScream(int seed, float baseFreq, float dur)
    {
        var rng = new System.Random(seed);
        int body = (int)(Rate * dur);
        int echoDelay = (int)(Rate * 0.28f);
        int n = body + Rate;              // + хвост эха
        var d = new float[n];
        float phase = 0f, noiseLp = 0f;
        for (int i = 0; i < body; i++)
        {
            float t = i / (float)Rate;
            float u = t / dur;

            // Вопль: высота резко растёт и медленно падает, с дрожанием
            float contour = 1f + 1.6f * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.6f));
            float f = baseFreq * contour * (1f + 0.04f * Mathf.Sin(2f * Mathf.PI * 7f * t));
            phase += 2f * Mathf.PI * f / Rate;
            if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;

            float tone = 0f;
            for (int h = 1; h <= 12; h++)
                tone += Mathf.Sin(phase * h) / Mathf.Pow(h, 0.8f);

            float ring = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 41f * t);       // «рычание»
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            noiseLp += (white - noiseLp) * 0.3f;

            float x = (tone * 0.25f * ring + noiseLp * 0.5f);
            x = (float)System.Math.Tanh(x * 3.0);                                // перегруз
            float env = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((dur - t) / 0.5f);
            d[i] = x * env;
        }

        // Эхо леса
        for (int i = echoDelay; i < n; i++)
            d[i] += d[i - echoDelay] * 0.35f;

        Normalize(d, 0.9f);
        return Make("Scream" + seed, d);
    }

    // ───────────── Вспомогательное ─────────────

    private static AudioClip Make(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static void Normalize(float[] d, float peak)
    {
        float max = 0.0001f;
        for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
        float k = peak / max;
        for (int i = 0; i < d.Length; i++) d[i] *= k;
    }
}

/// <summary>Одноразовый 3D-звук в точке мира.</summary>
public static class AudioUtil
{
    public static void PlayAt(AudioClip clip, Vector3 position, float volume,
        float minDistance, float maxDistance, float pitch = 1f)
    {
        if (clip == null) return;
        var go = new GameObject("OneShot3D");
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.5f);
    }
}
