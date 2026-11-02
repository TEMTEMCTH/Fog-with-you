using System.Collections;
using UnityEngine;

/// <summary>
/// Редкие пугающие звуки леса: резкое карканье ворон рядом и далёкие крики монстров.
/// Повесьте на пустой объект в ИГРОВОЙ сцене и добавьте свои звуки в поля Custom ...
/// Без своих звуков тишина (встроенные синтезированные звучат плохо, их можно включить галочкой).
/// Звуки случайные и у каждого игрока свои (не синхронизируются по сети).
/// </summary>
public class AmbientScares : MonoBehaviour
{
    [Header("Ваши звуки")]
    [Tooltip("Карканье ворон. Несколько вариантов — выбирается случайный.")]
    [SerializeField] private AudioClip[] customCaws;
    [Tooltip("Далёкие крики монстров.")]
    [SerializeField] private AudioClip[] customScreams;
    [Tooltip("Включить встроенные синтезированные звуки (звучат хуже настоящих записей).")]
    [SerializeField] private bool useBuiltInSounds = false;

    [Header("Вороны (резко и близко)")]
    [SerializeField] private float crowMinInterval = 35f;
    [SerializeField] private float crowMaxInterval = 90f;
    [SerializeField] private float crowMinDistance = 8f;
    [SerializeField] private float crowMaxDistance = 20f;
    [SerializeField, Range(0f, 1f)] private float crowVolume = 0.9f;

    [Header("Далёкие крики монстров")]
    [SerializeField] private float screamMinInterval = 90f;
    [SerializeField] private float screamMaxInterval = 220f;
    [SerializeField] private float screamMinDistance = 40f;
    [SerializeField] private float screamMaxDistance = 70f;
    [SerializeField, Range(0f, 1f)] private float screamVolume = 1f;

    [Header("Общее")]
    [SerializeField] private float firstDelay = 25f;   // тишина в начале игры

    private void Start()
    {
        StartCoroutine(CrowLoop());
        StartCoroutine(ScreamLoop());
    }

    private IEnumerator CrowLoop()
    {
        yield return new WaitForSeconds(firstDelay + Random.Range(0f, 20f));
        while (true)
        {
            yield return PlayCrows();
            yield return new WaitForSeconds(Random.Range(crowMinInterval, crowMaxInterval));
        }
    }

    private IEnumerator ScreamLoop()
    {
        yield return new WaitForSeconds(firstDelay + Random.Range(20f, 60f));
        while (true)
        {
            var clip = Pick(customScreams, useBuiltInSounds ? ProceduralSounds.Screams : null);
            if (clip != null && TryGetListenerPosition(out Vector3 center))
            {
                Vector3 pos = RandomAround(center, screamMinDistance, screamMaxDistance, 0f, 2f);
                AudioUtil.PlayAt(clip, pos, screamVolume, 20f, 120f, Random.Range(0.92f, 1.05f));
                Debug.Log("[Звуки леса] Крик монстра");
            }
            yield return new WaitForSeconds(Random.Range(screamMinInterval, screamMaxInterval));
        }
    }

    // Стая ворон: 2–4 резких крика подряд из одной точки в кронах
    private IEnumerator PlayCrows()
    {
        if (!TryGetListenerPosition(out Vector3 center)) yield break;

        Vector3 pos = RandomAround(center, crowMinDistance, crowMaxDistance, 3f, 8f);
        int count = Random.Range(2, 5);
        Debug.Log("[Звуки леса] Вороны");

        for (int i = 0; i < count; i++)
        {
            var clip = Pick(customCaws, useBuiltInSounds ? ProceduralSounds.Caws : null);
            if (clip == null) yield break;

            Vector3 jitter = new Vector3(Random.Range(-2f, 2f), Random.Range(-1f, 1f), Random.Range(-2f, 2f));
            AudioUtil.PlayAt(clip, pos + jitter, crowVolume, 4f, 70f, Random.Range(0.9f, 1.15f));
            yield return new WaitForSeconds(Random.Range(0.25f, 0.6f));
        }
    }

    private static AudioClip Pick(AudioClip[] custom, AudioClip[] fallback)
    {
        var list = (custom != null && custom.Length > 0) ? custom : fallback;
        if (list == null || list.Length == 0) return null;
        return list[Random.Range(0, list.Length)];
    }

    private static Vector3 RandomAround(Vector3 center, float minDist, float maxDist, float minHeight, float maxHeight)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float dist = Random.Range(minDist, maxDist);
        return center + new Vector3(Mathf.Cos(angle) * dist, Random.Range(minHeight, maxHeight), Mathf.Sin(angle) * dist);
    }

    // Позиция слушателя = позиция камеры локального игрока
    private static bool TryGetListenerPosition(out Vector3 position)
    {
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (l.enabled && l.gameObject.activeInHierarchy)
            {
                position = l.transform.position;
                return true;
            }
        }
        position = Vector3.zero;
        return false;
    }
}