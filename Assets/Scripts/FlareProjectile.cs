using UnityEngine;

/// <summary>
/// Сигнальная ракета: летит по дуге, падает на землю и горит красным светом.
/// Создаётся кодом через FlareProjectile.Launch — на каждом компьютере локально,
/// все получают одинаковые начальные данные, поэтому ракету видят все игроки.
/// </summary>
public class FlareProjectile : MonoBehaviour
{
    private Vector3 velocity;
    private float lifetime;
    private float age;
    private bool landed;
    private Light flareLight;
    private float baseIntensity;
    private float noiseSeed;

    public static void Launch(Vector3 position, Vector3 velocity, float lifetime,
        float intensity, float range, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Flare";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.18f;

        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var renderer = go.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.material.color = color;
        renderer.material.EnableKeyword("_EMISSION");
        renderer.material.SetColor("_EmissionColor", color * 4f);

        var lightGo = new GameObject("FlareLight");
        lightGo.transform.SetParent(go.transform, false);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = 0f;
        l.shadows = LightShadows.None;

        var flare = go.AddComponent<FlareProjectile>();
        flare.velocity = velocity;
        flare.lifetime = lifetime;
        flare.flareLight = l;
        flare.baseIntensity = intensity;
        flare.noiseSeed = Random.Range(0f, 100f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;

        if (!landed)
        {
            velocity += Physics.gravity * 0.35f * dt;   // ракета летит по пологой дуге
            Vector3 step = velocity * dt;

            // Первые мгновения не проверяем столкновения, чтобы не попасть в самого стрелка
            if (age > 0.12f && step.sqrMagnitude > 0.000001f &&
                Physics.Raycast(transform.position, step.normalized, out RaycastHit hit, step.magnitude + 0.1f,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + hit.normal * 0.1f;
                landed = true;
            }
            else
            {
                transform.position += step;
            }
        }

        // Мерцание и угасание в последние 3 секунды
        float fade = Mathf.Clamp01((lifetime - age) / 3f);
        float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 12f, noiseSeed);
        flareLight.intensity = baseIntensity * flicker * fade;

        if (age >= lifetime) Destroy(gameObject);
    }
}
