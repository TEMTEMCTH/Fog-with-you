using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Голосовой чат по близости: вы слышите игроков, пока они находятся в радиусе слышимости,
/// и чем они дальше, тем тише. Обычную речь слышно на 30 метров, а громкий КРИК — на 60 метров и с эхом.
/// Голос идёт через хоста по вашей же сетевой игре (без сторонних сервисов).
/// Качество «как по рации»: 16 кГц, 8 бит.
/// Хост получает голоса всех игроков, чтобы монстры могли повторять сказанное.
/// Вешается на префаб игрока.
/// </summary>
public class VoiceChat : NetworkBehaviour
{
    [Header("Дальность голоса (метры)")]
    [Tooltip("В этом радиусе голос слышно на полной громкости.")]
    [SerializeField] private float voiceMinDistance = 3f;
    [Tooltip("Обычная речь: за этим радиусом не слышно совсем. Между min и max громкость плавно падает.")]
    [SerializeField] private float voiceMaxDistance = 30f;
    [Tooltip("Громкий крик: слышно на таком расстоянии и с эхом.")]
    [SerializeField] private float shoutMaxDistance = 60f;

    [Header("Микрофон")]
    [SerializeField] private bool pushToTalk = false;       // false = голос включается сам, когда вы говорите
    [Tooltip("Чем меньше — тем чувствительнее микрофон (ловит и тихий голос, и шумы).")]
    [SerializeField] private float voiceThreshold = 0.015f;
    [Tooltip("Громкость, с которой речь считается криком. Если крик не срабатывает — уменьшите, если срабатывает от обычной речи — увеличьте.")]
    [SerializeField] private float shoutThreshold = 0.12f;
    [SerializeField] private float hangoverSeconds = 0.4f;  // сколько ещё передавать после окончания фразы
    [SerializeField] private float micGain = 1f;

    private VoicePlayback playback;

    // Микрофон
    private AudioClip micClip;
    private string micDevice;
    private int lastMicPos;
    private int decimateFactor = 1;
    private int effectiveRate = VoiceCodec.TargetRate;
    private int frameSamples = 800;
    private float accumulator;
    private int accumulatedCount;
    private readonly List<float> pending = new List<float>();
    private float[] previousFrame;
    private float lastLoudTime = -100f;
    private float lastShoutTime = -100f;
    private bool gateOpen;
    private bool shouting;
    private bool micAvailable;

    // Эффект зелья: изменение высоты голоса
    private PitchShifter shifter;
    private float pitchRatio = 1f;
    private float effectEndTime;

    // Индикатор
    private GameObject uiRoot;
    private Text indicator;

    public override void OnNetworkSpawn()
    {
        // Динамик есть у всех игроков (в том числе у вас, но свой голос вы не услышите)
        playback = VoicePlayback.Create(transform, new Vector3(0f, 0.6f, 0f), voiceMinDistance, voiceMaxDistance);

        // Хост (он появляется первым) очищает «память» прошлой игры
        if (IsServer && IsOwner) VoiceBank.Clear();

        if (!IsOwner)
        {
            enabled = false;   // у чужих игроков микрофон не нужен (их голос придёт по сети)
            return;
        }

        StartMic();
        BuildUI();
    }

    public override void OnNetworkDespawn()
    {
        if (micClip != null) Microphone.End(micDevice);
        if (uiRoot != null) Destroy(uiRoot);
    }

    // ───────────── Микрофон ─────────────

