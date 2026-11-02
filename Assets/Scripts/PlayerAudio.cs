using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Звуки игрока: шаги (ходьба / бег / присед), прыжок, приземление, приседание.
/// Работает по движению тела, поэтому ВСЕ игроки слышат чужие шаги в 3D
/// (бег слышно дальше, присед почти не слышно). Вешается на префаб игрока.
/// </summary>
public class PlayerAudio : NetworkBehaviour
{
    [Header("Громкость")]
    [SerializeField] private float walkVolume = 0.5f;
    [SerializeField] private float runVolume = 0.9f;
    [SerializeField] private float crouchVolume = 0.2f;
    [SerializeField] private float jumpVolume = 0.6f;
    [SerializeField] private float landVolume = 0.8f;

    [Header("Слышимость шагов (метры)")]
    [SerializeField] private float walkHearDistance = 14f;
    [SerializeField] private float runHearDistance = 30f;
    [SerializeField] private float crouchHearDistance = 5f;

    [Header("Длина шага (метров)")]
    [SerializeField] private float walkStride = 1.9f;
    [SerializeField] private float runStride = 2.4f;
    [SerializeField] private float crouchStride = 1.5f;

    [Header("Ваши звуки")]
    [SerializeField] private AudioClip[] customSteps;
    [SerializeField] private AudioClip customJump;
    [SerializeField] private AudioClip customLand;
    [SerializeField] private AudioClip customCrouch;
    [Tooltip("Включить встроенные синтезированные звуки (звучат хуже настоящих записей).")]
    [SerializeField] private bool useBuiltInSounds = false;

    private AudioSource source;
    private CharacterController controller;
    private PlayerController motion;

    private Vector3 lastPos;
    private Vector3 smoothHorizontal;
    private bool wasGrounded = true;
    private bool wasCrouching;
    private float stepDistance;
    private float lowestVy;
    private float silentUntil;
    private int lastStepIndex = -1;

    private const float RunSpeedThreshold = 5.5f;

    public override void OnNetworkSpawn()
    {
        controller = GetComponent<CharacterController>();
        motion = GetComponent<PlayerController>();

        var go = new GameObject("FootstepAudio");
        go.transform.SetParent(transform, false);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;

        lastPos = transform.position;
        silentUntil = Time.time + 2f;   // не шумим при появлении на карте
    }

    private void Update()
    {
        if (source == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 pos = transform.position;
        Vector3 velocity = (pos - lastPos) / dt;
        lastPos = pos;

        // Телепорт или рывок — пропускаем кадр
        if (velocity.magnitude > 40f) { silentUntil = Time.time + 1f; return; }

        float vy = velocity.y;
        var horizontal = new Vector3(velocity.x, 0f, velocity.z);
        smoothHorizontal = Vector3.Lerp(smoothHorizontal, horizontal, 1f - Mathf.Exp(-dt * 12f));
        float speed = smoothHorizontal.magnitude;

        bool crouching = motion != null && motion.IsCrouching;
        bool grounded = (IsOwner && controller != null) ? controller.isGrounded : Mathf.Abs(vy) < 0.8f;

        if (Time.time < silentUntil)
        {
            wasGrounded = grounded;
            wasCrouching = crouching;
            return;
        }

        // Приседание / вставание
        if (crouching != wasCrouching)
        {
            PlayOne(CrouchClip(), 0.35f, 6f);
            wasCrouching = crouching;
        }

        // Прыжок
        if (wasGrounded && !grounded && vy > 2f)
            PlayOne(JumpClip(), jumpVolume, 14f);

        // Приземление
        if (!grounded) lowestVy = Mathf.Min(lowestVy, vy);
        if (!wasGrounded && grounded)
        {
            if (lowestVy < -3f)
            {
                float k = Mathf.Clamp01(-lowestVy / 10f);
                PlayOne(LandClip(), landVolume * Mathf.Lerp(0.5f, 1f, k), 20f);
            }
            lowestVy = 0f;
        }
        wasGrounded = grounded;

        // Шаги
        if (grounded && speed > 0.7f)
        {
            stepDistance += speed * dt;
            float stride = crouching ? crouchStride : (speed > RunSpeedThreshold ? runStride : walkStride);
            if (stepDistance >= stride)
            {
                stepDistance -= stride;
                PlayStep(crouching, speed > RunSpeedThreshold);
            }
        }
        else if (speed < 0.3f)
        {
            stepDistance = walkStride * 0.6f;   // первый шаг после остановки — быстро
        }
    }

    private void PlayStep(bool crouching, bool running)
    {
        AudioClip[] clips = (customSteps != null && customSteps.Length > 0) ? customSteps
                          : (useBuiltInSounds ? ProceduralSounds.Steps : null);
        if (clips == null || clips.Length == 0) return;
        int index;
        do { index = Random.Range(0, clips.Length); } while (index == lastStepIndex && clips.Length > 1);
        lastStepIndex = index;

        float volume = crouching ? crouchVolume : (running ? runVolume : walkVolume);
        float distance = crouching ? crouchHearDistance : (running ? runHearDistance : walkHearDistance);

        source.pitch = Random.Range(0.9f, 1.1f);
        source.maxDistance = distance;
        source.PlayOneShot(clips[index], volume);
    }

    private AudioClip JumpClip() => customJump != null ? customJump : (useBuiltInSounds ? ProceduralSounds.Jump : null);
    private AudioClip LandClip() => customLand != null ? customLand : (useBuiltInSounds ? ProceduralSounds.Land : null);
    private AudioClip CrouchClip() => customCrouch != null ? customCrouch : (useBuiltInSounds ? ProceduralSounds.Crouch : null);

    private void PlayOne(AudioClip clip, float volume, float hearDistance)
    {
        if (clip == null) return;
        source.pitch = 1f;
        source.maxDistance = hearDistance;
        source.PlayOneShot(clip, volume);
    }
}