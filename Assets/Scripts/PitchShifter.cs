using UnityEngine;

/// <summary>
/// Меняет высоту голоса в реальном времени, не меняя скорость речи
/// (две «читающие головки» с плавным переходом между ними).
/// Ratio больше 1 — голос выше (2 = на октаву), меньше 1 — ниже.
/// </summary>
public class PitchShifter
{
    private readonly float[] buffer;
    private readonly int windowLength;
    private int writeIndex;
    private float phase;

    /// <summary>Во сколько раз повысить голос.</summary>
    public float Ratio = 1f;

    public PitchShifter(int sampleRate)
    {
        windowLength = Mathf.Max(64, (int)(sampleRate * 0.04f));   // «зерно» около 40 мс
        buffer = new float[windowLength + 4];
    }

    public void Reset()
    {
        System.Array.Clear(buffer, 0, buffer.Length);
        writeIndex = 0;
        phase = 0f;
    }

    public float Process(float input)
    {
        buffer[writeIndex] = input;

        // Задержка плавно меняется, из-за этого звук «читается» быстрее или медленнее
        phase += (1f - Ratio) / windowLength;
        phase -= Mathf.Floor(phase);

        float output = 0f;
        for (int tap = 0; tap < 2; tap++)
        {
            float p = phase + 0.5f * tap;
            p -= Mathf.Floor(p);

            float delay = p * windowLength;
            float readPos = writeIndex - delay;
            int i0 = Mathf.FloorToInt(readPos);
            float frac = readPos - i0;

            int a = ((i0 % buffer.Length) + buffer.Length) % buffer.Length;
            int b = (a + 1) % buffer.Length;
            float sample = buffer[a] * (1f - frac) + buffer[b] * frac;

            float window = Mathf.Sin(Mathf.PI * p);   // на стыках громкость 0 — щелчков нет
            output += sample * window * window;
        }

        writeIndex = (writeIndex + 1) % buffer.Length;
        return output;
    }
}