    private void StartMic()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("VoiceChat: микрофон не найден — голосовой чат отключён.");
            return;
        }

        micDevice = null;   // микрофон по умолчанию
        micClip = Microphone.Start(micDevice, true, 1, VoiceCodec.TargetRate);
        if (micClip == null) return;

        decimateFactor = Mathf.Max(1, Mathf.RoundToInt(micClip.frequency / (float)VoiceCodec.TargetRate));
        effectiveRate = micClip.frequency / decimateFactor;
        frameSamples = Mathf.Clamp(effectiveRate / 20, 160, 1000);   // кадры по ~50 мс
        micAvailable = true;
    }

    private void Update()
    {
        if (micAvailable) ReadMic();
        UpdateIndicator();
    }

    private void ReadMic()
    {
        int pos = Microphone.GetPosition(micDevice);
        if (pos < 0 || pos == lastMicPos) return;

        int total = micClip.samples;
        int count = pos - lastMicPos;
        if (count < 0) count += total;
        if (count <= 0) return;

        // Читаем новые отсчёты (с учётом того, что запись зациклена)
        var chunk = new float[count];
        if (lastMicPos + count <= total)
        {
            micClip.GetData(chunk, lastMicPos);
        }
        else
        {
            int first = total - lastMicPos;
            var a = new float[first];
            var b = new float[count - first];
            micClip.GetData(a, lastMicPos);
            micClip.GetData(b, 0);
            System.Array.Copy(a, 0, chunk, 0, first);
            System.Array.Copy(b, 0, chunk, first, b.Length);
        }
        lastMicPos = pos;

        // Понижаем частоту усреднением
        for (int i = 0; i < chunk.Length; i++)
        {
            accumulator += chunk[i];
            accumulatedCount++;
            if (accumulatedCount == decimateFactor)
            {
                float sample = Mathf.Clamp(accumulator / decimateFactor * micGain, -1f, 1f);
                if (EffectActive && shifter != null)
                {
                    shifter.Ratio = pitchRatio;
                    sample = Mathf.Clamp(shifter.Process(sample), -1f, 1f);
                }
                pending.Add(sample);
                accumulator = 0f;
                accumulatedCount = 0;
            }
        }

        // Режем на кадры и отправляем
        while (pending.Count >= frameSamples)
        {
            var frame = pending.GetRange(0, frameSamples).ToArray();
            pending.RemoveRange(0, frameSamples);
            ProcessFrame(frame);
        }
    }

    private void ProcessFrame(float[] frame)
    {
        float sum = 0f;
        for (int i = 0; i < frame.Length; i++) sum += frame[i] * frame[i];
        float rms = Mathf.Sqrt(sum / frame.Length);

        if (rms > voiceThreshold) lastLoudTime = Time.time;
        if (rms > shoutThreshold) lastShoutTime = Time.time;

        bool open = pushToTalk ? PushToTalkHeld() : (Time.time - lastLoudTime < hangoverSeconds);
        // Крик «держится» чуть дольше самого громкого момента, чтобы целое слово считалось криком
        bool shout = open && (Time.time - lastShoutTime < 0.35f);

        if (open)
        {
            // Первый кадр фразы: добавляем предыдущий, чтобы не «съедать» начало слова
            if (!gateOpen && previousFrame != null)
                SendVoiceRpc(VoiceCodec.Encode(previousFrame), effectiveRate, false);

            SendVoiceRpc(VoiceCodec.Encode(frame), effectiveRate, shout);
        }

        gateOpen = open;
        shouting = shout;
        previousFrame = frame;
    }

    // ───────────── Эффект зелья ─────────────

    /// <summary>Идёт ли сейчас эффект изменения голоса.</summary>
    public bool EffectActive => Time.time < effectEndTime;

    /// <summary>Сколько секунд эффект ещё продлится.</summary>
    public float EffectRemaining => Mathf.Max(0f, effectEndTime - Time.time);

    /// <summary>Сделать голос выше (ratio &gt; 1) на seconds секунд. Голос меняется до отправки по сети, поэтому его слышат все.</summary>
    public void StartPitchEffect(float ratio, float seconds)
    {
        pitchRatio = ratio;
        effectEndTime = Time.time + seconds;

        if (shifter == null) shifter = new PitchShifter(effectiveRate);
        shifter.Reset();
    }

    // ───────────── Сеть ─────────────

    // Игрок -> сервер (хост). Потеря отдельных кадров не страшна, поэтому без гарантии доставки.
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void SendVoiceRpc(byte[] data, int rate, bool shout)
    {
        ulong speaker = OwnerClientId;

        // Запоминаем речь: из неё монстры потом берут реплики для повтора
        VoiceBank.Feed(speaker, data, rate, shout);

        // Отправляем только тем, кто достаточно близко (крик — дальше)
        var nm = NetworkManager.Singleton;
        var targets = new List<ulong>();
        float range = (shout ? shoutMaxDistance : voiceMaxDistance) * 1.1f;
        float maxSqr = range * range;

        foreach (var kv in nm.ConnectedClients)
        {
            if (kv.Key == speaker) continue;
            var playerObject = kv.Value.PlayerObject;
            if (playerObject == null) continue;
            if ((playerObject.transform.position - transform.position).sqrMagnitude <= maxSqr)
                targets.Add(kv.Key);
        }

        if (targets.Count > 0)
            PlayVoiceRpc(data, rate, shout, RpcTarget.Group(targets, RpcTargetUse.Temp));
    }

    // Сервер -> слушатели
    [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
    private void PlayVoiceRpc(byte[] data, int rate, bool shout, RpcParams rpcParams = default)
    {
        if (playback != null)
            playback.Feed(data, rate, shout ? shoutMaxDistance : voiceMaxDistance, shout);
    }

    // ───────────── Индикатор ─────────────

    private void BuildUI()
    {
        uiRoot = new GameObject("VoiceIndicatorUI");
        var canvas = uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = uiRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var go = new GameObject("Indicator", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(uiRoot.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(40f, 90f);
        rt.sizeDelta = new Vector2(500f, 40f);

        indicator = go.AddComponent<Text>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        indicator.font = font != null ? font : Font.CreateDynamicFontFromOSFont("Arial", 26);
        indicator.fontSize = 26;
        indicator.alignment = TextAnchor.MiddleLeft;
        indicator.raycastTarget = false;
        go.AddComponent<Shadow>();
    }

    private void UpdateIndicator()
    {
        if (indicator == null) return;

        if (!micAvailable)
        {
            indicator.text = "Микрофон не найден";
            indicator.color = new Color(0.9f, 0.5f, 0.4f);
        }
        else if (shouting)
        {
            indicator.text = "● Вы кричите (слышно на " + Mathf.RoundToInt(shoutMaxDistance) + " м)";
            indicator.color = new Color(1f, 0.6f, 0.2f);
        }
        else if (gateOpen)
        {
            indicator.text = "● Вы говорите";
            indicator.color = new Color(1f, 0.35f, 0.3f);
        }
        else
        {
            indicator.text = pushToTalk ? "Микрофон: удерживайте V" : "Микрофон включён";
            indicator.color = new Color(0.75f, 0.8f, 0.85f, 0.7f);
        }

        if (EffectActive)
            indicator.text += $"   (высокий голос: ещё {Mathf.CeilToInt(EffectRemaining)} с)";
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private bool PushToTalkHeld() => Keyboard.current != null && Keyboard.current.vKey.isPressed;
#else
    private bool PushToTalkHeld() => Input.GetKey(KeyCode.V);
#endif
}