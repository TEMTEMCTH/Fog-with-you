using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 6f;
    public float gravity = -9.81f;
    public Transform cameraTransform;
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;

    private CharacterController controller;
    private Vector3 velocity;
    private float xRotation = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Если камера не назначена, попробуем найти её автоматически
        if (cameraTransform == null)
        {
            cameraTransform = Camera.main.transform;
            Debug.Log("Camera Transform был назначен автоматически.");
        }
    }

    void Update()
    {
        // Вращение мышью
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Движение WASD
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // Важно: используем TransformDirection для правильного учёта поворота
        Vector3 move = transform.TransformDirection(new Vector3(x, 0, z)) * moveSpeed;

        // Гравитация (применяем всегда, чтобы персонаж не «висел»)
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
        velocity.y += gravity * Time.deltaTime;

        // Объединяем движение и гравитацию в один вызов Move
        controller.Move((move + new Vector3(0, velocity.y, 0)) * Time.deltaTime);
    }
}