using UnityEngine;

/// <summary>
/// Летящий брошенный предмет (только картинка). Настоящий предмет на земле создаёт сервер.
/// </summary>
public class ThrownVisual : MonoBehaviour
{
    private Vector3 velocity;
    private float age;

    public static void Launch(Vector3 position, Vector3 velocity)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Thrown";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.2f;

        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var renderer = go.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.material.color = new Color(0.45f, 0.4f, 0.35f);

        go.AddComponent<ThrownVisual>().velocity = velocity;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;

        velocity += Physics.gravity * dt;
        Vector3 step = velocity * dt;

        // Первые мгновения не проверяем столкновения (чтобы не попасть в самого бросающего)
        if (age > 0.08f && step.sqrMagnitude > 0.000001f &&
            Physics.SphereCast(transform.position, 0.1f, step.normalized, out _, step.magnitude,
                ~0, QueryTriggerInteraction.Ignore))
        {
            Destroy(gameObject);
            return;
        }

        transform.position += step;
        if (age > 5f) Destroy(gameObject);
    }
}
