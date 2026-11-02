using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Монстр: бродит по лесу, а когда игрок подходит ближе 10 метров — идёт к нему и постепенно ускоряется.
/// Поймав игрока, поднимает его и разглядывает 3 секунды. Если за это время в монстра попадут предметом,
/// он отпускает игрока и оглушается на 5 секунд. Иначе быстро несёт пленника в ближайшую свободную клетку или яму.
/// Удар или бросок оружием (копьё, палка, камень) оглушает монстра на 5 секунд.
/// Логика работает на хосте. Движение — по прямой с обходом препятствий (без NavMesh).
/// Нужны на объекте: NetworkObject, NetworkTransform, CharacterController, MonsterAI.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class MonsterAI : NetworkBehaviour
{
    public enum State { Wander = 0, Chase = 1, Grabbing = 2, Carrying = 3, Stunned = 4 }

    [Header("Обнаружение")]
    [SerializeField] private float detectRadius = 10f;
    [Tooltip("Дальше этого расстояния монстр теряет игрока из виду.")]
    [SerializeField] private float loseRadius = 18f;
    [Tooltip("Присевшего игрока монстр замечает только на таком доле расстояния (0.5 = вдвое ближе).")]
    [SerializeField, Range(0.1f, 1f)] private float crouchDetectFactor = 0.5f;

    [Header("Скорость (метры в секунду)")]
    [SerializeField] private float wanderSpeed = 1.5f;
    [SerializeField] private float chaseStartSpeed = 2.5f;
    [SerializeField] private float chaseMaxSpeed = 6.5f;
    [Tooltip("На сколько метров в секунду растёт скорость каждую секунду погони.")]
    [SerializeField] private float chaseAcceleration = 1f;
    [SerializeField] private float carrySpeed = 8f;
    [SerializeField] private float turnSpeed = 360f;

    [Header("Захват")]
    [SerializeField] private float grabDistance = 1.7f;
    [SerializeField] private float inspectSeconds = 3f;
    [Tooltip("Где висит схваченный игрок относительно монстра.")]
    [SerializeField] private Vector3 holdOffset = new Vector3(0f, 2.3f, 0.9f);

    [Header("Оглушение")]
    [SerializeField] private float stunSeconds = 5f;
    [Tooltip("После оглушения несколько секунд монстра нельзя оглушить снова.")]
    [SerializeField] private float stunImmunitySeconds = 2f;

    [Header("Блуждание")]
    [SerializeField] private float wanderRadius = 25f;
    [SerializeField] private float gravity = -20f;

    private NetworkVariable<int> stateVar;
    private CharacterController controller;
    private Renderer bodyRenderer;

    private State state = State.Wander;
    private float stateUntil;
    private float immuneUntil;
    private float verticalVelocity;

    private Vector3 homePosition;
    private Vector3 wanderTarget;
    private float wanderWaitUntil;
    private float wanderRepickAt;

    private PlayerCapture chaseTarget;
    private float chaseTime;
    private float lostTime;

    private PlayerCapture victim;
    private CaptureSpot spot;

    public State CurrentState => state;

    private void Awake()
    {
        stateVar = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        controller = GetComponent<CharacterController>();
        bodyRenderer = GetComponentInChildren<Renderer>();

        stateVar.OnValueChanged += OnStateChanged;
        ApplyTint();

        if (IsServer)
        {
            homePosition = transform.position;
            PickWanderTarget();
        }
        else
        {
            controller.enabled = false;   // у остальных игроков монстра двигает сеть, а не CharacterController
        }
    }

    public override void OnNetworkDespawn()
    {
        stateVar.OnValueChanged -= OnStateChanged;
    }

    private void OnStateChanged(int oldValue, int newValue) => ApplyTint();

    // Цвет монстра показывает, что он делает: тёмно-красный — бродит, красный — гонится,
    // оранжевый — несёт игрока, жёлтый — оглушён
    private void ApplyTint()
    {
        if (bodyRenderer == null) return;
        Color color;
        switch ((State)stateVar.Value)
        {
            case State.Chase:    color = new Color(0.85f, 0.1f, 0.1f); break;
            case State.Grabbing:
            case State.Carrying: color = new Color(0.95f, 0.45f, 0.1f); break;
            case State.Stunned:  color = new Color(0.95f, 0.9f, 0.2f); break;
            default:             color = new Color(0.35f, 0.1f, 0.1f); break;
        }
        bodyRenderer.material.color = color;
    }

    private void Update()
    {
        if (!IsServer) return;

        float dt = Time.deltaTime;
        switch (state)
        {
            case State.Wander:   UpdateWander(dt); break;
            case State.Chase:    UpdateChase(dt); break;
            case State.Grabbing: UpdateGrabbing(dt); break;
            case State.Carrying: UpdateCarrying(dt); break;
            case State.Stunned:  UpdateStunned(dt); break;
        }
    }

    // ───────────── Состояния ─────────────

    private void UpdateWander(float dt)
    {
        PlayerCapture found = FindTarget();
        if (found != null)
        {
            chaseTarget = found;
            EnterState(State.Chase);
            return;
        }

        if (Time.time < wanderWaitUntil) { Move(Vector3.zero, dt); return; }
        if (Time.time > wanderRepickAt) PickWanderTarget();

        if (FlatDistance(transform.position, wanderTarget) < 1f)
        {
            wanderWaitUntil = Time.time + Random.Range(2f, 6f);
            PickWanderTarget();
            Move(Vector3.zero, dt);
            return;
        }

        MoveToward(wanderTarget, wanderSpeed, dt);
    }

    private void UpdateChase(float dt)
    {
        if (chaseTarget == null || chaseTarget.IsLocked)
        {
            EnterState(State.Wander);
            return;
        }

        Vector3 targetPos = chaseTarget.transform.position;
        float distance = FlatDistance(transform.position, targetPos);

        // Схватил
        if (distance <= grabDistance && Mathf.Abs(targetPos.y - transform.position.y) < 2.5f)
        {
            StartGrab(chaseTarget);
            return;
        }

        // Потерял игрока из виду
        if (distance > loseRadius)
        {
            lostTime += dt;
            if (lostTime > 2f) { EnterState(State.Wander); return; }
        }
        else
        {
            lostTime = 0f;
        }

        // Сначала идёт, потом разгоняется
        chaseTime += dt;
        float speed = Mathf.Min(chaseMaxSpeed, chaseStartSpeed + chaseAcceleration * chaseTime);
        MoveToward(targetPos, speed, dt);
    }

    private void UpdateGrabbing(float dt)
    {
        Move(Vector3.zero, dt);

        if (victim == null) { EnterState(State.Wander); return; }   // игрок отключился

        if (Time.time >= stateUntil)
        {
            spot = CaptureSpot.FindNearestFree(transform.position);
            if (spot == null)
            {
                // Некуда нести — отпускаем
                ReleaseVictim();
                wanderWaitUntil = Time.time + 3f;
                EnterState(State.Wander);
                return;
            }
            EnterState(State.Carrying);
        }
    }

    private void UpdateCarrying(float dt)
    {
        if (victim == null) { EnterState(State.Wander); return; }

        // Если ближайшую клетку заняли — ищем другую
        if (spot == null || spot.Occupied)
        {
            spot = CaptureSpot.FindNearestFree(transform.position);
            if (spot == null)
            {
                ReleaseVictim();
                EnterState(State.Wander);
                return;
            }
        }

        if (FlatDistance(transform.position, spot.transform.position) <= 1.8f)
        {
            spot.ServerImprison(victim);
            victim = null;
            spot = null;
            wanderWaitUntil = Time.time + 4f;
            EnterState(State.Wander);
            return;
        }

        MoveToward(spot.transform.position, carrySpeed, dt);
    }

    private void UpdateStunned(float dt)
    {
        Move(Vector3.zero, dt);

        if (Time.time >= stateUntil)
        {
            immuneUntil = Time.time + stunImmunitySeconds;
            wanderWaitUntil = Time.time + 1.5f;
            EnterState(State.Wander);
        }
    }

    // ───────────── Действия ─────────────

    private void StartGrab(PlayerCapture target)
    {
        victim = target;
        stateUntil = Time.time + inspectSeconds;
        EnterState(State.Grabbing);
        victim.ServerBeginCarried(NetworkObject, holdOffset);
    }

    /// <summary>Монстра ударили или в него попали предметом (вызывается только на сервере).</summary>
    public void ServerHit()
    {
        if (!IsServer) return;
        if (state == State.Stunned || Time.time < immuneUntil) return;

        ReleaseVictim();
        spot = null;
        chaseTarget = null;
        stateUntil = Time.time + stunSeconds;
        EnterState(State.Stunned);
    }

    private void ReleaseVictim()
    {
        if (victim == null) return;

        Vector3 drop = transform.position + transform.forward * 1.2f;
        if (Physics.Raycast(drop + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0,
                QueryTriggerInteraction.Ignore))
            drop = hit.point;
        drop += Vector3.up * 1.2f;   // немного выше земли — дальше игрок опустится сам

        victim.ServerEndCarried(drop);
        victim = null;
    }

    private void EnterState(State newState)
    {
        state = newState;
        stateVar.Value = (int)newState;
        if (newState == State.Chase) { chaseTime = 0f; lostTime = 0f; }
    }

    // ───────────── Поиск цели ─────────────

    private PlayerCapture FindTarget()
    {
        PlayerCapture best = null;
        float bestDistance = float.MaxValue;

        foreach (var kv in NetworkManager.Singleton.ConnectedClients)
        {
            var playerObject = kv.Value.PlayerObject;
            if (playerObject == null) continue;

            var capture = playerObject.GetComponent<PlayerCapture>();
            if (capture == null || capture.IsLocked) continue;

            float radius = detectRadius;
            var motion = playerObject.GetComponent<PlayerController>();
            if (motion != null && motion.IsCrouching) radius *= crouchDetectFactor;

            float d = FlatDistance(transform.position, playerObject.transform.position);
            if (d <= radius && d < bestDistance)
            {
                bestDistance = d;
                best = capture;
            }
        }
        return best;
    }

    // ───────────── Движение ─────────────

    private void PickWanderTarget()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        wanderTarget = homePosition + new Vector3(offset.x, 0f, offset.y);
        wanderRepickAt = Time.time + 15f;
    }

    private void MoveToward(Vector3 target, float speed, float dt)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) { Move(Vector3.zero, dt); return; }

        direction = Steer(direction.normalized);

        var look = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);

        Move(direction * speed, dt);
    }

    private void Move(Vector3 horizontalVelocity, float dt)
    {
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * dt;
        controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
    }

    // Если впереди препятствие (дерево, стена) — ищем свободное направление рядом
    private Vector3 Steer(Vector3 direction)
    {
        if (!IsBlocked(direction)) return direction;

        for (int angle = 30; angle <= 150; angle += 30)
        {
            Vector3 left = Quaternion.AngleAxis(-angle, Vector3.up) * direction;
            if (!IsBlocked(left)) return left;
            Vector3 right = Quaternion.AngleAxis(angle, Vector3.up) * direction;
            if (!IsBlocked(right)) return right;
        }
        return direction;
    }

    private bool IsBlocked(Vector3 direction)
    {
        if (!Physics.SphereCast(transform.position, controller.radius * 0.9f, direction, out RaycastHit hit, 1.8f,
                ~0, QueryTriggerInteraction.Ignore))
            return false;

        Collider c = hit.collider;
        if (c.transform.IsChildOf(transform)) return false;                 // сам монстр
        if (c.GetComponentInParent<PlayerCapture>() != null) return false;  // игроки — не препятствие
        if (c.GetComponentInParent<PickupItem>() != null) return false;     // мелкие предметы
        if (c.GetComponentInParent<CaptureSpot>() != null) return false;    // клетки и ямы
        return true;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
