using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Движение персонажа от первого лица + вращение камерой мышью.
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
    [SerializeField] private float acceleration = 12f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;

    [Header("Камера")]
    [SerializeField] private Transform cameraPivot;      // пустой объект на уровне головы
    [SerializeField] private Camera playerCamera;        // камера — дочерняя к cameraPivot
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float pitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
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

        HandleMovement(paused);
    }

    private void HandleLook()
    {
        Vector2 look = GetLookInput() * mouseSensitivity;

        transform.Rotate(Vector3.up * look.x);

        pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement(bool paused)
    {
        // На паузе ввод игнорируем, но гравитация продолжает работать
        Vector2 input = paused ? Vector2.zero : Vector2.ClampMagnitude(GetMoveInput(), 1f);

        float targetSpeed = (!paused && SprintHeld()) ? sprintSpeed : walkSpeed;
        Vector3 targetVelocity = (transform.right * input.x + transform.forward * input.y) * targetSpeed;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity, targetVelocity, acceleration * targetSpeed * Time.deltaTime);

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (!paused && controller.isGrounded && JumpPressed())
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

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
    private bool JumpPressed()  => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
    private bool ClickPressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    private Vector2 GetLookInput() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
    private Vector2 GetMoveInput() => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    private bool SprintHeld() => Input.GetKey(KeyCode.LeftShift);
    private bool JumpPressed() => Input.GetButtonDown("Jump");
    private bool ClickPressed() => Input.GetMouseButtonDown(0);
#endif
}
