using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Движение персонажа от первого лица + вращение камерой мышью + приседание (Ctrl)
/// + расход выносливости (PlayerStamina, необязательна).
/// Рассчитан на кооператив (Netcode for GameObjects):
/// управление и камера работают только у владельца (Owner) персонажа,
/// позиция синхронизируется через ClientNetworkTransform.
/// Курсором во время паузы управляет PauseMenu.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Движение")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float crouchSpeed = 2f;
    [SerializeField] private float acceleration = 12f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;

    [Header("Приседание (Ctrl)")]
    [SerializeField] private float crouchHeight = 1.2f;
    [SerializeField] private float crouchTransitionSpeed = 6f;   // метров в секунду

    [Header("Камера")]
    [SerializeField] private Transform cameraPivot;      // пустой объект на уровне головы
    [SerializeField] private Camera playerCamera;        // камера — дочерняя к cameraPivot
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;

    private CharacterController controller;
    private PlayerStamina stamina;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float pitch;

    // Приседание
    private float standHeight;
    private Vector3 standCenter;
    private float standPivotY;
    private bool crouching;

    // Присел ли игрок — видят все (пригодится, например, чтобы монстр хуже замечал присевших)
    private NetworkVariable<bool> isCrouching;
    public bool IsCrouching => isCrouching.Value;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        stamina = GetComponent<PlayerStamina>();
        standHeight = controller.height;
        standCenter = controller.center;

        isCrouching = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Чужие персонажи: без камеры, звука и обработки ввода
            if (playerCamera != null)
            {
                playerCamera.enabled = false;
                var listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
            enabled = false;
            return;
        }

        if (cameraPivot == null || playerCamera == null)
        {
            Debug.LogError("PlayerController: не назначены Camera Pivot и/или Player Camera в инспекторе префаба!", this);
            return;
        }

        // Отключаем все остальные камеры сцены
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (cam != playerCamera)
            {
                cam.enabled = false;
                var l = cam.GetComponent<AudioListener>();
                if (l != null) l.enabled = false;
            }
        }

        playerCamera.enabled = true;
        var myListener = playerCamera.GetComponent<AudioListener>();
        if (myListener != null) myListener.enabled = true;

        pitch = cameraPivot.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        standPivotY = cameraPivot.localPosition.y;

        SetCursorLock(true);
    }

    private void Update()
    {
        if (cameraPivot == null) return;

        bool paused = PauseMenu.IsOpen;

        // Курсор потерян (например, клик вне окна игры) — возвращаем кликом
        if (!paused && Cursor.lockState != CursorLockMode.Locked && ClickPressed())
            SetCursorLock(true);

        if (!paused && Cursor.lockState == CursorLockMode.Locked)
            HandleLook();

        HandleCrouch(paused);
        HandleMovement(paused);
    }

    private void HandleLook()
    {
        Vector2 look = GetLookInput() * mouseSensitivity;

        transform.Rotate(Vector3.up * look.x);

        pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleCrouch(bool paused)
    {
        bool wantCrouch = !paused && CrouchHeld();

        if (wantCrouch) crouching = true;
        else if (crouching && CanStandUp()) crouching = false;   // под потолком встать нельзя

        if (isCrouching.Value != crouching)
            isCrouching.Value = crouching;

        // Плавно меняем высоту контроллера, ноги остаются на месте
        float targetHeight = crouching ? crouchHeight : standHeight;
        float h = Mathf.MoveTowards(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
        float bottom = standCenter.y - standHeight / 2f;
        controller.height = h;
        controller.center = new Vector3(standCenter.x, bottom + h / 2f, standCenter.z);

        // Камера опускается вместе с макушкой
        var p = cameraPivot.localPosition;
        cameraPivot.localPosition = new Vector3(p.x, standPivotY - (standHeight - h), p.z);
    }

    private bool CanStandUp()
    {
        float radius = controller.radius * 0.9f;
        Vector3 origin = transform.position + controller.center
                         + Vector3.up * (controller.height / 2f - controller.radius);
        float distance = (standHeight - controller.height) + 0.05f;
        return !Physics.SphereCast(origin, radius, Vector3.up, out _, distance, ~0,
            QueryTriggerInteraction.Ignore);
    }

    private void HandleMovement(bool paused)
    {
        Vector2 input = paused ? Vector2.zero : Vector2.ClampMagnitude(GetMoveInput(), 1f);
        bool moving = input.sqrMagnitude > 0.01f;

        // Бег: нужна выносливость, присев — не побежишь
        bool sprinting = false;
        if (!paused && !crouching && moving && SprintHeld() && (stamina == null || stamina.CanSprint))
        {
            sprinting = true;
            if (stamina != null) stamina.Drain(stamina.SprintDrainPerSecond * Time.deltaTime);
        }

        float targetSpeed = crouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed);
        Vector3 targetVelocity = (transform.right * input.x + transform.forward * input.y) * targetSpeed;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity, targetVelocity, acceleration * Mathf.Max(targetSpeed, walkSpeed) * Time.deltaTime);

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        // Прыжок: нельзя в приседе, стоит выносливости
        if (!paused && !crouching && controller.isGrounded && JumpPressed()
            && (stamina == null || stamina.TrySpend(stamina.JumpCost)))
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    private void SetCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ───────────── Ввод (поддержка обеих систем) ─────────────
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private Vector2 GetLookInput()
    {
        if (Mouse.current == null) return Vector2.zero;
        return Mouse.current.delta.ReadValue() * 0.05f;
    }

    private Vector2 GetMoveInput()
    {
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
    }

    private bool SprintHeld()   => Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
    private bool CrouchHeld()   => Keyboard.current != null &&
                                   (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
    private bool JumpPressed()  => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
    private bool ClickPressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    private Vector2 GetLookInput() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
    private Vector2 GetMoveInput() => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    private bool SprintHeld() => Input.GetKey(KeyCode.LeftShift);
    private bool CrouchHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    private bool JumpPressed() => Input.GetButtonDown("Jump");
    private bool ClickPressed() => Input.GetMouseButtonDown(0);
#endif
}
