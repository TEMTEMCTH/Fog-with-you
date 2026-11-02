using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Атмосфера ночного леса: густой туман, темнота, холодный лунный свет и звук ветра.
/// Повесьте на пустой объект в ИГРОВОЙ сцене. Все значения можно менять в инспекторе.
/// </summary>
public class Atmosphere : MonoBehaviour
{
    [Header("Туман")]
    [SerializeField] private Color fogColor = new Color(0.07f, 0.09f, 0.12f);
    [Tooltip("Чем больше — тем гуще туман (0.03 — лёгкий, 0.08 — очень густой).")]
    [SerializeField] private float fogDensity = 0.05f;

    [Header("Темнота и лунный свет")]
    [SerializeField] private Color ambientColor = new Color(0.03f, 0.04f, 0.06f);
    [SerializeField] private Color moonColor = new Color(0.5f, 0.6f, 0.9f);
    [SerializeField] private float moonIntensity = 0.25f;

    [Header("Звук ветра")]
    [SerializeField] private bool playWind = true;
    [SerializeField, Range(0f, 1f)] private float windVolume = 0.5f;

    private void Start()
    {
        ApplyEnvironment();
        if (playWind) StartWind();
        StartCoroutine(KeepCamerasDark());
    }

    private void ApplyEnvironment()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;
        RenderSettings.reflectionIntensity = 0f;

        // «Луна» — все направленные источники света сцены
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            l.color = moonColor;
            l.intensity = moonIntensity;
        }
    }

    // Вместо яркого неба камеры показывают цвет тумана (камера игрока появляется уже после загрузки сцены)
    private IEnumerator KeepCamerasDark()
    {
        var wait = new WaitForSeconds(0.5f);
        while (true)
        {
            foreach (var cam in Camera.allCameras)
            {
                if (cam.cameraType != CameraType.Game) continue;
                if (cam.clearFlags == CameraClearFlags.SolidColor && cam.backgroundColor == fogColor) continue;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = fogColor;
            }
            yield return wait;
        }
    }

    // ───────────── Ветер (генерируется кодом, звуковые файлы не нужны) ─────────────

    private void StartWind()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.clip = BuildWindClip();
        source.loop = true;
        source.spatialBlend = 0f;   // 2D-звук
        source.volume = windVolume;
        source.Play();
    }

    private AudioClip BuildWindClip()
    {
        const int sampleRate = 22050;
        const float seconds = 8f;
        int count = (int)(sampleRate * seconds);
        var data = new float[count];

        var rng = new System.Random(12345);
        float low1 = 0f, low2 = 0f;
        for (int i = 0; i < count; i++)
        {
            // Белый шум, дважды сглаженный = низкий гул
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            low1 += (white - low1) * 0.05f;
            low2 += (low1 - low2) * 0.05f;

            // Медленные порывы (целое число периодов за клип — стык почти незаметен)
            float t = i / (float)count;
            float gust = 0.55f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * t)
                               + 0.20f * Mathf.Sin(2f * Mathf.PI * 5f * t + 1f);

            data[i] = Mathf.Clamp(low2 * gust * 7f, -1f, 1f);
        }

        // Плавное затухание на концах, чтобы не щёлкало при повторе
        int fade = (int)(sampleRate * 0.25f);
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[i] *= k;
            data[count - 1 - i] *= k;
        }

        var clip = AudioClip.Create("Wind", count, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
